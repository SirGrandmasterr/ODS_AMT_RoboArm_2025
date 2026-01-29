using System;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Random = UnityEngine.Random;

[RequireComponent(typeof(DecisionRequester))]
public class ArmAgent_RL : Agent
{
    [Header("Environment Connection")]
    [Tooltip("Drag the SortingEnvironment object here.")]
    [SerializeField] private SortingEnvironment environment;
    
    [Header("Initialization Config")]
    [SerializeField] private InitializationConfig initConfig;

    [Header("Arm Joints (Transforms)")]
    [SerializeField] private Transform armbase;
    [SerializeField] private Transform firstSegment;
    [SerializeField] private Transform smallSegment;
    [SerializeField] private Transform smallSegmentDrill;

    [Header("Claw Components")]
    [SerializeField] private Transform claw1;
    [SerializeField] private Transform claw2;
    [SerializeField] private Transform endEffector;

    [Header("Joint Limits")]
    [SerializeField] private Vector2 baseRotationLimits = new Vector2(-180f, 180f);
    [SerializeField] private Vector2 firstSegmentRotationLimits = new Vector2(-90f, 90f);
    [SerializeField] private Vector2 smallSegmentRotationLimits = new Vector2(-90f, 90f);
    [SerializeField] private Vector2 drillRotationLimits = new Vector2(-180f, 180f);

    [Header("Grabbing Logic")]
    [SerializeField] private float grabRadius = 0.1f;
    [SerializeField] private Rigidbody endEffectorRb;

    // Joint Control State
    private float currentBaseYRotation;
    private float currentFirstSegmentYRotation;
    private float currentSmallSegmentYRotation;
    private float currentSmallSegmentDrillYRotation;
    private float claw1XRotation;
    private float claw2XRotation;
    
    // Grabbing State
    private Rigidbody heldObjectRb; 
    
    // Reward Tracking
    private float previousDistanceToBottle;
    private float previousDistanceToBin;

    public override void Initialize()
    {
        // 1. Check Environment
        if (environment == null)
        {
            // First try finding it on this object
            environment = GetComponent<SortingEnvironment>();
            
            // Then try finding it globally (fallback)
            if (environment == null)
            {
                environment = FindFirstObjectByType<SortingEnvironment>();
            }

            if (environment == null)
            {
                Debug.LogError($"<color=red><b>[ArmAgent_RL] FATAL ERROR:</b> 'Environment' field is not assigned and no SortingEnvironment found in scene! Agent cannot function.</color>", this);
                // We don't disable the agent to allow for fix, but later methods will fail.
            }
        }

        // 2. Check Joints
        if (armbase == null || firstSegment == null || smallSegment == null || smallSegmentDrill == null)
        {
             Debug.LogError($"<color=red><b>[ArmAgent_RL] ERROR:</b> One or more Arm Joints (Transforms) are not assigned in the Inspector!</color>", this);
        }

        // 3. Setup DecisionRequester if needed (Auto-added by RequireComponent, but ensure settings)
        var dr = GetComponent<DecisionRequester>();
        if (dr != null && dr.DecisionPeriod == 0) dr.DecisionPeriod = 5; // Default safety

        if (endEffector != null)
        {
            if (endEffectorRb == null) endEffectorRb = endEffector.GetComponent<Rigidbody>();
            if (endEffectorRb == null) endEffectorRb = endEffector.gameObject.AddComponent<Rigidbody>();
            endEffectorRb.isKinematic = true; 
        }
        
        if (initConfig == null) initConfig = GetComponent<InitializationConfig>();
        
        // Safety: Ensure MaxStep is not 0 (infinite)
        if (MaxStep == 0) MaxStep = 5000;
    }

