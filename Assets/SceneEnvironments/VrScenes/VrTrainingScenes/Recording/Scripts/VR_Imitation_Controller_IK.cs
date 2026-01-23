using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;
using System.IO;
using Unity.MLAgents.Demonstrations;
using Unity.MLAgents.Policies;

public class VR_Imitation_Controller_IK : MonoBehaviour
{
    [Header("Imitation Components")]
    [SerializeField] private RobotArm_IK_Controller _robotArmController;
    [SerializeField] private ArmAgent_IK_Recording _armAgent; 
    [SerializeField] private GameObject _trackingBallPrefab;
    [SerializeField] private GameObject _ghostHandPrefab; // Visual for "Align Here"
    
    [Header("VR Player Setup")]
    [Tooltip("The Transform of the Right Hand Controller (the one used for drawing).")]
    [SerializeField] private Transform _rightHandController;
    [Tooltip("The Transform of the VR Player Root (to move around).")]
    [SerializeField] private Transform _playerRoot;
    [Tooltip("The Locomotion System GameObject to deactivate (e.g. Move provider).")]
    [SerializeField] private GameObject _locomotionSystem;
    [Tooltip("Optional: CharacterController to disable gravity/collision.")]
    [SerializeField] private CharacterController _playerCharacterController;
    [Tooltip("Target Transform to snap the player to when driving.")]
    [SerializeField] private Transform _snapTarget;

    [Header("Drive Mode Settings")]
    [Tooltip("Scale of the player in Drive Mode (e.g., 2.0 for 2x size).")]
    [SerializeField] private float _driveScale = 1.0f;
    [Tooltip("Visuals to hide when driving (e.g. Controller Models).")]
    [SerializeField] private List<GameObject> _controllerVisualsToHide;
    
    [Header("Safety Settings")]
    [Tooltip("Distance in meters to trigger engagement.")]
    [SerializeField] private float _engagementDistance = 0.15f; 

    [Header("Button Bindings")]
    [SerializeField] private string _toggleDriveBinding = "<XRController>/primaryButton"; 
    [SerializeField] private string _grabBinding = "<XRController>/grip";
    [SerializeField] private float _wristRotationOffset = 0.0f;

    // Internal State
    private int _episodesRecorded = 0;
    private enum DriveState
    {
        Idle,
        WaitingForAlignment,
        Driving
    }
    private DriveState _currentState = DriveState.Idle;

    // State Restoration
    private Vector3 _originalPosition;
    private Quaternion _originalRotation;
    private Vector3 _originalScale;
    
    // Recording
    private DemonstrationRecorder _demoRecorder;
    private Coroutine _sessionCoroutine;
    [SerializeField] private int _episodesToRecord = 10;
    [SerializeField] private InitializationConfig.StartPoseType _recordingStartPose = InitializationConfig.StartPoseType.SlightRandom;
    
    private string _currentSessionFolder; // Relative path for the current session's demos

    // Visuals
    private GameObject _activeTrackingBall;
    private GameObject _activeGhost; // The "Ghost" to align to

    // Input Actions
    private InputAction _toggleDriveAction;
    private InputAction _grabAction;
    private bool _isGrabbing = false;

    private void Awake()
    {
        _toggleDriveAction = new InputAction("ToggleDrive", binding: _toggleDriveBinding);
        _grabAction = new InputAction("Grab", binding: _grabBinding);
    }

    private void OnEnable()
    {
        _toggleDriveAction.Enable();
        _grabAction.Enable();
    }

    private void OnDisable()
    {
        _toggleDriveAction.Disable();
        _grabAction.Disable();
    }

    private void Update()
    {
        if (_robotArmController == null || _armAgent == null) return;

        // 1. Toggle Session
        if (_toggleDriveAction.WasPressedThisFrame())
        {
            if (_currentState != DriveState.Idle) StopDriveMode();
            else StartSession();
        }

        // 2. Logic based on State
        switch (_currentState)
        {
            case DriveState.WaitingForAlignment:
                HandleAlignment();
                break;
            case DriveState.Driving:
                HandleDriveLogic();
                HandleGrabInput();
                break;
        }
    }

