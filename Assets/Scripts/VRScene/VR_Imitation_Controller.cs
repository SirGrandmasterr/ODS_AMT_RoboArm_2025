using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System;

namespace UnityFactorySceneHDRP
{
    public class VR_Imitation_Controller : MonoBehaviour
    {
        [Header("Imitation Components")]
        [SerializeField] private RobotArm_IK_Controller _robotArmController;
        [SerializeField] private ArmAgentSorting_Curriculum _armAgent; // Reference to the Agent for Grab/Release
        [SerializeField] private GameObject _trackingBallPrefab;
        
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

        [Header("Button Bindings")]
        [Tooltip("Binding for the Record/Drive Mode Toggle. Default: Primary Button (A/X).")]
        [SerializeField] private string _toggleDriveBinding = "<XRController>/primaryButton"; 
        
        [Tooltip("Binding for Grabbing. Default: Grip Axis (Analog).")]
        [SerializeField] private string _grabBinding = "<XRController>/grip";

        [Tooltip("Binding for Movement. Default: Thumbstick.")]
        [SerializeField] private string _moveBinding = "<XRController>/thumbstick";

        [Header("Movement Settings")]
        [SerializeField] private float _moveSpeed = 2.0f;
        
        [Header("Drive Mode Settings")]
        [Tooltip("Scale of the player in Drive Mode (e.g., 2.0 for 2x size).")]
        [SerializeField] private float _driveScale = 1.0f;
        
        [Tooltip("Visuals to hide when driving (e.g. Controller Models).")]
        [SerializeField] private List<GameObject> _controllerVisualsToHide;

        [Tooltip("Offset for mapped wrist rotation.")]
        [SerializeField] private float _wristRotationOffset = 0.0f;

        // Internal State
        private bool _isDriveMode = false;
        private bool _isGrabbing = false;
        
        // State Restoration
        private Vector3 _originalPosition;
        private Quaternion _originalRotation;
        private Vector3 _originalScale;
        
        // Recording
        private StringBuilder _csvRecording;
        private float _recordingStartTime;

        // Visuals
        private GameObject _activeTrackingBall;
        
        // Input Actions
        private InputAction _toggleDriveAction;
        private InputAction _grabAction;
        private InputAction _moveAction;

        private void Awake()
        {
            // Initialize Input Actions
            _toggleDriveAction = new InputAction("ToggleDrive", binding: _toggleDriveBinding);
            _grabAction = new InputAction("Grab", binding: _grabBinding);
            _moveAction = new InputAction("Move", binding: _moveBinding);
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<XRController>{LeftHand}/thumbstick/y")
                .With("Down", "<XRController>{LeftHand}/thumbstick/y", processors: "invert")
                .With("Left", "<XRController>{LeftHand}/thumbstick/x", processors: "invert")
                .With("Right", "<XRController>{LeftHand}/thumbstick/x");
        }

        private void OnEnable()
        {
            _toggleDriveAction.Enable();
            _grabAction.Enable();
            _moveAction.Enable();
        }

        private void OnDisable()
        {
            _toggleDriveAction.Disable();
            _grabAction.Disable();
            _moveAction.Disable();
        }

        private void Update()
        {
            if (_robotArmController == null) return;

            // 1. Toggle Drive Mode
            if (_toggleDriveAction.WasPressedThisFrame())
            {
                if (_isDriveMode) StopDriveMode();
                else StartDriveMode();
            }

            // 2. Drive Mode Logic
            if (_isDriveMode)
            {
                HandleDriveLogic();
                HandleGrabInput();
            }
            else
            {
                // 3. Normal Player Movement (Locomotion) - Only when NOT driving
                HandleMovementLogic();
            }
        }

