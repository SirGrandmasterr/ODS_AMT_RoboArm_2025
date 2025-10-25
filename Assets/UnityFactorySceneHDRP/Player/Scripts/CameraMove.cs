using UnityEngine;

namespace UnityFactorySceneHDRP
{
	public class CameraMove : MonoBehaviour
	{
		[SerializeField] private CharacterController _characterController;
		[SerializeField] private Transform _playerRoot;
		[SerializeField] private Transform _camera;

		[Space(10)]
		[SerializeField] private float _moveSpeed = 2;
		[SerializeField] private float _rotateSpeed = 2;

		[Space(10)]
		[SerializeField] private float _minWorldY;
		[SerializeField] private float _walkModeCameraHeight = 1.5f; // Used when changing modes

		[Space(10)]
		[Header("Locked Camera State")]
		[SerializeField] private Transform _lockedCameraTarget; // Drag an empty GameObject here
		[SerializeField] private KeyCode _lockCameraKey = KeyCode.L;


		private float _yaw = 0;
		private float _tilt = 0;
		private bool _isRunning = false;
		private bool _isWalkMode = true;

		/// <summary>
		/// Manages the camera's current behavior state.
		/// </summary>
		public enum CameraState
		{
			FreeMove,
			Locked
		}
		private CameraState _currentState = CameraState.FreeMove;

		/// <summary>
		/// Public property to allow other scripts to check the camera's state.
		/// </summary>
		public CameraState CurrentState => _currentState;


		private void Awake()
		{
			_yaw = _playerRoot.eulerAngles.y;
			_tilt = _camera.localEulerAngles.x;
		}

		private void Update()
		{
			// --- Handle State Change Inputs ---

			// 1. Check for Lock Key Toggle
			if (Input.GetKeyDown(_lockCameraKey))
			{
				if (_currentState == CameraState.FreeMove)
				{
					// --- Switch TO Locked ---
					if (_lockedCameraTarget != null)
					{
						_currentState = CameraState.Locked;

						// Disable controller to teleport
						_characterController.enabled = false;
						
						// Set player root position and rotation from the target
						_playerRoot.position = _lockedCameraTarget.position;
						_playerRoot.rotation = _lockedCameraTarget.rotation;
						_yaw = _playerRoot.eulerAngles.y; // Update yaw tracker

						// Reset camera local transform relative to new root transform
						_camera.localPosition = Vector3.zero;
						_camera.localRotation = Quaternion.identity;
						_tilt = 0; // Reset tilt tracker

						// Re-enable controller
						_characterController.enabled = true;
					}
					else
					{
						Debug.LogWarning("Locked Camera Target not set! Staying in FreeMove.");
					}
				}
				else // _currentState == CameraState.Locked
				{
					// --- Switch TO FreeMove ---
					_currentState = CameraState.FreeMove;
					// Restore camera height based on current mode
					RestoreCameraHeight();
				}
			}

			// 2. Check for Mode Change Key ('F')
			if (Input.GetKeyDown(KeyCode.F))
			{
				_isWalkMode = !_isWalkMode;

				// Apply new mode logic
				if (_isWalkMode)
				{
					_playerRoot.position = new Vector3(_playerRoot.position.x, _minWorldY, _playerRoot.position.z);
					_camera.localPosition = new Vector3(0, _walkModeCameraHeight, 0);
				}
				else
				{
					// When switching to fly mode, root Y should match camera Y
					_playerRoot.position = new Vector3(_playerRoot.position.x, _camera.position.y, _playerRoot.position.z);
					_camera.localPosition = Vector3.zero;
				}

				// As per prompt, changing movement state also unlocks camera
				if (_currentState == CameraState.Locked)
				{
					_currentState = CameraState.FreeMove;
				}
			}

			// --- Execute Logic Based on Current State ---
			if (_currentState == CameraState.FreeMove)
			{
				HandleFreeMove();
			}
			else // _currentState == CameraState.Locked
			{
				HandleLockedState();
			}
		}

		/// <summary>
		/// Handles all player movement and rotation logic when in FreeMove state.
		/// </summary>
		private void HandleFreeMove()
		{
			// Rotate
			if (Input.GetMouseButton(1))
			{
				_yaw += Input.GetAxis("Mouse X") * _rotateSpeed;
				_tilt -= Input.GetAxis("Mouse Y") * _rotateSpeed;
				_tilt = Mathf.Clamp(_tilt, -89, 89);
				_playerRoot.eulerAngles = new Vector3(0, _yaw, 0);
				_camera.localEulerAngles = new Vector3(_tilt, 0, 0);
			}

			// Toggle Run
			if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
			{
				_isRunning = !_isRunning;
			}

			// Prepare Move Inputs
			Vector3 dir = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
			float verticalInput = (Input.GetKey(KeyCode.Q) ? -1f : 0) + (Input.GetKey(KeyCode.E) ? 1f : 0);
			float currentSpeed = _moveSpeed * (_isRunning ? 3 : 1);

			// Apply Movement
			if (_isWalkMode)
			{
				// Walk Mode (SimpleMove for XZ, manual Y)
				dir = Quaternion.Euler(0, _playerRoot.localEulerAngles.y, 0) * dir;
				_characterController.SimpleMove(dir * currentSpeed);

				// Apply Q/E height change
				float height = Mathf.Max(0, _camera.localPosition.y + verticalInput * _moveSpeed * Time.deltaTime);
				_camera.localPosition = new Vector3(0, height, 0);
			}
			else
			{
				// Fly Mode (Move for XYZ)
				// Add vertical input to direction vector
				dir.y = verticalInput;

				dir = Quaternion.Euler(_camera.localEulerAngles.x, _playerRoot.localEulerAngles.y, _camera.localEulerAngles.z) * dir;
				_characterController.Move(dir * currentSpeed * Time.deltaTime);
			}

			// Clamp to min world Y
			if (_playerRoot.position.y < _minWorldY)
			{
				Vector3 position = _playerRoot.position;
				position.y = _minWorldY;
				_playerRoot.position = position;
			}
		}

		/// <summary>
		/// Handles logic when in Locked state. Currently does nothing, as requested.
		/// </summary>
		private void HandleLockedState()
		{
			// Do nothing. All movement and rotation is disabled.
			// State change logic is handled in Update().
		}

		/// <summary>
		/// Utility to reset the camera's local Y position based on the current mode.
		/// </summary>
		private void RestoreCameraHeight()
		{
			if (_isWalkMode)
			{
				_camera.localPosition = new Vector3(0, _walkModeCameraHeight, 0);
			}
			else
			{
				_camera.localPosition = Vector3.zero;
			}
		}
	}
}