    private void StartSession()
    {
        Debug.Log("[VR_Imitation] Starting Session...");
        _episodesRecorded = 0;

        // --- SNAP & SCALE ---
        if (_playerRoot != null)
        {
             _originalPosition = _playerRoot.position;
             _originalRotation = _playerRoot.rotation;
             _originalScale = _playerRoot.localScale;
             
             if (_snapTarget != null)
             {
                 _playerRoot.position = _snapTarget.position;
                 _playerRoot.rotation = _snapTarget.rotation;
             }
             _playerRoot.localScale = Vector3.one * _driveScale;
        }

        if (_locomotionSystem != null) _locomotionSystem.SetActive(false);
        if (_playerCharacterController != null) _playerCharacterController.enabled = false;
        
        var bodyTransformer = _playerRoot ? _playerRoot.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer>() : null;
        if (bodyTransformer != null) bodyTransformer.enabled = false;

        if (_controllerVisualsToHide != null)
        {
            foreach (var visual in _controllerVisualsToHide) if(visual) visual.SetActive(false);
        }
        
        // 1. Ensure Agent is ready
        var config = _armAgent.GetComponent<InitializationConfig>();
        if (config) config.startPoseType = _recordingStartPose;
        
        // 1.5 Force Full Task on Environment
        var env = _armAgent.GetComponent<SortingEnvironment_Recording>();
        if (!env) env = FindFirstObjectByType<SortingEnvironment_Recording>();
        if (env) env.forceFullTaskMode = true;

        // 2. Directory Setup
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string folderName = $"Sorting_Session_{timestamp}_ik";
        string rootDemoFolder = "Demonstrations";
        
        // Ensure physical directory exists
        // Application.dataPath is ".../Assets"
        string fullPath = Path.Combine(Application.dataPath, rootDemoFolder, folderName);
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
            Debug.Log($"[VR_Imitation] Created Demo Directory: {fullPath}");
        }
        
        // Use ABSOLUTE PATH for DemonstrationDirectory
        _currentSessionFolder = fullPath;
        Debug.Log($"[VR_Imitation] Session Folder Path: {_currentSessionFolder}");

        // 3. Setup Recorder Component
        _demoRecorder = _armAgent.GetComponent<DemonstrationRecorder>();
        if (_demoRecorder == null)
            _demoRecorder = _armAgent.gameObject.AddComponent<DemonstrationRecorder>();
        
        // Reset Recorder State
        _demoRecorder.Record = false;
        _demoRecorder.enabled = false; // Closed file state

