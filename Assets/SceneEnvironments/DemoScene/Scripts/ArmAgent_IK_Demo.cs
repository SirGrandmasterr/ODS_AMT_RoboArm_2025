using System;
using System.Collections;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Random = UnityEngine.Random;

[RequireComponent(typeof(DecisionRequester))]
public class ArmAgent_IK_Demo : Agent
{
    [Header("Environment Connection")]
    [Tooltip("Drag the SortingEnvironment_Demo object here.")]
    [SerializeField] private SortingEnvironment_Demo environment;

    [Header("Initialization Config")]
    [SerializeField] private InitializationConfig initConfig;

    [Header("IK Controller")]
    [SerializeField] private RobotArm_IK_Controller ikController;

    [Header("Arm Joints (Transforms)")]
    [SerializeField] private Transform armbase;
    [SerializeField] private Transform firstSegment;
    [SerializeField] private Transform smallSegment;
    [SerializeField] private Transform smallSegmentDrill;

    [Header("Claw Components")]
    [SerializeField] private Transform claw1;
    [SerializeField] private Transform claw2;
    [SerializeField] private Transform endEffector;

    [Header("Motion Settings")]
    [SerializeField] private float moveSpeed = 1.0f;
    [SerializeField] private float rotationSpeed = 300f;
    [SerializeField] private BoxCollider workspaceBounds;

    [Header("Grabbing Logic")]
    [SerializeField] private float grabRadius = 0.1f;
    [SerializeField] private Rigidbody endEffectorRb;

    // State
    private Vector3 currentTargetPosition;
    private float currentSmallSegmentDrillYRotation;
    private float claw1XRotation;
    private float claw2XRotation;
    
    // Grabbing State
    private Rigidbody heldObjectRb; 

    [Header("Demo Control")]
    public bool isOperating = true; 
    public event Action OnJobFinished;

    public override void Initialize()
    {
        // 1. Check Environment
        if (environment == null)
        {
            environment = GetComponent<SortingEnvironment_Demo>();
            if (environment == null)
            {
                environment = FindFirstObjectByType<SortingEnvironment_Demo>();
            }

            if (environment == null)
            {
                Debug.LogError($"<color=red><b>[ArmAgent_IK_Demo] FATAL ERROR:</b> 'Environment' field is not assigned in the Inspector!</color>", this);
            }
        }

        // 2. Check IK Controller
        if (ikController == null)
            ikController = GetComponent<RobotArm_IK_Controller>();
            
        if (ikController == null)
             Debug.LogError($"<color=red><b>[ArmAgent_IK_Demo] ERROR:</b> RobotArm_IK_Controller not assigned!</color>", this);

        // 2.5 Check Init Config
        if (initConfig == null) initConfig = GetComponent<InitializationConfig>();

        // 3. Components
        if (endEffector != null)
        {
            if (endEffectorRb == null) endEffectorRb = endEffector.GetComponent<Rigidbody>();
            if (endEffectorRb == null) endEffectorRb = endEffector.gameObject.AddComponent<Rigidbody>();
            endEffectorRb.isKinematic = true; 
        }
        
        // Init state
        claw1XRotation = -90.0f;
        claw2XRotation = 90.0f;
    }

    public override void OnEpisodeBegin()
    {
        if (environment == null) return;

        // 1. Reset Robot State (via Forward Kinematics first, then IK Sync)
        ResetAndApplyJointRotations(); // This resets arm joints
        
        // Reset Claw to Open
        claw1XRotation = -90.0f;
        claw2XRotation = 90.0f;
        
        // NOW read the physical position to set the IK Target
        // This ensures smoothness and respects the InitializationConfig
        currentTargetPosition = endEffector.position;
        
        // Teleport there physically first
        if(ikController != null)
            ikController.SetLiveTarget(currentTargetPosition, currentSmallSegmentDrillYRotation);
        
        Release(); 
        
        // Ensure Physics Sync
        Physics.SyncTransforms();
    }

