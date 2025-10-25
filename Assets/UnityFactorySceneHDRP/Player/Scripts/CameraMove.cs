using UnityEngine;

namespace UnityFactorySceneHDRP
{
	public class CameraMove : MonoBehaviour
	{
		// --- REPLACED CharacterController with Rigidbody ---
		[SerializeField] private Rigidbody _rigidbody;
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

		// --- ADDED: Input storage for FixedUpdate ---
		private Vector3 _moveDir;
		private float _verticalInput;
		private float _currentSpeed;

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

			// --- ADDED: Rigidbody check ---
			if (_rigidbody == null)
			{
				Debug.LogError("Rigidbody is not assigned on CameraMove script!", this);
				_playerRoot.GetComponent<Rigidbody>(); // Try to find it
			}
			if (_rigidbody == null)
			{
				Debug.LogError("Could not find Rigidbody on PlayerRoot. Movement will fail.", this);
			}
			else
			{
				// Set initial gravity state
				_rigidbody.useGravity = _isWalkMode;
			}
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

						// --- MODIFIED: Use Rigidbody properties ---
						// Disable physics simulation
						_rigidbody.isKinematic = true; 
						
						// Set player root position and rotation from the target
						_playerRoot.position = _lockedCameraTarget.position;
						_playerRoot.rotation = _lockedCameraTarget.rotation;
						_yaw = _playerRoot.eulerAngles.y; // Update yaw tracker

						// Reset camera local transform relative to new root transform
						_camera.localPosition = Vector3.zero;
						_camera.localRotation = Quaternion.identity;
						_tilt = 0; // Reset tilt tracker
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
					// --- MODIFIED: Use Rigidbody properties ---
					// Re-enable physics simulation
					_rigidbody.isKinematic = false; 
					// Restore camera height based on current mode
					RestoreCameraHeight();
				}
			}

			// 2. Check for Mode Change Key ('F')
			if (Input.GetKeyDown(KeyCode.F))
			{
				_isWalkMode = !_isWalkMode;

				// --- MODIFIED: Use Rigidbody properties ---
				// Update physics settings for the new mode
				_rigidbody.useGravity = _isWalkMode;
				_rigidbody.linearVelocity = Vector3.zero; // Stop all movement on switch

				// Apply new mode logic
				if (_isWalkMode)
				{
					// Safely teleport to the ground
					_rigidbody.MovePosition(new Vector3(_playerRoot.position.x, _minWorldY, _playerRoot.position.z));
					_camera.localPosition = new Vector3(0, _walkModeCameraHeight, 0);
				}
				else
				{
					// When switching to fly mode, root Y should match camera Y
					_rigidbody.MovePosition(new Vector3(_playerRoot.position.x, _camera.position.y, _playerRoot.position.z));
					_camera.localPosition = Vector3.zero;
				}

				// As per prompt, changing movement state also unlocks camera
				if (_currentState == CameraState.Locked)
				{
					_currentState = CameraState.FreeMove;
					_rigidbody.isKinematic = false; // Ensure physics is re-enabled
				}
			}

			// --- Execute Logic Based on Current State ---
			if (_currentState == CameraState.FreeMove)
			{
				// HandleFreeMove now just GATHERS input in Update()
				HandleFreeMove_GatherInput();
			}
			else // _currentState == CameraState.Locked
			{
				HandleLockedState();
			}
		}

		// --- ADDED: FixedUpdate for all physics ---
		/// <summary>
		/// Handles all physics-based movement and rotation.
		/// </summary>
		private void FixedUpdate()
		{
			// Don't move if not in free move state
			if (_currentState != CameraState.FreeMove)
			{
				return;
			}

			// --- Apply Rotation ---
			// We use MoveRotation for smooth, physics-safe rotation
			Quaternion targetRotation = Quaternion.Euler(0, _yaw, 0);
			_rigidbody.MoveRotation(targetRotation);

			// --- Apply Movement ---
			if (_isWalkMode)
			{
				// Walk Mode
				// Convert local input direction to world-space direction based on player's rotation
				Vector3 worldMoveDir = _playerRoot.TransformDirection(_moveDir);
				Vector3 targetVelocity = worldMoveDir * _currentSpeed;

				// Set velocity, but PRESERVE existing Y (vertical) velocity (for gravity)
				_rigidbody.linearVelocity = new Vector3(targetVelocity.x, _rigidbody.linearVelocity.y, targetVelocity.z);
			}
			else
			{
				// Fly Mode
				// Add vertical input
				Vector3 localMoveDir = _moveDir;
				localMoveDir.y = _verticalInput;

				// Convert local input (including Y) to world space based on *camera's* rotation
				Vector3 worldMoveDir = _camera.transform.TransformDirection(localMoveDir);
				Vector3 targetVelocity = worldMoveDir * _currentSpeed;

				// Set velocity directly. Gravity is off, so we control all 3 axes.
				_rigidbody.linearVelocity = targetVelocity;

				// Clamp to min world Y
				if (_rigidbody.position.y < _minWorldY)
				{
					_rigidbody.MovePosition(new Vector3(_rigidbody.position.x, _minWorldY, _rigidbody.position.z));
					// Stop Y velocity if we hit the floor
					_rigidbody.linearVelocity = new Vector3(_rigidbody.linearVelocity.x, 0, _rigidbody.linearVelocity.z);
				}
			}
		}

		/// <summary>
		/// GATHERS all player inputs when in FreeMove state.
		/// Movement is APPLIED in FixedUpdate().
		/// </summary>
		private void HandleFreeMove_GatherInput()
		{
			// Rotate (Mouse Look)
			if (Input.GetMouseButton(1))
			{
				_yaw += Input.GetAxis("Mouse X") * _rotateSpeed;
				_tilt -= Input.GetAxis("Mouse Y") * _rotateSpeed;
				_tilt = Mathf.Clamp(_tilt, -89, 89);
				// Rotation is applied in FixedUpdate, but camera tilt is visual-only
				_camera.localEulerAngles = new Vector3(_tilt, 0, 0);
			}

			// Toggle Run
			if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
			{
				_isRunning = !_isRunning;
			}

			// --- MODIFIED: Store inputs for FixedUpdate ---
			_moveDir = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
			_verticalInput = (Input.GetKey(KeyCode.Q) ? -1f : 0) + (Input.GetKey(KeyCode.E) ? 1f : 0);
			_currentSpeed = _moveSpeed * (_isRunning ? 3 : 1);

			// Apply Movement
			if (_isWalkMode)
			{
				// Apply Q/E height change (visual only, moves camera up/down)
				float height = Mathf.Max(0, _camera.localPosition.y + _verticalInput * _moveSpeed * Time.deltaTime);
				_camera.localPosition = new Vector3(0, height, 0);
			}
			// All other movement logic is now in FixedUpdate
		}

		/// <summary>
		/// Handles logic when in Locked state.
		/// </summary>
		private void HandleLockedState()
		{
			// Do nothing. All movement and rotation is disabled.
			// Rigidbody is Kinematic, so it won't be affected by physics.
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
