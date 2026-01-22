using System;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Random = UnityEngine.Random;

[RequireComponent(typeof(DecisionRequester))]
public class ArmAgent_IK_Fixed : Agent
{
    [Header("Environment Connection")]
    [Tooltip("Drag the SortingEnvironment object here.")]
    [SerializeField] private SortingEnvironment environment;
    
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
    
    // Reward Tracking
    private float previousDistanceToBottle;
    private float previousDistanceToBin;
    private float initialDistanceToBottle; // NEW: Used for normalization

    public override void Initialize()
    {
        // 1. Check Environment
        if (environment == null)
            environment = GetComponent<SortingEnvironment>() ?? FindFirstObjectByType<SortingEnvironment>();

        if (environment == null)
            Debug.LogError($"<color=red><b>[ArmAgent_IK] FATAL ERROR:</b> No SortingEnvironment found!</color>", this);

        // 2. Check IK Controller
        if (ikController == null)
            ikController = GetComponent<RobotArm_IK_Controller>();
            
        if (ikController == null)
             Debug.LogError($"<color=red><b>[ArmAgent_IK] ERROR:</b> RobotArm_IK_Controller not assigned!</color>", this);
        
        // 2.5 Check Init Config
        if (initConfig == null) initConfig = GetComponent<InitializationConfig>();

        // 3. Components
        if (endEffector != null)
        {
            if (endEffectorRb == null) endEffectorRb = endEffector.GetComponent<Rigidbody>();
            if (endEffectorRb == null) endEffectorRb = endEffector.gameObject.AddComponent<Rigidbody>();
            endEffectorRb.isKinematic = true; 
        }
        
        // Safety
        if (MaxStep == 0) MaxStep = 5000;
        
        // Init state
        claw1XRotation = -90.0f;
        claw2XRotation = 90.0f;
    }

    public override void OnEpisodeBegin()
    {
        if (environment == null) return;

        // 1. Reset Environment Logic
        environment.ResetEnvironment();

        // 2. Reset Robot State (via Forward Kinematics first, then IK Sync)
        ResetAndApplyJointRotations();
        
        // NOW read the physical position to set the IK Target
        // This ensures smoothness and respects the InitializationConfig
        currentTargetPosition = endEffector.position;
        
        // Teleport there physically first
        ikController.SetLiveTarget(currentTargetPosition, currentSmallSegmentDrillYRotation);
        
        Release(); 

        // 3. Lesson Handling - Force Grab
        if (environment.bottleRb != null && environment.CurrentLessonNumber == 2.0f)
        {
            environment.ResetBottlePhysics(endEffector.position, true);
            ForceGrab(environment.bottleRb);
        }

        // 4. Initialize Rewards
        previousDistanceToBottle = Vector3.Distance(endEffector.position, environment.bottle.position);
        initialDistanceToBottle = previousDistanceToBottle; // Store initial distance
        previousDistanceToBin = environment.GetHorizontalDistanceToBin(environment.bottle.position);
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

        // 3 Bottle Orientation
        sensor.AddObservation(environment.bottle.up);

        // 1 Holding State
        sensor.AddObservation(IsHoldingObject());

        // 1 Lesson
        sensor.AddObservation(environment.CurrentLessonNumber);
        
        // 3 IK Target Error
        sensor.AddObservation(currentTargetPosition - endEffector.position);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
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

        // Action 3: Drill Rotation
        float rotDelta = actions.ContinuousActions[3] * rotationSpeed * dt;
        currentSmallSegmentDrillYRotation += rotDelta;

        // Apply to IK Controller
        ikController.SetLiveTarget(currentTargetPosition, currentSmallSegmentDrillYRotation);
        
        ApplyClawRotations();

        // --- 2. CLAW LOGIC ---
        float clawInput = actions.ContinuousActions[4];
        bool wasHolding = IsHoldingObject();

        if (clawInput > 0.5f) // Close
        {
            claw1XRotation = -28.0f;
            claw2XRotation = 28.0f;
            
            if (!wasHolding)
            {
                if (TryGrab())
                {
                    AddReward(2.0f);
                    if (environment.CurrentLessonNumber == 1f) 
                    {
                        Debug.Log($"<color=green>[ArmAgent_IK] WIN: Grabbed Bottle in Lesson 1! Reward: {GetCumulativeReward():F2}</color>");
                        EndEpisode();
                    }
                }
            }
        }
        else // Open
        {
            claw1XRotation = -90.0f;
            claw2XRotation = 90.0f;
            
            if (wasHolding)
            {
                Release();
                if (environment.CurrentLessonNumber >= 2f && !environment.IsInBinZone())
                {
                    AddReward(-1.0f);
                    EndEpisode();
                    return;
                }
            }
        }

        // --- 3. REWARDS ---
        float currentDistanceToBottle = Vector3.Distance(endEffector.position, environment.bottle.position);
        float currentDistanceToBin = environment.GetHorizontalDistanceToBin(environment.bottle.position);

        if (environment.CurrentLessonNumber == 0f) // Reach
        {
            // FIX: Normalize the delta so the total reward for closing the distance is always 1.0.
            float rawDelta = previousDistanceToBottle - currentDistanceToBottle;
            
            // Prevent division by zero if initial distance is extremely small
            float distDenominator = Mathf.Max(initialDistanceToBottle, 0.001f);
            
            // Normalize: Moving 1 unit is worth (1 / TotalDistance)
            float normalizedDelta = rawDelta / distDenominator;

            // FIX: Clamp to prevent physics explosions from giving massive rewards
            // Assuming max speed * dt is small, this clamp should be safe.
            // e.g. if max speed is 1m/s and dt is 0.02, max move is 0.02. 
            // 0.02 / 1.0 (dist) = 0.02 reward. 
            // We clamp strictly to avoid teleportation exploits.
            AddReward(Mathf.Clamp(normalizedDelta, -0.1f, 0.1f)); 

            if (currentDistanceToBottle < 0.05f) 
            { 
                AddReward(1.0f); // Bonus for completion
                EndEpisode(); 
            }
        }
        else // Grab / Place / Full
        {
             if (!IsHoldingObject())
             {
                 float delta = previousDistanceToBottle - currentDistanceToBottle;
                 AddReward(Mathf.Clamp(delta, -1f, 1f)); 
             }
             else
             {
                 float delta = previousDistanceToBin - currentDistanceToBin;
                 AddReward(Mathf.Clamp(delta, -1f, 1f));
                 
                 if (currentDistanceToBin < 0.3f) AddReward(-0.005f); 
             }
        }
        
        previousDistanceToBottle = currentDistanceToBottle;
        previousDistanceToBin = currentDistanceToBin;
        
        AddReward(-0.0001f); // Time Penalty

        // --- 4. SUCCESS/FAIL CHECKS ---
        if (environment.CurrentLessonNumber >= 2f)
        {
            if (!IsHoldingObject())
            {
                if (environment.CheckPlacementSuccess())
                {
                    AddReward(5.0f);
                    EndEpisode();
                }
                else if (environment.CheckPlacementFailure())
                {
                    AddReward(-2.0f);
                    EndEpisode();
                }
            }
        }

        // Fail if bottle falls
        if (!IsHoldingObject() && (environment.bottle.position.y < (environment.bottleSpawnPoint.position.y - 0.4f)))
        {
            if (!environment.IsInBinZone())
            {
                 // FIX: Ensure penalty cancels out any potential gain from "pushing" the bottle off
                 if (environment.CurrentLessonNumber >= 1f) AddReward(-1.0f);
                 EndEpisode();
            }
        }
    }