        private void StartDriveMode()
        {
            if (_armAgent == null)
            {
                Debug.LogWarning("[VR_Imitation] ArmAgent not assigned. Cannot guide.");
                return;
            }

            _isDriveMode = true;
            Debug.Log("[VR_Imitation] Starting Drive Mode...");

            // --- HIDE CONTROLLERS ---
            if (_controllerVisualsToHide != null)
            {
                foreach (var visual in _controllerVisualsToHide)
                {
                    if (visual != null) visual.SetActive(false);
                }
            }

            // --- SAVE STATE ---
            if (_playerRoot != null)
            {
                _originalPosition = _playerRoot.position;
                _originalRotation = _playerRoot.rotation;
                _originalScale = _playerRoot.localScale;
            }

            // --- DEBUG: Analyzed Devices ---
            Debug.Log("[VR_Imitation] Analyzing Input Devices...");
            foreach (var dev in InputSystem.devices)
            {
                string usages = string.Join(",", dev.usages);
                Debug.Log($"[Device] '{dev.name}' (Class: {dev.GetType().Name})\n   Path: {dev.path}\n   Usages: [{usages}]");

                // Dump controls for Index/Controller to see what they are called
                if (dev.name.Contains("Index") || dev.name.Contains("Controller"))
                {
                    Debug.Log($"   -> Dumping Controls for {dev.name}:");
                    foreach(var c in dev.allControls)
                    {
                        // Log first few or specific interesting ones
                        if(c.name.Contains("trigger") || c.name.Contains("grip") || c.name.Contains("primary"))
                            Debug.Log($"      - {c.name} (Path: {c.path}) Type: {c.GetType().Name}");
                    }
                }
            }
            // ---------------------------

            // --- LOCK & SNAP ---
            if (_locomotionSystem != null) _locomotionSystem.SetActive(false);
            if (_playerCharacterController != null) _playerCharacterController.enabled = false;
            
            // Try to disable XRBodyTransformer to stop spam
            var bodyTransformer = _playerRoot.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer>();
            if (bodyTransformer != null) bodyTransformer.enabled = false;

            if (_snapTarget != null && _playerRoot != null)
            {
                // Snap Position & Rotation
                _playerRoot.position = _snapTarget.position;
                _playerRoot.rotation = _snapTarget.rotation;
                
                // Apply Scale
                _playerRoot.localScale = Vector3.one * _driveScale;
            }

            // Take control of Agent
            _armAgent.SetExternalControl(true);

            // Init Recording
            _csvRecording = new StringBuilder();
            _csvRecording.AppendLine("Time,Base,First,Small,Drill,Claw"); // Header
            _recordingStartTime = Time.time;

            // Visual Cue?
            if (_trackingBallPrefab != null)
            {
                 _activeTrackingBall = Instantiate(_trackingBallPrefab);
                 if(_activeTrackingBall.GetComponent<Collider>()) Destroy(_activeTrackingBall.GetComponent<Collider>());
            }
        }

        // ... StopDriveMode ...

        private void LogAllControllerInputs()
        {
            if (Time.frameCount % 30 != 0) return; 

            var devices = InputSystem.devices;
            foreach (var device in devices)
            {
                // Filter for likely controllers to avoid spamming from Keyboard/Mouse
                if (!device.name.ToLower().Contains("controller") && !device.name.ToLower().Contains("hand")) continue;

                foreach (var control in device.allControls)
                {
                    // Check for floats (Triggers, Grips)
                    if (control is InputControl<float> floatControl && floatControl.ReadValue() > 0.05f)
                    {
                        Debug.Log($"[InputDebug] Device: {device.name} | Control: {control.name} | Value: {floatControl.ReadValue():F2}");
                    }
                    // Check for bools (Buttons)
                    else if (control is InputControl<bool> boolControl && boolControl.ReadValue())
                    {
                         // Filter out "AnyKey" or excessively noisy synthetics if needed, but for now show all
                        Debug.Log($"[InputDebug] Device: {device.name} | Control: {control.name} | State: Pressed");
                    }
                }
            }
        }

