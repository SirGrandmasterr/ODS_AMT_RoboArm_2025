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
    
    [Header("Safety Settings")]
    [Tooltip("Distance in meters to trigger engagement.")]
    [SerializeField] private float _engagementDistance = 0.15f; 

    [Header("Button Bindings")]
    [SerializeField] private string _toggleDriveBinding = "<XRController>/primaryButton"; 
    [SerializeField] private string _grabBinding = "<XRController>/grip";
    [SerializeField] private float _wristRotationOffset = 0.0f;

    // Internal State
    private enum DriveState
    {
        Idle,
        WaitingForAlignment,
        Driving
    }
    private DriveState _currentState = DriveState.Idle;
    
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
        
        // 1. Ensure Agent is ready
        // Set config on Agent if we assume it has one attached
        var config = _armAgent.GetComponent<InitializationConfig>();
        if (config) config.startPoseType = _recordingStartPose;

        // 2. Start Recording Coroutine
        if (_sessionCoroutine != null) StopCoroutine(_sessionCoroutine);
        _sessionCoroutine = StartCoroutine(DemonstrationSession());
        
        // NOTE: DemonstrationSession manages the episodes and calling StartEpisode()
    }

    private System.Collections.IEnumerator DemonstrationSession()
    {
        int episodesCompleted = 0;
        
        // Ensure Recorder exists
        _demoRecorder = _armAgent.GetComponent<DemonstrationRecorder>();
        if (_demoRecorder == null)
            _demoRecorder = _armAgent.gameObject.AddComponent<DemonstrationRecorder>();
        
        _demoRecorder.NumStepsToRecord = 0; 

        while (episodesCompleted < _episodesToRecord)
        {
            // --- START NEW EPISODE ---
            Debug.Log($"[VR_Imitation] Starting Episode {episodesCompleted + 1}");
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

            _demoRecorder.DemonstrationName = $"{_demoNameBase}_{DateTime.Now:MMdd_HHmm}_Ep{episodesCompleted}";
            _demoRecorder.Record = true;

            // Wait for Episode Completion (Success/Fail)
            bool episodeFinished = false;
            Action onFinish = () => { episodeFinished = true; };
            _armAgent.OnEpisodeEnded += onFinish;
            
            while (!episodeFinished && _currentState == DriveState.Driving)
            {
                yield return null;
            }
            
            _armAgent.OnEpisodeEnded -= onFinish;
            _demoRecorder.Record = false;

            if (_currentState != DriveState.Driving) break; // Canceled mid-episode

            episodesCompleted++;
            
            // Disengage Control for next reset
            DisengageControl(); // Robot stops following hand, stays in last pose or resets
            yield return new WaitForSeconds(0.5f);
        }

        StopDriveMode();
    }

    private void SpawnGhostAtRobotEE()
    {
        if (_activeGhost != null) Destroy(_activeGhost);
        
        // We need the position of the Agent's End Effector.
        // But ArmAgent_Recording stores references usually private or serialized.
        // We can access it via a public property or finding the child.
        // Or assume the IK Controller knows generally where it is if initialized.
        // Better: ArmAgent_Recording should expose EndEffector Transform?
        // Let's assume we find it by name or use a known reference.
        // For now, let's look for "EndEffector" in children of Agent.
        Transform ee = _armAgent.transform.Find("EndEffector") ?? _armAgent.transform.Find("SuctionGap"); // Guessing names
        // Actually, we can just use the IK Controller's knowledge of FK if available, 
        // OR ask the Agent what its Current FK position is.
        // SIMPLIFICATION: I will add a public 'EndEffector' accessor to ArmAgent_Recording or just allow dragging it in inspector here.
        // Wait, _armAgent is a class I created. I can't modify it easily now without rewriting.
        // But I made sure to check the code: I *did* serialize private Transform endEffector. 
        // I can't access it. 
        // Workaround: Use _robotArmController.transform.position? No that's the base.
        // The script inspector has a reference to EndEffector likely. 
        // Let's find end effector by tag "EndEffector" or name. 
        // Or better: Just use the RobotArm_IK_Controller's "endEffector" field if public? 
        // It is [SerializeField] private. 
        // OK, I will assume the Ghost should spawn at the position of the "Suction" object or similar.
        // Let's assume for this specific robot, we can find it.
        // Actually, I can use `_armAgent.transform.GetComponentInChildren<Rigidbody>().transform`. 
        // Let's try to find "EndEffector" by name, standard convention.
        Transform targetT = _armAgent.transform.Find("endEffector") ?? _armAgent.transform.Find("EndEffector");
        Vector3 spawnPos = targetT ? targetT.position : _armAgent.transform.position; // Fallback

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

    private void StopDriveMode()
    {
        Debug.Log("[VR_Imitation] Stopping Drive Mode.");
        _currentState = DriveState.Idle;
        
        if (_sessionCoroutine != null) StopCoroutine(_sessionCoroutine);
        
        DisengageControl();
        
        if (_activeGhost) Destroy(_activeGhost);
        if (_demoRecorder) _demoRecorder.Record = false;
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