    public void OnPartCollision(string hitTag)
    {
        if (hitTag == "Conveyor" || hitTag == "RobotPart" || hitTag == "Ground")
        {
            // FIX: -1.0 is now mathematically stronger because max travel reward is normalized to +1.0.
            // So a suicide dive (travel +1, crash -1) yields 0 net reward, discouraging the behavior.
            AddReward(-1.0f); 
            EndEpisode();
        }
    }

    public void OnBottleHitGround()
    {
        Debug.Log($"<color=red>[ArmAgent_IK] FAILED: Bottle hit ground (Collision).</color>");
        AddReward(-1.0f);
        EndEpisode();
    }

    // --- Helpers ---

    private void ResetAndApplyJointRotations()
    {
        float baseY, firstY, smallY, drillY;

        if (initConfig != null)
        {
            initConfig.GetStartRotations(
                ikController.baseLimits, ikController.firstSegLimits, ikController.smallSegLimits, ikController.drillLimits,
                out baseY, out firstY, out smallY, out drillY
            );
        }
        else
        {
             baseY = 0f;
             firstY = -45f;
             smallY = -45f;
             drillY = 0f;
        }

        if (armbase) armbase.localRotation = Quaternion.Euler(0f, baseY, 0f);
        if (firstSegment) firstSegment.localRotation = Quaternion.Euler(0f, firstY, 0f);
        if (smallSegment) smallSegment.localRotation = Quaternion.Euler(-180f, smallY, 0f);
        if (smallSegmentDrill) smallSegmentDrill.localRotation = Quaternion.Euler(0f, drillY, 0f);
        
        currentSmallSegmentDrillYRotation = drillY;
        Physics.SyncTransforms(); 
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
        float moveX = 0f; float moveY = 0f; float moveZ = 0f;

        if (Input.GetKey(KeyCode.D)) moveX = 1f;
        if (Input.GetKey(KeyCode.A)) moveX = -1f;
        if (Input.GetKey(KeyCode.W)) moveZ = 1f;
        if (Input.GetKey(KeyCode.S)) moveZ = -1f;
        if (Input.GetKey(KeyCode.Q)) moveY = 1f;
        if (Input.GetKey(KeyCode.E)) moveY = -1f;

        continuousActions[0] = moveX;
        continuousActions[1] = moveY;
        continuousActions[2] = moveZ;
        continuousActions[3] = Input.GetKey(KeyCode.RightArrow) ? 1f : (Input.GetKey(KeyCode.LeftArrow) ? -1f : 0f);
        continuousActions[4] = Input.GetKey(KeyCode.Space) ? 1.0f : -1.0f;
    }
}