    private void ResetAndApplyJointRotations()
    {
        float baseY, firstY, smallY, drillY;

        if (initConfig != null && ikController != null)
        {
            initConfig.GetStartRotations(
                ikController.baseLimits, ikController.firstSegLimits, ikController.smallSegLimits, ikController.drillLimits,
                out baseY, out firstY, out smallY, out drillY
            );
        }
        else
        {
            // Fallback safe defaults if no config or controller
             baseY = 0f;
             firstY = -45f;
             smallY = -45f;
             drillY = 0f;
        }

        // Apply immediately
        if (armbase) armbase.localRotation = Quaternion.Euler(0f, baseY, 0f);
        if (firstSegment) firstSegment.localRotation = Quaternion.Euler(0f, firstY, 0f);
        
        // Note: Small Segment usually has -180 offset in this setup, check RobotArm_IK_Controller.UpdateFKValues
        if (smallSegment) smallSegment.localRotation = Quaternion.Euler(-180f, smallY, 0f);
        
        if (smallSegmentDrill) smallSegmentDrill.localRotation = Quaternion.Euler(0f, drillY, 0f);
        
        // Sync State Variables
        currentSmallSegmentDrillYRotation = drillY;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1 Material
        sensor.AddObservation((int)environment.bottleScript.material);

        // 12 World Positions
        sensor.AddObservation(transform.InverseTransformPoint(endEffector.position));
        sensor.AddObservation(transform.InverseTransformPoint(environment.bottle.position));
        sensor.AddObservation(transform.InverseTransformPoint(environment.targetBinAluminum.position));
        sensor.AddObservation(transform.InverseTransformPoint(environment.targetBinPlastic.position));
        
        // 3 Relative Vector to target
        sensor.AddObservation(environment.bottle.position - endEffector.position); 

        // 1 Holding State
        sensor.AddObservation(IsHoldingObject());

        // 1 Lesson - Hardcoded to 3.0 (Full Task) for Demo
        sensor.AddObservation(3.0f);
        
        // 3 IK Target Error
        sensor.AddObservation(currentTargetPosition - endEffector.position);

        // 1 Claw Rotation (Normalized)
        sensor.AddObservation(Mathf.InverseLerp(-90f, -28f, claw1XRotation));
    }