        // 4. Start Recording Coroutine
        if (_sessionCoroutine != null) StopCoroutine(_sessionCoroutine);
        _sessionCoroutine = StartCoroutine(DemonstrationSession());
    }

    private System.Collections.IEnumerator DemonstrationSession()
    {
        while (_episodesRecorded < _episodesToRecord)
        {
            // --- ALIGNMENT PHASE (NOT RECORDED) ---
            Debug.Log($"[VR_Imitation] Starting Episode {_episodesRecorded + 1}");
            _currentState = DriveState.WaitingForAlignment; 

            // Force Agent Reset (Randomizes Pose)
            _armAgent.preventSpawning = true; 
            _armAgent.EndEpisode(); 
            yield return null;
            
            // Disable Agent Step while aligning
            _armAgent.enabled = false;

            SpawnGhostAtRobotEE();

            // Wait for User to Align
            while (_currentState == DriveState.WaitingForAlignment)
            {
                 if (_sessionCoroutine == null) yield break; 
                yield return null;
            }

            // --- DRIVE & RECORD (ACTUAL EPISODE) ---
            if (_currentState != DriveState.Driving) break; 

            
            _armAgent.enabled = true;
            _armAgent.preventSpawning = false; // Allow bottle spawn
            
            // 1. Manually Reset Environment/State
            // calling EndEpisode() here raises a "Done" flag which creates a ghost episode.
            // So we manually call OnEpisodeBegin to reset logic without triggering the ML-Agents EndEpisode cycle.
            _armAgent.OnEpisodeBegin(); 
            
            // 2. NOW Enable Recorder
            _demoRecorder.Close(); // Ensure clean state
            _demoRecorder.DemonstrationDirectory = _currentSessionFolder;
            _demoRecorder.DemonstrationName = $"Episode-{_episodesRecorded}";
            
            Debug.Log($"[VR_Imitation] Enabling Recorder for {_demoRecorder.DemonstrationName}");
            _demoRecorder.enabled = true; 
            _demoRecorder.Record = true;

            // Wait for Episode Completion
            bool episodeFinished = false;
            ArmAgent_IK_Recording.EpisodeCompleteHandler onFinish = (bool s, int c) => { episodeFinished = true; };
            _armAgent.OnEpisodeCompleted += onFinish;
            
            while (!episodeFinished && _currentState == DriveState.Driving)
            {
                yield return null;
            }
            
            // --- STOP RECORDING & CLOSE FILE ---
            // Note: The ArmAgent already called Close() internally inside FinishEpisode
            // to prevent the "Next Episode Start" from leaking. 
            // We call it here again to be safe and ensure the file handle is released.
            _demoRecorder.Record = false;
            _demoRecorder.enabled = false; 
            _demoRecorder.Close(); 
            
            _armAgent.OnEpisodeCompleted -= onFinish;

            if (_currentState != DriveState.Driving) break; 

            _episodesRecorded++; 
            
            // Disengage Control for next reset
            DisengageControl(); 
            yield return new WaitForSeconds(0.5f);
        }
        
        StopDriveMode();
    }
    
    private void SpawnGhostAtRobotEE()
    {
        if (_activeGhost != null) Destroy(_activeGhost);
        
        Vector3 spawnPos = _armAgent.endEffector != null ? _armAgent.endEffector.position : _armAgent.transform.position;

        if (_ghostHandPrefab)
        {
             _activeGhost = Instantiate(_ghostHandPrefab, spawnPos, Quaternion.identity);
             var col = _activeGhost.GetComponent<Collider>();
             if(col) Destroy(col);
        }
        else
        {
             _activeGhost = GameObject.CreatePrimitive(PrimitiveType.Sphere);
             _activeGhost.transform.position = spawnPos;
             _activeGhost.transform.localScale = Vector3.one * 0.1f;
             var r = _activeGhost.GetComponent<Renderer>();
             if(r) r.material.color = new Color(0, 1, 1, 0.5f); 
             Destroy(_activeGhost.GetComponent<Collider>());
        }
    }

    private void HandleAlignment()
    {
        if (_activeGhost == null || _rightHandController == null) return;

        float dist = Vector3.Distance(_rightHandController.position, _activeGhost.transform.position);
        
        var r = _activeGhost.GetComponent<Renderer>();
        if (r) r.material.color = (dist < _engagementDistance) ? Color.green : Color.cyan;

        if (dist < _engagementDistance)
        {
            EngageControl();
        }
    }

    private void EngageControl()
    {
        Debug.Log("[VR_Imitation] <color=green>ENGAGED</color>");
        _currentState = DriveState.Driving;
        
        if (_activeGhost) Destroy(_activeGhost);

        if (_trackingBallPrefab && _activeTrackingBall == null)
        {
            _activeTrackingBall = Instantiate(_trackingBallPrefab);
             if(_activeTrackingBall.GetComponent<Collider>()) Destroy(_activeTrackingBall.GetComponent<Collider>());
        }

        _armAgent.SetExternalControl(true);
        _armAgent.SetIKController(_robotArmController);
        
        var behaviorParams = _armAgent.GetComponent<BehaviorParameters>();
        if (behaviorParams) behaviorParams.BehaviorType = BehaviorType.HeuristicOnly;
    }

    private void DisengageControl()
    {
        _armAgent.SetExternalControl(false);
        if (_activeTrackingBall) Destroy(_activeTrackingBall);
    }

    private void StopDriveMode()
    {
        Debug.Log("[VR_Imitation] Stopping Drive Mode.");
        
        if (_armAgent != null)
        {
             _armAgent.SetExternalControl(false);
        }

        _currentState = DriveState.Idle;
        if (_sessionCoroutine != null) StopCoroutine(_sessionCoroutine);
        
        DisengageControl();
        
        // Restore Environment Mode
        if (_armAgent)
        {
             var env = _armAgent.GetComponent<SortingEnvironment_Recording>();
             if (!env) env = FindFirstObjectByType<SortingEnvironment_Recording>();
             if (env) env.forceFullTaskMode = false;
        }
        
        if (_activeGhost) Destroy(_activeGhost);
        if (_demoRecorder) 
        {
            _demoRecorder.Record = false;
            _demoRecorder.enabled = false;
        }

        // --- RESTORE STATE ---
        if (_playerRoot != null)
        {
            _playerRoot.position = _originalPosition;
            _playerRoot.rotation = _originalRotation;
            _playerRoot.localScale = _originalScale;
        }
        
        if (_locomotionSystem != null) _locomotionSystem.SetActive(true);
        if (_playerCharacterController != null) _playerCharacterController.enabled = true;

        var bodyTransformer = _playerRoot ? _playerRoot.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer>() : null;
        if (bodyTransformer != null) bodyTransformer.enabled = true;

        if (_controllerVisualsToHide != null)
        {
            foreach (var visual in _controllerVisualsToHide) if(visual) visual.SetActive(true);
        }
    }

    private void HandleDriveLogic()
    {
        if (_rightHandController == null) return;

        Vector3 targetPos = _rightHandController.position;
        float wristAngle = _rightHandController.localEulerAngles.z + _wristRotationOffset;
        
        _armAgent.SetExternalTarget(targetPos, wristAngle);

        if (_activeTrackingBall) _activeTrackingBall.transform.position = targetPos;
        
        _armAgent.RequestDecision(); 
    }

    private void HandleGrabInput()
    {
        if (_armAgent == null) return;
        
        float grabValue = _grabAction.ReadValue<float>();
        bool isPressing = grabValue > 0.5f;

        if (isPressing && !_isGrabbing) 
        {
            _isGrabbing = true;
            _armAgent.externalClawSignal = true; 
        }
        else if (!isPressing && _isGrabbing)
        {
             _isGrabbing = false;
             _armAgent.externalClawSignal = false; 
        }
    }
}