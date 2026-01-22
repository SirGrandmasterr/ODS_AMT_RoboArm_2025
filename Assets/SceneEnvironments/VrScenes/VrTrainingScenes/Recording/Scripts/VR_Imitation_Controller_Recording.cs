using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;
using Unity.MLAgents.Demonstrations;
using Unity.MLAgents.Policies;

public class VR_Imitation_Controller_Recording : MonoBehaviour
{
    [Header("Imitation Components")]
    [SerializeField] private RobotArm_IK_Controller _robotArmController;
    [SerializeField] private ArmAgent_Recording _armAgent; 
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
    [SerializeField] private string _demoNameBase = "Sorting_Demo_User";
    [SerializeField] private int _episodesToRecord = 10;
    [SerializeField] private InitializationConfig.StartPoseType _recordingStartPose = InitializationConfig.StartPoseType.SlightRandom;

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
        // Set config on Agent if we assume it has one attached
        var config = _armAgent.GetComponent<InitializationConfig>();
        if (config) config.startPoseType = _recordingStartPose;
        
        // 1.5 Force Full Task on Environment
        var env = _armAgent.GetComponent<SortingEnvironment_Recording>();
        if (!env) env = FindFirstObjectByType<SortingEnvironment_Recording>();
        if (env) env.forceFullTaskMode = true;

        // 2. Start Recording Coroutine
        if (_sessionCoroutine != null) StopCoroutine(_sessionCoroutine);
        _sessionCoroutine = StartCoroutine(DemonstrationSession());
        
        // NOTE: DemonstrationSession manages the episodes and calling StartEpisode()
    }

    private System.Collections.IEnumerator DemonstrationSession()
    {
        // Ensure Recorder exists
        _demoRecorder = _armAgent.GetComponent<DemonstrationRecorder>();
        if (_demoRecorder == null)
            _demoRecorder = _armAgent.gameObject.AddComponent<DemonstrationRecorder>();
        
        _demoRecorder.NumStepsToRecord = 0; 

        while (_episodesRecorded < _episodesToRecord)
        {
            // --- START NEW EPISODE ---
            Debug.Log($"[VR_Imitation] Starting Episode {_episodesRecorded + 1}");
            _currentState = DriveState.WaitingForAlignment; // Need alignment for every new start if randomized!

            // Force Agent Reset (Randomizes Pose)
            _armAgent.EndEpisode(); 
            // Wait a frame for initialization to settle
            yield return null;

            // Spawn Ghost at actual robot position
            // Since we just reset, the robot is at a new random pose.
            SpawnGhostAtRobotEE();

            // Wait for User to Align
            while (_currentState == DriveState.WaitingForAlignment)
            {
                // User can cancel via button, handled in Update()
                 if (_sessionCoroutine == null) yield break; // Safety check
                yield return null;
            }

            // --- DRIVE & RECORD ---
            // If we are here, state became Driving (Engagement Success)
            if (_currentState != DriveState.Driving) break; // Canceled?

            _demoRecorder.DemonstrationName = $"{_demoNameBase}_{DateTime.Now:MMdd_HHmm}_Ep{_episodesRecorded}";
            _demoRecorder.Record = true;

            // Wait for Episode Completion (Success/Fail)
            bool episodeFinished = false;
            ArmAgent_Recording.EpisodeCompleteHandler onFinish = (bool s, int c) => { episodeFinished = true; };
            _armAgent.OnEpisodeCompleted += onFinish;
            
            while (!episodeFinished && _currentState == DriveState.Driving)
            {
                yield return null;
            }
            
            _armAgent.OnEpisodeCompleted -= onFinish;
            _demoRecorder.Record = false;

            if (_currentState != DriveState.Driving) break; // Canceled mid-episode

            _episodesRecorded++; // Use class field
            
            // Disengage Control for next reset
            DisengageControl(); // Robot stops following hand, stays in last pose or resets
            yield return new WaitForSeconds(0.5f);
        }

        StopDriveMode();
    }
    
    private void SpawnGhostAtRobotEE()
    {
        if (_activeGhost != null) Destroy(_activeGhost);
        
        // Use the exposed EndEffector from the Agent
        Vector3 spawnPos = _armAgent.EndEffector != null ? _armAgent.EndEffector.position : _armAgent.transform.position;

        if (_ghostHandPrefab)
        {
             _activeGhost = Instantiate(_ghostHandPrefab, spawnPos, Quaternion.identity);
             // Make sure it doesn't collide
             var col = _activeGhost.GetComponent<Collider>();
             if(col) Destroy(col);
        }
        else
        {
             _activeGhost = GameObject.CreatePrimitive(PrimitiveType.Sphere);
             _activeGhost.transform.position = spawnPos;
             _activeGhost.transform.localScale = Vector3.one * 0.1f;
             var r = _activeGhost.GetComponent<Renderer>();
             if(r) r.material.color = new Color(0, 1, 1, 0.5f); // Cyan transparent
             Destroy(_activeGhost.GetComponent<Collider>());
        }
    }

    private void HandleAlignment()
    {
        if (_activeGhost == null || _rightHandController == null) return;

        float dist = Vector3.Distance(_rightHandController.position, _activeGhost.transform.position);
        
        // Feedback? Turn Ghost Green?
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

        // Visual
        if (_trackingBallPrefab && _activeTrackingBall == null)
        {
            _activeTrackingBall = Instantiate(_trackingBallPrefab);
             if(_activeTrackingBall.GetComponent<Collider>()) Destroy(_activeTrackingBall.GetComponent<Collider>());
        }

        // Enable Agent
        _armAgent.SetExternalControl(true);
        _armAgent.useIKHeuristic = true; 
        _armAgent.SetIKController(_robotArmController);
        
        // Force Heuristic Mode
        var behaviorParams = _armAgent.GetComponent<BehaviorParameters>();
        if (behaviorParams) behaviorParams.BehaviorType = BehaviorType.HeuristicOnly;
    }

    private void DisengageControl()
    {
        _armAgent.SetExternalControl(false);
        _armAgent.useIKHeuristic = false;
        
        // Stop Visuals
        if (_activeTrackingBall) Destroy(_activeTrackingBall);
    }

    private void HandleEpisodeCompleted(bool success, int steps)
    {
        float time = steps * Time.fixedDeltaTime;
        string result = success ? "<color=green>SUCCESS</color>" : "<color=red>FAIL</color>";
        Debug.Log($"<b>[VR RECORDER]</b> Episode {_episodesRecorded + 1}: {result} | Steps: {steps} | Time: {time:F2}s");
    }

    private void StopDriveMode()
    {
        Debug.Log("[VR_Imitation] Stopping Drive Mode.");
        
        if (_armAgent != null)
        {
             _armAgent.SetExternalControl(false);
             _armAgent.OnEpisodeCompleted -= HandleEpisodeCompleted;
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
        if (_demoRecorder) _demoRecorder.Record = false;

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
        
        if (_robotArmController)
        {
             _robotArmController.SetLiveTarget(targetPos, wristAngle);
        }

        if (_activeTrackingBall) _activeTrackingBall.transform.position = targetPos;
        
        _armAgent.RequestDecision(); // Force Agent Step
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
            // Also update Init Settings if needed? No.
        }
        else if (!isPressing && _isGrabbing)
        {
             _isGrabbing = false;
             _armAgent.externalClawSignal = false; 
        }
    }
}