    public void SetDemoBottle(GameObject bottleObj)
    {
        if (environment != null && bottleObj != null)
        {
            environment.bottle = bottleObj.transform;
            environment.bottleRb = bottleObj.GetComponent<Rigidbody>();
            environment.bottleScript = bottleObj.GetComponent<DemoBottle>();
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (!isOperating) return;

        float dt = Time.fixedDeltaTime;

        // --- 1. MOVEMENT (IK Target) ---
        float moveX = actions.ContinuousActions[0];
        float moveY = actions.ContinuousActions[1];
        float moveZ = actions.ContinuousActions[2];
        
        Vector3 moveDelta = new Vector3(moveX, moveY, moveZ) * moveSpeed * dt;
        currentTargetPosition += moveDelta;

        // Clamp Target to Workspace Bounds
        if (workspaceBounds != null)
        {
            currentTargetPosition = workspaceBounds.bounds.ClosestPoint(currentTargetPosition);
        }

        // Drill Rotation
        float rotDelta = actions.ContinuousActions[3] * rotationSpeed * dt;
        currentSmallSegmentDrillYRotation += rotDelta;

        // Apply to IK Controller
        if (ikController != null)
        {
            ikController.SetLiveTarget(currentTargetPosition, currentSmallSegmentDrillYRotation);
        }
        
        ApplyClawRotations();

        // --- 2. CLAW LOGIC ---
        float clawInput = actions.ContinuousActions[4];
        bool wasHolding = IsHoldingObject();

        if (clawInput > 0.0f) // Close
        {
            claw1XRotation = -28.0f;
            claw2XRotation = 28.0f;
            
            if (!wasHolding)
            {
                TryGrab();
            }
        }
        else // Open
        {
            claw1XRotation = -90.0f;
            claw2XRotation = 90.0f;
            
            if (wasHolding)
            {
                Release();
                
                // Demo Logic: Check Success immediately on release
                if (environment.CheckPlacementSuccess())
                {
                    Debug.Log("<color=green>[ArmAgent_IK_Demo] SUCCESS: Bottle sorted correctly!</color>");
                    StartCoroutine(ReturnToHomeSequence());
                }
                else if (environment.CheckPlacementFailure())
                {
                    Debug.Log("<color=orange>[ArmAgent_IK_Demo] FAIL: Bottle sorted incorrectly.</color>");
                    StartCoroutine(ReturnToHomeSequence());
                }
            }
        }
        
        // Verify Bounds (FailSafe)
        if (!IsHoldingObject() && (environment.bottle.position.y < (environment.bottleSpawnPoint.position.y - 0.5f)))
        {
             // If it fell and wasn't in a bin
            if (!environment.IsInBinZone())
            {
                 Debug.Log($"<color=red>[ArmAgent_IK_Demo] FAILED: Bottle fell out of bounds.</color>");
                 StartCoroutine(ReturnToHomeSequence());
            }
        }

        // Timeout Check
        if (StepCount >= 5000)
        {
             Debug.Log($"<color=orange>[ArmAgent_IK_Demo] TIMEOUT: Max steps (5000) reached.</color>");
             StartCoroutine(ReturnToHomeSequence());
        }
    }

    private IEnumerator ReturnToHomeSequence()
    {
        if (!isOperating) yield break; // Already stopping/stopped
        isOperating = false;

        float homeBase = 0f;
        float homeFirst = 0f;
        float homeSmall = -0f; 
        float homeDrill = 0f;

        if (initConfig != null)
        {
            homeBase = initConfig.homeBase;
            homeFirst = initConfig.homeFirst;
            homeSmall = initConfig.homeSmall;
            homeDrill = initConfig.homeDrill;
        }

        // Helper to animate one or more joints
        // We need to animate specific groups.
        
        float animSpeed = 90f; // degrees per sec

        // 1. Small Segment & Drill
        // Need to read current angles carefully (handling offsets)
        while (true)
        {
            float currentSmall = NormalizeAngle(GetRobustJointYAngle(smallSegment, -180f));
            float currentDrill = NormalizeAngle(GetRobustJointYAngle(smallSegmentDrill, 0f));
            
            bool smallDone = Mathf.Abs(currentSmall - homeSmall) < 1f;
            bool drillDone = Mathf.Abs(currentDrill - homeDrill) < 1f;

            if (smallDone && drillDone) break;

            float dt = Time.fixedDeltaTime; // Use fixed delta time as likely running in physics loop context or close enough
            if (Time.inFixedTimeStep) dt = Time.fixedDeltaTime; else dt = Time.deltaTime;

            float nextSmall = Mathf.MoveTowards(currentSmall, homeSmall, animSpeed * dt);
            float nextDrill = Mathf.MoveTowards(currentDrill, homeDrill, animSpeed * dt);

            if (smallSegment) smallSegment.localRotation = Quaternion.Euler(-180f, nextSmall, 0f);
            if (smallSegmentDrill) smallSegmentDrill.localRotation = Quaternion.Euler(0f, nextDrill, 0f);
            
            // Sync current drill var
            currentSmallSegmentDrillYRotation = nextDrill;

            yield return null;
        }

        // 2. First Segment
        while (true)
        {
            float currentFirst = NormalizeAngle(GetRobustJointYAngle(firstSegment, 0f));
            if (Mathf.Abs(currentFirst - homeFirst) < 1f) break;

            float dt = Time.inFixedTimeStep ? Time.fixedDeltaTime : Time.deltaTime;
            float nextFirst = Mathf.MoveTowards(currentFirst, homeFirst, animSpeed * dt);
            
            if (firstSegment) firstSegment.localRotation = Quaternion.Euler(0f, nextFirst, 0f);
            yield return null;
        }

        // 3. Arm Base
        while (true)
        {
            float currentBase = NormalizeAngle(GetRobustJointYAngle(armbase, 0f));
            if (Mathf.Abs(currentBase - homeBase) < 1f) break;

            float dt = Time.inFixedTimeStep ? Time.fixedDeltaTime : Time.deltaTime;
            float nextBase = Mathf.MoveTowards(currentBase, homeBase, animSpeed * dt);
            
            if (armbase) armbase.localRotation = Quaternion.Euler(0f, nextBase, 0f);
            yield return null;
        }

        // Final Sync Target so IK doesn't snap if re-enabled
        currentTargetPosition = endEffector.position;
        Physics.SyncTransforms();

        OnJobFinished?.Invoke();
    }

    private float GetRobustJointYAngle(Transform t, float xOffset)
    {
        if (t == null) return 0f;
        Quaternion offsetRot = Quaternion.Euler(xOffset, 0, 0);
        Quaternion cleanRot = t.localRotation * Quaternion.Inverse(offsetRot);
        return cleanRot.eulerAngles.y;
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    public void ManualEndEpisode()
    {
        EndEpisode();
    }

    private void FixedUpdate()
    {
        // IK Controller usually handles the heavy lifting in its own Update/FixedUpdate,
        // but we might need to visualizing claw rotation if it's not handled there.
        ApplyClawRotations();
    }

    private void ApplyClawRotations()
    {
        float lerpSpeed = 15f;
        if (claw1) claw1.localRotation = Quaternion.Lerp(claw1.localRotation, Quaternion.Euler(claw1XRotation, 0f, 0f), Time.fixedDeltaTime * lerpSpeed);
        if (claw2) claw2.localRotation = Quaternion.Lerp(claw2.localRotation, Quaternion.Euler(claw2XRotation, 0f, 0f), Time.fixedDeltaTime * lerpSpeed);
    }

    private bool TryGrab()
    {
        if (heldObjectRb != null || environment == null) return false;

        Collider[] colliders = Physics.OverlapSphere(endEffector.position, grabRadius);
        foreach (var col in colliders)
        {
            if (col.transform == environment.bottle)
            {
                ForceGrab(environment.bottleRb);
                return true;
            }
        }
        return false;
    }

    private void ForceGrab(Rigidbody targetRb)
    {
        heldObjectRb = targetRb;
        heldObjectRb.isKinematic = true; 
        heldObjectRb.transform.SetParent(endEffector); 
        heldObjectRb.transform.localPosition = new Vector3(0, -0.08f, 0); 
        if (environment.bottleScript) environment.bottleScript.isHeld = true;
    }

    private void Release()
    {
        if (heldObjectRb == null) return;
        
        if (environment.bottleScript) environment.bottleScript.isHeld = false;
        heldObjectRb.transform.SetParent(environment.bottleOriginalParent);
        heldObjectRb.isKinematic = false; 
        heldObjectRb = null;
    }

    private bool IsHoldingObject()
    {
        return heldObjectRb != null;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        
        float moveX = 0f;
        float moveY = 0f;
        float moveZ = 0f;

        if (Input.GetKey(KeyCode.D)) moveX = 1f;
        if (Input.GetKey(KeyCode.A)) moveX = -1f;
        
        if (Input.GetKey(KeyCode.W)) moveZ = 1f;
        if (Input.GetKey(KeyCode.S)) moveZ = -1f;
        
        if (Input.GetKey(KeyCode.Q)) moveY = 1f;
        if (Input.GetKey(KeyCode.E)) moveY = -1f;

        continuousActions[0] = moveX; 
        continuousActions[1] = moveY; 
        continuousActions[2] = moveZ; 

        continuousActions[3] = Input.GetKey(KeyCode.RightArrow) ? 1f : (Input.GetKey(KeyCode.LeftArrow) ? -1f : 0f); // Drill
        continuousActions[4] = Input.GetKey(KeyCode.Space) ? 1.0f : -1.0f; // Claw
    }
}
