using System;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Demonstrations;
using Random = UnityEngine.Random;

[RequireComponent(typeof(DecisionRequester))]
public class ArmAgent_IK_Recording : Agent
{
    [Header("Environment Connection")]
    [Tooltip("Drag the SortingEnvironment_Recording object here.")]
    [SerializeField] private SortingEnvironment_Recording environment;
    
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
    [SerializeField] public Transform endEffector; // Public for Recorder Access

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
    
    private int defaultMaxStep;

    // --- RECORDING / EXTERNAL CONTROL ---
    public bool useExternalHeuristic = false;
    private Vector3 externalTargetPos;
    private float externalTargetDrillRot;
    public bool externalClawSignal = false;
    public bool preventSpawning = false; // Control flag for VR Controller
    
    // Events
    public delegate void EpisodeCompleteHandler(bool success, int stepCount);
    public event EpisodeCompleteHandler OnEpisodeCompleted;
    private int _currentStepCount = 0;

    public void SetExternalTarget(Vector3 pos, float drillRot)
    {
        externalTargetPos = pos;
        externalTargetDrillRot = drillRot;
    }

    public void SetIKController(RobotArm_IK_Controller controller)
    {
        ikController = controller;
    }

    public void SetExternalControl(bool active)
    {
        useExternalHeuristic = active;
    }

    public override void Initialize()
    {
        // 1. Check Environment
        if (environment == null)
            environment = GetComponent<SortingEnvironment_Recording>() ?? FindFirstObjectByType<SortingEnvironment_Recording>();

        if (environment == null)
            Debug.LogError($"<color=red><b>[ArmAgent_IK_Recording] FATAL ERROR:</b> No SortingEnvironment_Recording found!</color>", this);

        // 2. Check IK Controller
        if (ikController == null)
            ikController = GetComponent<RobotArm_IK_Controller>();
            
        if (ikController == null)
             Debug.LogError($"<color=red><b>[ArmAgent_IK_Recording] ERROR:</b> RobotArm_IK_Controller not assigned!</color>", this);
        
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
        defaultMaxStep = MaxStep;
        
        // Init state
        claw1XRotation = -90.0f;
        claw2XRotation = 90.0f;
    }

    public override void OnEpisodeBegin()
    {
        if (environment == null) return;

        _currentStepCount = 0;

        // 1. Reset Environment Logic
        environment.ResetEnvironment();

        // Adjust MaxStep based on lesson
        if (environment.CurrentLessonNumber == 0f)
        {
            MaxStep = 5000;
        }
        else
        {
            MaxStep = defaultMaxStep;
        }

        // 2. Reset Robot State (via Forward Kinematics first, then IK Sync)
        // 2. Reset Robot State (via Forward Kinematics first, then IK Sync)
        ResetAndApplyJointRotations(); // This resets arm joints
        
        // --- SPAWNING CONTROL ---
        if (preventSpawning) return; // Skip bottle spawning logic
        
        // Reset Claw State based on Lesson
        if (environment.CurrentLessonNumber == 2f) 
        {
             // Start Closed for "Place" lesson
             claw1XRotation = -28.0f;
             claw2XRotation = 28.0f;
        }
        else
        {
             // Start Open
             claw1XRotation = -90.0f;
             claw2XRotation = 90.0f;
        }
        
        // NOW read the physical position to set the IK Target
        // This ensures smoothness and respects the InitializationConfig
        currentTargetPosition = endEffector.position;
        externalTargetPos = currentTargetPosition;
        externalTargetDrillRot = currentSmallSegmentDrillYRotation;
        
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
        previousDistanceToBottle = Vector3.Distance(currentTargetPosition, environment.bottle.position);
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

        // 1 Holding State
        sensor.AddObservation(IsHoldingObject());

        // 1 Lesson
        sensor.AddObservation(environment.CurrentLessonNumber);
        
        // 3 IK Target Error (Optional, helps agent know where it's trying to go vs where it is)
        sensor.AddObservation(currentTargetPosition - endEffector.position);

        // 1 Claw Rotation (Normalized) - CRITICAL for agent to know tool state
        // -90 is Open (0), -28 is Closed (1)
        sensor.AddObservation(Mathf.InverseLerp(-90f, -28f, claw1XRotation));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float dt = Time.fixedDeltaTime;

        // --- 1. MOVEMENT (IK Target) ---
        // Actions 0, 1, 2: XYZ Movement
        float moveX = actions.ContinuousActions[0];
        float moveY = actions.ContinuousActions[1];
        float moveZ = actions.ContinuousActions[2];
        
        Vector3 moveDelta = new Vector3(moveX, moveY, moveZ) * moveSpeed * dt;
        currentTargetPosition += moveDelta;

        // Clamp Target to Workspace Bounds if provided
        if (workspaceBounds != null)
        {
            currentTargetPosition = workspaceBounds.bounds.ClosestPoint(currentTargetPosition);
        }

        // Action 3: Drill Rotation
        float rotDelta = actions.ContinuousActions[3] * rotationSpeed * dt;
        currentSmallSegmentDrillYRotation += rotDelta;

        // Apply to IK Controller
        ikController.SetLiveTarget(currentTargetPosition, currentSmallSegmentDrillYRotation);
        
        // Updates the claw transforms manually (visual only, logic is below)
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
                if (TryGrab())
                {
                    AddReward(2.0f);
                    if (environment.CurrentLessonNumber == 1f) 
                    {
                        FinishEpisode(true, "Lesson 1 Success: Grabbed Bottle");
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
                    FinishEpisode(false, "Premature Release (Lesson 2+)");
                    return;
                }
            }
        }

