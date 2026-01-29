using UnityEngine;
using System.Collections.Generic;

namespace UnityFactorySceneHDRP
{
    public class CameraMove_Imitation : MonoBehaviour
    {
        [Header("Imitation Components")]
        [SerializeField] private RobotArm_IK_Controller _robotArmController;
        [SerializeField] private GameObject _trackingBallPrefab; 
        
        [Header("Movement Settings")]
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private Transform _playerRoot;
        [SerializeField] private Transform _camera;
        [SerializeField] private float _moveSpeed = 2;
        [SerializeField] private float _rotateSpeed = 2;

        // Internal State
        private float _yaw = 0;
        private float _tilt = 0;
        private bool _isRecording = false;
        private GameObject _activeTrackingBall;
        private List<Vector3> _recordedRelativePath = new List<Vector3>();

        // --- NEW: Anchor for recording ---
        private GameObject _recordingAnchor;

        private void Awake()
        {
            _yaw = _playerRoot.eulerAngles.y;
            _tilt = _camera.localEulerAngles.x;

            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            // 1. Handle Input for Recording (Mouse Button 3 / Middle Mouse)
            if (Input.GetMouseButtonDown(2))
            {
                StartRecording();
            }
            else if (Input.GetMouseButtonUp(2))
            {
                StopRecording();
            }

            // 2. Camera Rotation
            if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
            {
                _yaw += Input.GetAxis("Mouse X") * _rotateSpeed;
                _tilt -= Input.GetAxis("Mouse Y") * _rotateSpeed;
                _tilt = Mathf.Clamp(_tilt, -89, 89);
                _camera.localEulerAngles = new Vector3(_tilt, 0, 0);
            }

            // 3. Movement Logic
            if (_isRecording)
            {
                HandleRecordingLogic();
            }
            else
            {
                HandleMovementLogic();
            }
        }

        private void FixedUpdate()
        {
            Quaternion targetRotation = Quaternion.Euler(0, _yaw, 0);
            _rigidbody.MoveRotation(targetRotation);
        }

        private void StartRecording()
        {
            _isRecording = true;
            _recordedRelativePath.Clear();

            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.isKinematic = true;

            // --- ANCHOR SETUP ---
            // Create a stationary anchor at the current position.
            _recordingAnchor = new GameObject("Recording_Anchor_Temp");
            _recordingAnchor.transform.position = _playerRoot.position;
            _recordingAnchor.transform.rotation = _playerRoot.rotation;

            // --- UPSIDE DOWN ADAPTATION ---
            // If the robot is mounted upside down (Ceiling), we flip the recording anchor's Z-axis.
            // This ensures that Player Up (+Y) maps to Robot Up (Away from floor), 
            // effectively inverting the coordinate system to match the ceiling mount.
            if (_robotArmController != null && _robotArmController.isUpsideDown)
            {
                _recordingAnchor.transform.Rotate(0, 0, 180f, Space.Self);
            }
            // -----------------------------

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
            if (_activeTrackingBall == null || _recordingAnchor == null) return;

            // Ball is 1 unit in front of CAMERA
            Vector3 targetPos = _camera.position + _camera.forward * 1.0f;
            _activeTrackingBall.transform.position = targetPos;

            // Record relative to the STATIONARY ANCHOR
            Vector3 relativePoint = _recordingAnchor.transform.InverseTransformPoint(targetPos);
            _recordedRelativePath.Add(relativePoint);
        }

        private void StopRecording()
        {
            _isRecording = false;
            _rigidbody.isKinematic = false;

            if (_activeTrackingBall != null) Destroy(_activeTrackingBall);
            if (_recordingAnchor != null) Destroy(_recordingAnchor);

            if (_robotArmController != null && _recordedRelativePath.Count > 0)
            {
                _robotArmController.ProcessRecordedPath(_recordedRelativePath);
            }
        }

        private void HandleMovementLogic()
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            float upDown = (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);

            Vector3 moveDir = new Vector3(h, 0, v);
            Vector3 worldMove = _camera.TransformDirection(moveDir);
            worldMove.y = upDown;

            _rigidbody.linearVelocity = worldMove * _moveSpeed;
        }
    }
}