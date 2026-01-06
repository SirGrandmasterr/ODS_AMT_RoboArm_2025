using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace UnityFactorySceneHDRP
{
    public class VR_Imitation_Controller : MonoBehaviour
    {
        [Header("Imitation Components")]
        [SerializeField] private RobotArm_IK_Controller _robotArmController;
        [SerializeField] private GameObject _trackingBallPrefab;
        
        [Header("VR Input Setup")]
        [Tooltip("The Transform of the Right Hand Controller (the one used for drawing).")]
        [SerializeField] private Transform _rightHandController;
        
        [Tooltip("The Transform of the VR Player Root (to move around).")]
        [SerializeField] private Transform _playerRoot;

        [Header("Button Bindings")]
        [Tooltip("Binding for the Record Button (Right Hand Trigger).")]
        [SerializeField] private string _recordBinding = "<XRController>{RightHand}/triggerPressed";
        
        [Tooltip("Binding for Movement (Left Hand Thumbstick).")]
        [SerializeField] private string _moveBinding = "<XRController>{LeftHand}/thumbstick";

        [Header("Movement Settings")]
        [SerializeField] private float _moveSpeed = 2.0f;

        // Internal State
        private bool _isRecording = false;
        private GameObject _activeTrackingBall;
        private List<Vector3> _recordedRelativePath = new List<Vector3>();

        // Anchor for recording (to make it relative to start pose)
        private GameObject _recordingAnchor;
        
        // Input Actions
        private InputAction _recordAction;
        private InputAction _moveAction;

        private void Awake()
        {
            // Initialize Input Actions
            _recordAction = new InputAction("Record", binding: _recordBinding);
            _moveAction = new InputAction("Move", binding: _moveBinding);
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<XRController>{LeftHand}/thumbstick/y")
                .With("Down", "<XRController>{LeftHand}/thumbstick/y", processors: "invert")
                .With("Left", "<XRController>{LeftHand}/thumbstick/x", processors: "invert")
                .With("Right", "<XRController>{LeftHand}/thumbstick/x");
        }

        private void OnEnable()
        {
            _recordAction.Enable();
            _moveAction.Enable();
        }

        private void OnDisable()
        {
            _recordAction.Disable();
            _moveAction.Disable();
        }

        private void Update()
        {
            if (_robotArmController == null)
            {
                Debug.LogWarning("[VR_Imitation] Robot Arm Controller is not assigned.");
                return;
            }

            // 1. Handle Recording Input
            if (_recordAction.WasPressedThisFrame())
            {
                StartRecording();
            }
            else if (_recordAction.WasReleasedThisFrame())
            {
                StopRecording();
            }

            // 2. Logic
            if (_isRecording)
            {
                HandleRecordingLogic();
            }
            
            // 3. Player Movement (Locomotion)
            HandleMovementLogic();
        }

        private void StartRecording()
        {
            _isRecording = true;
            _recordedRelativePath.Clear();

            // --- ANCHOR SETUP ---
            // Create a stationary anchor at the current position relative to base or player.
            // In CameraMoveImitation, it was _playerRoot.pos/rot.
            // Here we can use the Robot Base, or just the current Hand Position as the "Zero".
            // However, CameraMoveImitation used _playerRoot to be "Independent of the Robot".
            // Let's stick to the current Right Hand position as the start? 
            // NO, CameraMoveImitation used _playerRoot.position.
            // The goal is to record a RELATIVE path.
            // Let's create an anchor at the Robot Arm Base or the Player Root?
            // If the user moves around while recording, the anchor must be stationary in world or relative to something?
            // CameraMoveImitation made the _rigidbody Kinematic (stationary) during recording.
            // In VR, the player might move their head, but we probably want the *Environment* reference frame.
            
            _recordingAnchor = new GameObject("VR_Recording_Anchor_Temp");
            
            // We use the Robot Arm Base as the reference frame if possible, or just world zero if it's stationary.
            // But CameraMoveImitation aligned the anchor with the PLAYER.
            // This suggests the recording is "Relative to where I am standing".
            // Since we want to support OpenXR/RoomScale, let's align the anchor with the Robot Arm Base 
            // but rotated to match the player? Or just Robot Arm Base directly?
            // "RobotArmController.ProcessRecordedPath" converts relative path -> World Target using "transform.TransformPoint".
            // The RobotArmController script assumes the path is in its local space?
            // RobotArmIKController:
            // "Vector3 recordedStartPos = transform.TransformPoint(path[0]);"
            // "Vector3 actualStartPos = endEffector.position;" 
            // "pathOffset = actualStartPos - recordedStartPos;"
            // It seems it calculates an offset.
            
            // Best approach: Use the RobotArm base as the parent of our anchor creates a path relative to the robot.
            // CameraMoveImitation used _playerRoot. 
            // Let's try to mimic CameraMoveImitation logic: The anchor is spawned at _playerRoot.
            // If we use _playerRoot in VR (the XR Rig Origin), it should work similarly.
            
            if (_playerRoot != null)
            {
                _recordingAnchor.transform.position = _playerRoot.position;
                _recordingAnchor.transform.rotation = _playerRoot.rotation;
            }
            else
            {
                _recordingAnchor.transform.position = transform.position;
                _recordingAnchor.transform.rotation = transform.rotation;
            }

            // --- UPSIDE DOWN ADAPTATION (From CameraMoveImitation) ---
            if (_robotArmController != null && _robotArmController.isUpsideDown)
            {
                _recordingAnchor.transform.Rotate(0, 0, 180f, Space.Self);
            }

            // Spawn visual ball
            if (_trackingBallPrefab != null)
            {
                _activeTrackingBall = Instantiate(_trackingBallPrefab);
                if(_activeTrackingBall.GetComponent<Collider>()) 
                    Destroy(_activeTrackingBall.GetComponent<Collider>());
            }
            else
            {
                _activeTrackingBall = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _activeTrackingBall.transform.localScale = Vector3.one * 0.1f;
                Destroy(_activeTrackingBall.GetComponent<Collider>());
            }
        }

        private void HandleRecordingLogic()
        {
            if (_activeTrackingBall == null || _recordingAnchor == null || _rightHandController == null) return;

            // Target is the Hand Position
            Vector3 targetPos = _rightHandController.position;
            
            // Update Ball
            _activeTrackingBall.transform.position = targetPos;

            // Record Relative
            Vector3 relativePoint = _recordingAnchor.transform.InverseTransformPoint(targetPos);
            _recordedRelativePath.Add(relativePoint);
        }

        private void StopRecording()
        {
            _isRecording = false;

            if (_activeTrackingBall != null) Destroy(_activeTrackingBall);
            if (_recordingAnchor != null) Destroy(_recordingAnchor);

            if (_robotArmController != null && _recordedRelativePath.Count > 0)
            {
                _robotArmController.ProcessRecordedPath(_recordedRelativePath);
            }
        }

        private void HandleMovementLogic()
        {
            if (_playerRoot == null) return;

            Vector2 moveInput = _moveAction.ReadValue<Vector2>();
            if (moveInput.sqrMagnitude < 0.01f) return;

            // Move relative to Headset look direction? Or just Controller direction? 
            // Usually Headset. But we don't have Headset ref here.
            // We'll move relative to PlayerRoot forward for now, or just World X/Z.
            // Let's assume standard "Twin Stick" movement relative to the Rig orientation.
            
            Vector3 moveDir = new Vector3(moveInput.x, 0, moveInput.y);
            // Transform by player root rotation
            moveDir = _playerRoot.TransformDirection(moveDir);
            moveDir.y = 0; // Keep on floor
            
            _playerRoot.position += moveDir * _moveSpeed * Time.deltaTime;
        }
    }
}
