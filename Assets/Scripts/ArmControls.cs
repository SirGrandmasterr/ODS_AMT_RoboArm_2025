using UnityEngine;
using UnityFactorySceneHDRP;

public class ArmControls : MonoBehaviour
{
    private enum ControlMode
    {
        ForwardKinematics,
        InverseKinematics,
        AI
    }

    [Header("Arm Components")]
    public Transform armbase;
    public Transform firstSegment;
    public Transform smallSegment;
    public Transform smallSegmentDrill;

    [Tooltip("Reference to the first claw component.")]
    public Transform claw1;
    [Tooltip("Reference to the second claw component.")]
    public Transform claw2;

    [Tooltip("How fast the parts rotate.")]
    public float rotationSpeed = 45.0f;

    [Header("Rotation Limits")]
    [Tooltip("Symmetrical angle limit for the first segment (in degrees). Set to 45 to allow rotation between -45 and +45.")]
    public float firstSegmentAngleLimit = 45.0f;

    [Header("State Control")]
    [Tooltip("Reference to the CameraMove script on the player. Controls will only activate when the camera is locked.")]
    public CameraMove playerCameraMove;

    [Header("IK Components")]
    [Tooltip("The very end of the arm (e.g., an empty GameObject at the grip point). MUST be a child of smallSegmentDrill.")]
    public Transform endEffector;
    [Tooltip("The chain of rotating joints, from root to end. (e.g., armbase, firstSegment, smallSegment, smallSegmentDrill)")]
    public Transform[] ikJoints;
    [Tooltip("Speed the IK target moves with WASDQE.")]
    public float ikTargetMoveSpeed = 2.0f;
    [Tooltip("How many iterations the IK solver runs per frame.")]
    public int ikIterations = 10;
    [Tooltip("How close the end effector needs to be to the target to stop solving.")]
    public float ikTolerance = 0.01f;
    [Tooltip("Optional: A red material for the spawned IK target ball.")]
    public Material ikTargetMaterial;

    private float BaseYRotation = 0.0f;
    private float LargeSegmentRotation = 0.0f;
    private float SmallSegmentRotation = 0.0f;
    private float SmallSegmentClawRotation = 0.0f;

    // The current target rotation values for the claws.
    private float claw1XRotation = -90.0f; // Default to open state
    private float claw2XRotation = 90.0f;

    private ControlMode currentMode = ControlMode.ForwardKinematics;
    private GameObject ikTargetInstance;
    private Transform ikTargetTransform;
    private bool isArmControlActive = false;

    void Start()
    {
        if (playerCameraMove == null)
        {
            Debug.LogWarning("ArmControls: 'Player Camera Move' reference is not set. Arm controls will be disabled.", this);
        }

        if (claw1 != null) claw1XRotation = -90.0f;
        if (claw2 != null) claw2XRotation = 90.0f;

        SyncFKVariablesToArmPose();
    }