    public override void OnEpisodeBegin()
    {
        if (environment == null) return; // Prevent crash

        // 1. Reset Environment Logic
        environment.ResetEnvironment();

        // 2. Reset Robot Join Config
        ResetJointRotations();
        ApplyRotationsToTransforms();
        Release(); // Drop anything we might be holding from prev episode

        // 3. Special Lesson Handling (Lesson 2: Place -> Force Grab)
        // If the environment set up a 'Place' lesson, we might want to cheat and grab immediately.
        // We can check the internal lesson number from the environment.
        if (environment.bottleRb != null && environment.CurrentLessonNumber == 2.0f)
        {
            // Move bottle to EE? Or EE to bottle?
            // "SetupLesson_Place" in environment puts it at spawn.
            // Let's teleport bottle to hand and grab.
            environment.ResetBottlePhysics(endEffector.position, true);
            ForceGrab(environment.bottleRb);
        }

        // 4. Initialize Reward Deltas
        previousDistanceToBottle = Vector3.Distance(endEffector.position, environment.bottle.position);
        previousDistanceToBin = environment.GetHorizontalDistanceToBin(environment.bottle.position);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 5 Joint Angles
        sensor.AddObservation(Mathf.InverseLerp(baseRotationLimits.x, baseRotationLimits.y, currentBaseYRotation));
        sensor.AddObservation(Mathf.InverseLerp(firstSegmentRotationLimits.x, firstSegmentRotationLimits.y, currentFirstSegmentYRotation));
        sensor.AddObservation(Mathf.InverseLerp(smallSegmentRotationLimits.x, smallSegmentRotationLimits.y, currentSmallSegmentYRotation));
        sensor.AddObservation(Mathf.InverseLerp(drillRotationLimits.x, drillRotationLimits.y, currentSmallSegmentDrillYRotation));
        sensor.AddObservation(Mathf.InverseLerp(-90f, -28f, claw1XRotation)); 

        // 1 Material
        sensor.AddObservation((int)environment.bottleScript.material);

        // 12 World Positions (Self, Bottle, Bins)
        sensor.AddObservation(transform.InverseTransformPoint(endEffector.position));
        sensor.AddObservation(transform.InverseTransformPoint(environment.bottle.position));
        sensor.AddObservation(transform.InverseTransformPoint(environment.targetBinAluminum.position));
        sensor.AddObservation(transform.InverseTransformPoint(environment.targetBinPlastic.position));
        
        // 3 Relative Vector
        sensor.AddObservation(environment.bottle.position - endEffector.position); 

        // 3 Bottle Orientation
        sensor.AddObservation(environment.bottle.up);

        // 1 Holding State
        sensor.AddObservation(IsHoldingObject());

        // 1 Lesson
        sensor.AddObservation(environment.CurrentLessonNumber);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- 1. MOVEMENT ---
        float rotationSpeed = 300f; 
        float dt = Time.fixedDeltaTime;
        
        currentBaseYRotation += actions.ContinuousActions[0] * dt * rotationSpeed;
        currentFirstSegmentYRotation += actions.ContinuousActions[1] * dt * rotationSpeed;
        currentSmallSegmentYRotation += actions.ContinuousActions[2] * dt * rotationSpeed;
        currentSmallSegmentDrillYRotation += actions.ContinuousActions[3] * dt * rotationSpeed;
        
        float clawInput = actions.ContinuousActions[4];
        bool wasHolding = IsHoldingObject();

        if (clawInput > 0.5f) // Close
        {
            claw1XRotation = -28.0f;
            claw2XRotation = 28.0f;
            
            if (!wasHolding)
            {
                // Grab Logic
                // Only allow grab if we are not in 'Place' lesson (Lesson 2 typically starts holding)
                // But generally, we allow regrab if it fell?
                // For Lesson 1 (Grab), we reward grab.
                if (TryGrab())
                {
                    AddReward(1.0f);
                    if (environment.CurrentLessonNumber == 1f) EndEpisode();
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
                // Critical Check: Did we drop it in a safe zone?
                if (environment.CurrentLessonNumber >= 2f && !environment.IsInBinZone())
                {
                    AddReward(-1.0f);
                    EndEpisode();
                    return;
                }
            }
        }

        // --- 2. REWARDS ---
        float currentDistanceToBottle = Vector3.Distance(endEffector.position, environment.bottle.position);
        float currentDistanceToBin = environment.GetHorizontalDistanceToBin(environment.bottle.position);

        if (environment.CurrentLessonNumber == 0f) // Reach
        {
            float delta = previousDistanceToBottle - currentDistanceToBottle;
            AddReward(delta);
            if (currentDistanceToBottle < 0.05f) { AddReward(1.0f); EndEpisode(); }
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
                 
                 // Penalty if very close to bin but still holding? (Encourage release)
                 if (currentDistanceToBin < 0.3f) AddReward(-0.005f); 
             }
        }
        
        previousDistanceToBottle = currentDistanceToBottle;
        previousDistanceToBin = currentDistanceToBin;
        
        // Time Penalty
        AddReward(-0.0001f);

        // --- 3. SUCCESS CHECK (Placement) ---
        if (environment.CurrentLessonNumber >= 2f)
        {
            // Only check success if we released it
            if (!IsHoldingObject())
            {
                if (environment.CheckPlacementSuccess())
                {
                    AddReward(5.0f);
                    Debug.Log($"<color=green>[ArmAgent_RL] SUCCESS: Bottle placed in CORRECT bin! Reward: {GetCumulativeReward():F2}</color>");
                    EndEpisode();
                }
                else if (environment.CheckPlacementFailure())
                {
                    AddReward(-2.0f);
                    Debug.Log($"<color=yellow>[ArmAgent_RL] FAIL: Bottle placed in WRONG bin. Reward: {GetCumulativeReward():F2}</color>");
                    EndEpisode();
                }
            }
        }

        // --- 4. FAIL CHECK (Fell off world) ---
        if (!IsHoldingObject() && (environment.bottle.position.y < (environment.bottleSpawnPoint.position.y - 0.5f)))
        {
            // If it fell and wasn't in a bin
            if (!environment.IsInBinZone())
            {
                 if (environment.CurrentLessonNumber >= 1f) AddReward(-1.0f);
                 Debug.Log($"<color=red>[ArmAgent_RL] FAILED: Bottle fell out of bounds. Reward: {GetCumulativeReward():F2}</color>");
                 EndEpisode();
            }
        }
    }