        // --- 3. REWARDS ---
        // Reuse logic from RL agent
        float currentDistanceToBottle = Vector3.Distance(currentTargetPosition, environment.bottle.position);
        float currentDistanceToBin = environment.GetHorizontalDistanceToBin(environment.bottle.position);

        if (environment.CurrentLessonNumber == 0f) // Reach
        {
            float delta = previousDistanceToBottle - currentDistanceToBottle;
            AddReward(delta);
            if (currentDistanceToBottle < 0.05f) { AddReward(1.0f); FinishEpisode(true, "Lesson 0 Success: Reached Target"); }
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
                    environment.PlaySuccessSound();
                    FinishEpisode(true, "Lesson 2+ Success: Placed in Correct Bin");
                }
                else if (environment.CheckPlacementFailure())
                {
                    AddReward(-2.0f);
                    FinishEpisode(false, "Lesson 2+ Failure: Placed in Wrong Bin");
                }
            }
        }

        // Fail if bottle falls
        if (!IsHoldingObject() && (environment.bottle.position.y < (environment.bottleSpawnPoint.position.y - 0.4f)))
        {
            if (!environment.IsInBinZone())
            {
                 if (environment.CurrentLessonNumber >= 1f) AddReward(-1.0f);
                 FinishEpisode(false, "Failure: Bottle Fell Out of Bounds");
            }
        }

        if (StepCount >= MaxStep)
        {
            FinishEpisode(false, "Max Steps Reached");
        }
        
        _currentStepCount++;
    }

    public void OnPartCollision(string hitTag)
    {
        if (hitTag == "Conveyor" || hitTag == "RobotPart" || hitTag == "Ground")
        {  
            Debug.Log($"<color=red>[ArmAgent_IK_Recording] Collision ({hitTag}).</color>");
            AddReward(-1.0f); 
            FinishEpisode(false, $"Collision with {hitTag}");
        }
    }

    public void OnBottleHitGround()
    {
        AddReward(-1.0f);
        FinishEpisode(false, "Failure: Bottle Hit Ground");
    }

    private void FinishEpisode(bool success, string reason = "")
    {
        float reward = GetCumulativeReward();
        int steps = StepCount;
        float lesson = environment != null ? environment.CurrentLessonNumber : 0f;
        string result = success ? "SUCCESS" : "FAILURE";
        string color = success ? "green" : "red";
        
        Debug.Log($"<color={color}>[ArmAgent_IK_Recording] Episode Finished. Lesson: {lesson} | Result: {result} | Reward: {reward:F2} | Steps: {steps} | Reason: {reason}</color>");
        
        OnEpisodeCompleted?.Invoke(success, steps);
        
        // Stop Recorder explicitly to avoid recording the next "Reset" episode
        var recorder = GetComponent<DemonstrationRecorder>();
        if (recorder)
        {
            recorder.Record = false;
        }

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
        
        // Sync State Variables
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
        
        // If External Control (recording), we calculate the delta needed to get to the VR Target
        if (useExternalHeuristic)
        {
            float dt = Time.fixedDeltaTime;
            
            // 1. Calculate Vector Difference
            Vector3 diff = externalTargetPos - currentTargetPosition;
            
            // 2. Normalize by Speed*Time to get expected Input (-1 to 1)
            // OnActionReceived does: pos += action * speed * dt
            // So: action = diff / (speed * dt)
            
            float scale = 1.0f / (moveSpeed * dt);
            
            float actionX = Mathf.Clamp(diff.x * scale, -1f, 1f);
            float actionY = Mathf.Clamp(diff.y * scale, -1f, 1f);
            float actionZ = Mathf.Clamp(diff.z * scale, -1f, 1f);
            
            continuousActions[0] = actionX;
            continuousActions[1] = actionY;
            continuousActions[2] = actionZ;

            // Drill
            // Current uses simple addition: rot += action * speed * dt
            float drillDiff = externalTargetDrillRot - currentSmallSegmentDrillYRotation;
            // Normalize angle diff
            while (drillDiff > 180f) drillDiff -= 360f;
            while (drillDiff < -180f) drillDiff += 360f;
            
            float scaleRot = 1.0f / (rotationSpeed * dt);
            continuousActions[3] = Mathf.Clamp(drillDiff * scaleRot, -1f, 1f);
            
            // Claw
            continuousActions[4] = externalClawSignal ? 1.0f : -1.0f;
        }
        else
        {
            // Keyboard Fallback for Testing
            float moveX = 0f;
            float moveY = 0f;
            float moveZ = 0f;

            if (Input.GetKey(KeyCode.D)) moveX = 1f;
            if (Input.GetKey(KeyCode.A)) moveX = -1f;
            
            if (Input.GetKey(KeyCode.W)) moveZ = 1f;
            if (Input.GetKey(KeyCode.S)) moveZ = -1f;
            
            if (Input.GetKey(KeyCode.Q)) moveY = 1f;
            if (Input.GetKey(KeyCode.E)) moveY = -1f;

            continuousActions[0] = moveX; // X
            continuousActions[1] = moveY; // Y
            continuousActions[2] = moveZ; // Z

            continuousActions[3] = Input.GetKey(KeyCode.RightArrow) ? 1f : (Input.GetKey(KeyCode.LeftArrow) ? -1f : 0f); // Drill
            continuousActions[4] = Input.GetKey(KeyCode.Space) ? 1.0f : -1.0f; // Claw
        }
    }
}