        private void StopDriveMode()
        {
            _isDriveMode = false;
            Debug.Log("[VR_Imitation] Stopping Drive Mode...");

            // --- SHOW CONTROLLERS ---
            if (_controllerVisualsToHide != null)
            {
                foreach (var visual in _controllerVisualsToHide)
                {
                    if (visual != null) visual.SetActive(true);
                }
            }

            // Release Agent
            if(_armAgent) _armAgent.SetExternalControl(false);

            // Re-enable Movement
            if (_locomotionSystem != null) _locomotionSystem.SetActive(true);
            if (_playerCharacterController != null) _playerCharacterController.enabled = true;
            
            var bodyTransformer = _playerRoot.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer>();
            if (bodyTransformer != null) bodyTransformer.enabled = true;
            
            // --- RESTORE STATE ---
            if (_playerRoot != null)
            {
                _playerRoot.position = _originalPosition;
                _playerRoot.rotation = _originalRotation;
                _playerRoot.localScale = _originalScale;
            }

            // Destroy Visuals
            if (_activeTrackingBall != null) Destroy(_activeTrackingBall);

            // Save Recording
            SaveRecordingToFile();
        }

        private void HandleDriveLogic()
        {
            if (_rightHandController == null) return;

            Vector3 targetPos = _rightHandController.position;

            // Map Controller Roll (Z) to Drill Y
            float wristAngle = _rightHandController.localEulerAngles.z + _wristRotationOffset;
            
            // Update IK
            _robotArmController.SetLiveTarget(targetPos, wristAngle);

            // Visuals
            if (_activeTrackingBall) _activeTrackingBall.transform.position = targetPos;

            // Record Frame
            if (_csvRecording != null)
            {
                float time = Time.time - _recordingStartTime;
                // Get Angles from IK Controller (which reads from Transforms)
                float baseA = _robotArmController.out_BaseY;
                float firstA = _robotArmController.out_FirstY;
                float smallA = _robotArmController.out_SmallY;
                float drillA = _robotArmController.out_DrillY;
                // Claw state: we can use a float representation. 0 = Open, 1 = Closed.
                float clawVal = _isGrabbing ? 1.0f : 0.0f;

                _csvRecording.AppendLine($"{time:F4},{baseA:F4},{firstA:F4},{smallA:F4},{drillA:F4},{clawVal:F1}");
            }
        }

        [Header("Debug Settings")]
        [SerializeField] private bool _showInputDebug = true; // Enabled by default for debugging

        private void HandleGrabInput()
        {
            Debug.Log("Test");
            if (_armAgent == null) 
            {
                if (Time.frameCount % 60 == 0) Debug.LogWarning("[VR_Imitation] HandleGrabInput: ArmAgent is NULL!");
                return;
            }

            float grabValue = _grabAction.ReadValue<float>();
            
            // Log only significant changes or non-zero to avoid spam, but initially logging everything helps.
            if (_showInputDebug && grabValue > 0.01f)
            {
                // Debug.Log($"[VR_Imitation] Grab Action Raw Value from Binding '{_grabBinding}': {grabValue}");
            }

            bool isPressing = grabValue > 0.5f;

            if (isPressing && !_isGrabbing)
            {
                Debug.Log($"[VR_Imitation] Grabbing! (Value: {grabValue})");
                _isGrabbing = true;
                _armAgent.Grab();
                _armAgent.SetClawState(true);
            }
            else if (!isPressing && _isGrabbing)
            {
                Debug.Log($"[VR_Imitation] Releasing! (Value: {grabValue})");
                _isGrabbing = false;
                _armAgent.Release();
                _armAgent.SetClawState(false);
            }

            if (_showInputDebug)
            {
                LogAllControllerInputs();
            }
        }



        private void SaveRecordingToFile()
        {
            if (_csvRecording == null || _csvRecording.Length == 0) return;

            string filename = $"RobotArm_Recording_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string path = Path.Combine(Application.persistentDataPath, "Recordings");
            
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            
            string fullPath = Path.Combine(path, filename);
            
            File.WriteAllText(fullPath, _csvRecording.ToString());
            Debug.Log($"[VR_Imitation] Recording saved to: {fullPath}");
        }

        private void HandleMovementLogic()
        {
            if (_playerRoot == null) return;

            Vector2 moveInput = _moveAction.ReadValue<Vector2>();
            if (moveInput.sqrMagnitude < 0.01f) return;

            Vector3 moveDir = new Vector3(moveInput.x, 0, moveInput.y);
            moveDir = _playerRoot.TransformDirection(moveDir);
            moveDir.y = 0; 
            
            _playerRoot.position += moveDir * _moveSpeed * Time.deltaTime;
        }
    }
}