    public void OnPartCollision(string hitTag)
    {
        if (hitTag == "Conveyor" || hitTag == "RobotPart" || hitTag == "Ground")
        {
            AddReward(-1.0f); 
            Debug.Log($"<color=orange>[ArmAgent_RL] COLLISION FAIL: Hit {hitTag}</color>");
            EndEpisode();
        }
    }

    private void FixedUpdate()
    {

        ApplyRotationsToTransforms();
        ApplyClawRotations();
    }

    // --- Helpers ---
    
    private void ResetJointRotations()
    {
        if (initConfig != null)
        {
            initConfig.GetStartRotations(
                baseRotationLimits, firstSegmentRotationLimits, smallSegmentRotationLimits, drillRotationLimits,
                out currentBaseYRotation, out currentFirstSegmentYRotation, out currentSmallSegmentYRotation, out currentSmallSegmentDrillYRotation
            );
        }
        else
        {
            currentBaseYRotation = Random.Range(baseRotationLimits.x, baseRotationLimits.y);
            currentFirstSegmentYRotation = Random.Range(firstSegmentRotationLimits.x, firstSegmentRotationLimits.y);
            currentSmallSegmentYRotation = Random.Range(smallSegmentRotationLimits.x, smallSegmentRotationLimits.y);
            currentSmallSegmentDrillYRotation = Random.Range(drillRotationLimits.x, drillRotationLimits.y);
        }
        
        claw1XRotation = -90.0f;
        claw2XRotation = 90.0f;
    }

    private void ApplyRotationsToTransforms()
    {
         if (armbase) armbase.localRotation = Quaternion.Euler(0f, currentBaseYRotation, 0f);
         if (firstSegment) firstSegment.localRotation = Quaternion.Euler(0f, currentFirstSegmentYRotation, 0f);
         if (smallSegment) smallSegment.localRotation = Quaternion.Euler(-180f, currentSmallSegmentYRotation, 0f);
         if (smallSegmentDrill) smallSegmentDrill.localRotation = Quaternion.Euler(0f, currentSmallSegmentDrillYRotation, 0f);
    }
    
    private void ApplyClawRotations()
    {
        float lerpSpeed = 15f;
        if (claw1) claw1.localRotation = Quaternion.Lerp(claw1.localRotation, Quaternion.Euler(claw1XRotation, 0f, 0f), Time.fixedDeltaTime * lerpSpeed);
        if (claw2) claw2.localRotation = Quaternion.Lerp(claw2.localRotation, Quaternion.Euler(claw2XRotation, 0f, 0f), Time.fixedDeltaTime * lerpSpeed);
    }

    private bool TryGrab()
    {
        // Simple overlap sphere check against the ENV Bottle only
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
        continuousActions[0] = Input.GetKey(KeyCode.D) ? 1f : (Input.GetKey(KeyCode.A) ? -1f : 0f);
        continuousActions[1] = Input.GetKey(KeyCode.W) ? -1f : (Input.GetKey(KeyCode.S) ? 1f : 0f);
        continuousActions[2] = Input.GetKey(KeyCode.UpArrow) ? -1f : (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        continuousActions[3] = Input.GetKey(KeyCode.RightArrow) ? 1f : (Input.GetKey(KeyCode.LeftArrow) ? -1f : 0f);
        continuousActions[4] = Input.GetKey(KeyCode.Space) ? 1.0f : -1.0f;
    }
}