    void Update()
    {
        bool wasArmControlActive = isArmControlActive;
        isArmControlActive = (playerCameraMove != null && playerCameraMove.CurrentState == CameraMove.CameraState.Locked);

        if (wasArmControlActive && !isArmControlActive)
        {
            if (currentMode == ControlMode.InverseKinematics)
            {
                ToggleIKMode();
            }
            return;
        }
        
        if (currentMode == ControlMode.AI)
        {
            // AI is in control, player input is disabled
            return;
        }

        if (!isArmControlActive)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.K))
        {
            ToggleIKMode();
        }

        if (currentMode == ControlMode.ForwardKinematics)
        {
            HandleForwardKinematicsInput();
        }
        else
        {
            HandleInverseKinematicsInput();
        }

        HandleClawInput();
    }

    void LateUpdate()
    {
        if (currentMode != ControlMode.ForwardKinematics && !isArmControlActive && currentMode != ControlMode.AI)
        {
            return;
        }

        if (currentMode == ControlMode.InverseKinematics || currentMode == ControlMode.AI)
        {
            SolveIK();
        }
        else 
        {
            if (armbase != null)
            {
                armbase.localRotation = Quaternion.Euler(0f, BaseYRotation, 0f);
            }

            if (firstSegment != null)
            {
                firstSegment.localRotation = Quaternion.Euler(0f, LargeSegmentRotation, 0f);
            }
            if (smallSegment != null)
            {
                smallSegment.localRotation = Quaternion.Euler(-180f, SmallSegmentRotation, 0f);
            }

            if (smallSegmentDrill != null)
            {
                smallSegmentDrill.localRotation = Quaternion.Euler(0f, SmallSegmentClawRotation, 0f);
            }
        }

        if (claw1 != null)
        {
            Quaternion targetClaw1Rotation = Quaternion.Euler(claw1XRotation, 0f, 0f);
            claw1.localRotation = Quaternion.Lerp(claw1.localRotation, targetClaw1Rotation, rotationSpeed * Time.deltaTime);
        }

        if (claw2 != null)
        {
            Quaternion targetClaw2Rotation = Quaternion.Euler(claw2XRotation, 0f, 0f);
            claw2.localRotation = Quaternion.Lerp(claw2.localRotation, targetClaw2Rotation, rotationSpeed * Time.deltaTime);
        }
    }

    private void HandleForwardKinematicsInput()
    {
        if (Input.GetKey(KeyCode.A))
        {
            BaseYRotation -= rotationSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.D))
        {
            BaseYRotation += rotationSpeed * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.W))
        {
            LargeSegmentRotation -= rotationSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.S))
        {
            LargeSegmentRotation += rotationSpeed * Time.deltaTime;
        }
        // LargeSegmentRotation = Mathf.Clamp(LargeSegmentRotation, -firstSegmentAngleLimit, firstSegmentAngleLimit);

        if (Input.GetKey(KeyCode.UpArrow))
        {
            SmallSegmentRotation -= rotationSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.DownArrow))
        {
            SmallSegmentRotation += rotationSpeed * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.LeftArrow))
        {
            SmallSegmentClawRotation -= rotationSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.RightArrow))
        {
            SmallSegmentClawRotation += rotationSpeed * Time.deltaTime;
        }
    }

    private void HandleInverseKinematicsInput()
    {
        if (ikTargetTransform == null) return;

        Transform camTransform = Camera.main.transform;

        Vector3 camForward = camTransform.forward;
        Vector3 camRight = camTransform.right;
        
        camForward.y = 0;
        camRight.y = 0;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 moveDelta = Vector3.zero;

        if (Input.GetKey(KeyCode.W)) moveDelta += camForward;
        if (Input.GetKey(KeyCode.S)) moveDelta -= camForward;
        if (Input.GetKey(KeyCode.D)) moveDelta += camRight;
        if (Input.GetKey(KeyCode.A)) moveDelta -= camRight;
        if (Input.GetKey(KeyCode.E)) moveDelta += Vector3.up;    // World Up
        if (Input.GetKey(KeyCode.Q)) moveDelta -= Vector3.up;    // World Down

        ikTargetTransform.Translate(moveDelta * ikTargetMoveSpeed * Time.deltaTime, Space.World);
    }

    private void HandleClawInput()
    {
        if (Input.GetKeyDown(KeyCode.Space) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
        {
            claw1XRotation = -28.0f;
            claw2XRotation = 28.0f;
        }

        // Check for Shift + Space (Release)
        if (Input.GetKeyDown(KeyCode.Space) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
        {
            claw1XRotation = -90.0f;
            claw2XRotation = 90.0f;
        }
    }

    private void ToggleIKMode()
    {
        currentMode = (currentMode == ControlMode.ForwardKinematics) ? ControlMode.InverseKinematics : ControlMode.ForwardKinematics;

        if (currentMode == ControlMode.InverseKinematics)
        {
            if (endEffector == null || ikJoints == null || ikJoints.Length == 0)
            {
                Debug.LogError("IK cannot be enabled. 'endEffector' or 'ikJoints' are not assigned in the Inspector.");
                currentMode = ControlMode.ForwardKinematics;
                return;
            }

            ikTargetInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ikTargetTransform = ikTargetInstance.transform;
            ikTargetTransform.position = endEffector.position;
            ikTargetTransform.localScale = Vector3.one * 0.1f;
            Destroy(ikTargetInstance.GetComponent<Collider>());

            Renderer rend = ikTargetInstance.GetComponent<Renderer>();
            if (ikTargetMaterial != null)
            {
                rend.material = ikTargetMaterial;
            }
            else
            {
                rend.material = new Material(Shader.Find("Standard"));
                rend.material.color = Color.red;
            }
            
            UIManager.Instance.ShowNotification("Inverse Kinematics Active (K to toggle)\nMove target with WASD, QE");
        }
        else
        {
            if (ikTargetInstance != null)
            {
                Destroy(ikTargetInstance);
                ikTargetInstance = null;
                ikTargetTransform = null;
            }

            SyncFKVariablesToArmPose();
            
            UIManager.Instance.ShowNotification("Forward Kinematics Active (K to toggle)\nControl joints with WASD, Arrows");
        }
    }

    // --- METHODS FOR AI CONTROL ---

    public void SetAiControl(bool isAiControlled)
    {
        if (isAiControlled)
        {
            currentMode = ControlMode.AI;
            if (ikTargetInstance == null)
            {
                ikTargetInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ikTargetTransform = ikTargetInstance.transform;
                ikTargetTransform.localScale = Vector3.one * 0.1f;
                Destroy(ikTargetInstance.GetComponent<Collider>());
                Renderer rend = ikTargetInstance.GetComponent<Renderer>();
                if (ikTargetMaterial != null) rend.material = ikTargetMaterial;
                else
                {
                    rend.material = new Material(Shader.Find("Standard"));
                    rend.material.color = Color.red;
                }
            }
        }
        else
        {
            currentMode = ControlMode.ForwardKinematics;
            if (ikTargetInstance != null)
            {
                Destroy(ikTargetInstance);
                ikTargetInstance = null;
                ikTargetTransform = null;
            }
        }
    }

    public Transform GetIKTarget()
    {
        return ikTargetTransform;
    }

    public void MoveIkTarget_AI(Vector3 velocity)
    {
        if (currentMode == ControlMode.AI && ikTargetTransform != null)
        {
            ikTargetTransform.Translate(velocity * Time.deltaTime, Space.World);
        }
    }

    public void SetIkTargetPosition_AI(Vector3 worldPosition)
    {
        if (ikTargetTransform != null)
        {
            ikTargetTransform.position = worldPosition;
        }
    }

    public void SetClawState_AI(bool open)
    {
        if (open)
        {
            claw1XRotation = -90.0f;
            claw2XRotation = 90.0f;
        }
        else
        {
            claw1XRotation = -28.0f;
            claw2XRotation = 28.0f;
        }
    }

    public float GetClawState()
    {
        // Return 1.0f for open, 0.0f for closed
        return Mathf.InverseLerp(-28.0f, -90.0f, claw1XRotation);
    }

    private void SolveIK()
    {
        if (ikTargetTransform == null) return;

        Vector3 targetPosition = ikTargetTransform.position;
        int numJoints = ikJoints.Length;

        for (int iter = 0; iter < ikIterations; iter++)
        {
            if (Vector3.Distance(endEffector.position, targetPosition) < ikTolerance)
                break;

            for (int i = numJoints - 1; i >= 0; i--)
            {
                Transform joint = ikJoints[i];

                Vector3 toEndEffector = (endEffector.position - joint.position).normalized;
                Vector3 toTarget = (targetPosition - joint.position).normalized;

                Quaternion rotation = Quaternion.FromToRotation(toEndEffector, toTarget);

                ApplyRotationToFKVariable(joint, rotation);
            }
        }
    }

    private void ApplyRotationToFKVariable(Transform joint, Quaternion worldRotationDelta)
    {
        Quaternion targetWorldRotation = worldRotationDelta * joint.rotation;
        
        Quaternion parentRotation = (joint.parent != null) ? joint.parent.rotation : Quaternion.identity;
        
        Quaternion targetLocalRotation = Quaternion.Inverse(parentRotation) * targetWorldRotation;

        // Vector3 localEuler = targetLocalRotation.eulerAngles;
        // float WrapAngle(float angle) => (angle > 180f) ? angle - 360f : angle;
        
        // --- NEW ROBUST METHOD ---
        // We can't use .eulerAngles because it's unstable near 180-degree poles (like smallSegment).
        // We must analytically extract the one rotation axis we care about from the quaternion.

        if (joint == armbase)
        {
            // Rotation is (0, Y, 0) -> q = (0, sin(p), 0, cos(p))
            // p = atan2(y, w)
            float pitchRad = 2 * Mathf.Atan2(targetLocalRotation.y, targetLocalRotation.w);
            BaseYRotation = pitchRad * Mathf.Rad2Deg;
            joint.localRotation = Quaternion.Euler(0f, BaseYRotation, 0f);
        }
        else if (joint == firstSegment)
        {
            // Rotation is (0, Y, 0) -> q = (0, sin(p), 0, cos(p))
            // p = atan2(y, w)
            float pitchRad = 2 * Mathf.Atan2(targetLocalRotation.y, targetLocalRotation.w);
            LargeSegmentRotation = pitchRad * Mathf.Rad2Deg;
            joint.localRotation = Quaternion.Euler(0f, LargeSegmentRotation, 0f);
        }
        else if (joint == smallSegment)
        {
            // Rotation is (-180, Y, 0) -> q = (-cos(p), 0, sin(p), 0)
            // p = atan2(z, -x)
            float pitchRad = 2 * Mathf.Atan2(targetLocalRotation.z, -targetLocalRotation.x);
            SmallSegmentRotation = pitchRad * Mathf.Rad2Deg;
            joint.localRotation = Quaternion.Euler(-180f, SmallSegmentRotation, 0f);
        }
        else if (joint == smallSegmentDrill)
        {
            // Rotation is (0, Y, 0) -> q = (0, sin(p), 0, cos(p))
            // p = atan2(y, w)
            float pitchRad = 2 * Mathf.Atan2(targetLocalRotation.y, targetLocalRotation.w);
            SmallSegmentClawRotation = pitchRad * Mathf.Rad2Deg;
            joint.localRotation = Quaternion.Euler(0f, SmallSegmentClawRotation, 0f);
        }
    }

    private void SyncFKVariablesToArmPose()
    {
        float WrapAngle(float angle) => (angle > 180f) ? angle - 360f : angle;
        
        if (armbase != null) BaseYRotation = WrapAngle(armbase.localEulerAngles.y);
        if (firstSegment != null) LargeSegmentRotation = WrapAngle(firstSegment.localEulerAngles.y);
        if (smallSegment != null) SmallSegmentRotation = WrapAngle(smallSegment.localEulerAngles.y);
        if (smallSegmentDrill != null) SmallSegmentClawRotation = WrapAngle(smallSegmentDrill.localEulerAngles.y);
    }
}