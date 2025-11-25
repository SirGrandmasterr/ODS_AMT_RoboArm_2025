using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using System.Collections.Generic; // Needed for List

/// <summary>
/// This agent controls the robot arm using Forward Kinematics (FK).
/// It directly outputs rotation values for each joint.
/// This agent should be used INSTEAD of the IK-based ArmAgent.cs.
/// </summary>
public class ArmAgent_FK : Agent
{
    [Header("Arm Joints (Transforms)")]
    [SerializeField] private Transform armbase;
    [SerializeField] private Transform firstSegment;
    [SerializeField] private Transform smallSegment;
    [SerializeField] private Transform smallSegmentDrill;

    [Header("Claw Components")]
    [SerializeField] private Transform claw1;
    [SerializeField] private Transform claw2;
    [Tooltip("The end effector (grip point) for observations and grabbing.")]
    [SerializeField] private Transform endEffector;

    [Header("Joint Limits")]
    [Tooltip("Min/Max Y-axis rotation for the base.")]
    [SerializeField] private Vector2 baseRotationLimits = new Vector2(-180f, 180f);
    [Tooltip("Min/Max Y-axis rotation for the first segment.")]
    [SerializeField] private Vector2 firstSegmentRotationLimits = new Vector2(-90f, 90f);
    [Tooltip("Min/Max Y-axis rotation for the small segment.")]
    [SerializeField] private Vector2 smallSegmentRotationLimits = new Vector2(-90f, 90f);
    [Tooltip("Min/Max Y-axis rotation for the drill/claw base.")]
    [SerializeField] private Vector2 drillRotationLimits = new Vector2(-180f, 180f);

    [Header("Environment")]
    [SerializeField] private Transform bottle;
    [SerializeField] private Transform targetLocation;
    [SerializeField] private Transform startLocation;
    [SerializeField] private Rigidbody bottleRb;

    [Header("Randomization")]
    [Tooltip("Center of the area to randomize bottle/target positions.")]
    [SerializeField] private Transform randomizationAreaCenter;
    [Tooltip("Extents of the randomization area (half-size of the box).")]
    [SerializeField] private Vector3 randomizationAreaExtents = new Vector3(0.5f, 0f, 0.5f);
    [Tooltip("The Y-level (height) to place the randomized objects at.")]
    [SerializeField] private float randomizationFloorHeight = -1.46f;

    [Header("Grabbing Logic")]
    [Tooltip("The radius around the end effector to check for objects to grab.")]
    [SerializeField] private float grabRadius = 0.1f;
    [Tooltip("A Rigidbody is required on the end effector to act as the anchor for the FixedJoint. This will be added if not present.")]
    [SerializeField] private Rigidbody endEffectorRb;

    // --- Private State ---
    private BottleTarget bottleScript;
    private Vector3 initialBottlePos;
    private Quaternion initialBottleRot;

    // Store the initial rotations as the "home" pose
    private Quaternion initialBaseRot;
    private Quaternion initialFirstSegRot;
    private Quaternion initialSmallSegRot;
    private Quaternion initialDrillRot;
    private Quaternion initialClaw1Rot;
    private Quaternion initialClaw2Rot;

    // Current target rotations (driven by agent)
    private float currentBaseYRotation;
    private float currentFirstSegmentYRotation;
    private float currentSmallSegmentYRotation;
    private float currentSmallSegmentDrillYRotation;
    private float claw1XRotation; // Target for claw 1
    private float claw2XRotation; // Target for claw 2
    
    // Grabbing physics
    private FixedJoint heldObjectJoint;
    private Rigidbody heldObjectRb;

    // Reward shaping
    private float lastDistanceToBottle;
    private float lastDistanceToTarget;

    public override void Initialize()
    {
        bottleScript = bottle.GetComponent<BottleTarget>();
        
        // --- Setup End Effector Rigidbody for Grabbing ---
        if (endEffector != null)
        {
            if (endEffectorRb == null)
            {
                endEffectorRb = endEffector.GetComponent<Rigidbody>();
            }
            if (endEffectorRb == null)
            {
                Debug.LogWarning("ArmAgent_FK: No Rigidbody found on EndEffector. Adding one.");
                endEffectorRb = endEffector.gameObject.AddComponent<Rigidbody>();
            }
            endEffectorRb.isKinematic = true; // It must be kinematic!
        }
        else
        {
            Debug.LogError("End Effector is not assigned! Grabbing will not work.");
        }

        // --- Store initial "home" pose ---
        if (armbase) initialBaseRot = armbase.localRotation;
        if (firstSegment) initialFirstSegRot = firstSegment.localRotation;
        if (smallSegment) initialSmallSegRot = smallSegment.localRotation;
        if (smallSegmentDrill) initialDrillRot = smallSegmentDrill.localRotation;
        if (claw1) initialClaw1Rot = claw1.localRotation;
        if (claw2) initialClaw2Rot = claw2.localRotation;
        
        if (bottle)
        {
            initialBottlePos = bottle.position;
            initialBottleRot = bottle.rotation;
        }

        // --- Fallback for randomization area ---
        if (randomizationAreaCenter == null)
        {
            Debug.LogWarning("Randomization Area Center not set! Defaulting to agent's transform.", this);
            randomizationAreaCenter = this.transform;
        }
    }

    public override void OnEpisodeBegin()
    {
        // Release any held object
        Release();

        // --- Randomize Positions (copied from ArmAgent.cs) ---
        startLocation.position = GetRandomPosition();
        targetLocation.position = GetRandomPosition();

        while (Vector3.Distance(startLocation.position, targetLocation.position) < 0.3f)
        {
            targetLocation.position = GetRandomPosition();
        }

        // --- Reset Arm to Home Pose ---
        ResetJointRotations();
        ApplyRotationsToTransforms(); // Force visual update

        // --- Reset Bottle ---
        bottle.position = startLocation.position + Vector3.up * 0.2f;
        bottle.rotation = initialBottleRot;
        bottleRb.linearVelocity = Vector3.zero;
        bottleRb.angularVelocity = Vector3.zero;

        // Reset BottleTarget script
        bottleScript.hasBeenPlaced = false;
        bottleScript.isHeld = false;

        // Initialize reward shaping distances
        lastDistanceToBottle = Vector3.Distance(endEffector.position, bottle.position);
        lastDistanceToTarget = Vector3.Distance(bottle.position, targetLocation.position);
    }

    /// <summary>
    /// Resets all target joint rotations to their initial values.
    /// </summary>
    private void ResetJointRotations()
    {
        // Get the initial Y euler angles
        // Note: We use the *weird* Y-axis rotations to match ArmControls.cs
        currentBaseYRotation = initialBaseRot.eulerAngles.y;
        currentFirstSegmentYRotation = initialFirstSegRot.eulerAngles.y;
        currentSmallSegmentYRotation = initialSmallSegRot.eulerAngles.y;
        currentSmallSegmentDrillYRotation = initialDrillRot.eulerAngles.y;
        
        // Reset claws to "open"
        claw1XRotation = -90.0f;
        claw2XRotation = 90.0f;
    }

    /// <summary>
    /// Generates a random position within the defined randomization area.
    /// </summary>
    private Vector3 GetRandomPosition()
    {
        Vector3 center = randomizationAreaCenter.position;
        float randX = Random.Range(-randomizationAreaExtents.x, randomizationAreaExtents.x);
        float randZ = Random.Range(-randomizationAreaExtents.z, randomizationAreaExtents.z);
        return new Vector3(center.x + randX, randomizationFloorHeight, center.z + randZ);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // --- Observe Joint States (Normalized) ---
        // 5 observations
        sensor.AddObservation(Mathf.InverseLerp(baseRotationLimits.x, baseRotationLimits.y, currentBaseYRotation));
        sensor.AddObservation(Mathf.InverseLerp(firstSegmentRotationLimits.x, firstSegmentRotationLimits.y, currentFirstSegmentYRotation));
        sensor.AddObservation(Mathf.InverseLerp(smallSegmentRotationLimits.x, smallSegmentRotationLimits.y, currentSmallSegmentYRotation));
        sensor.AddObservation(Mathf.InverseLerp(drillRotationLimits.x, drillRotationLimits.y, currentSmallSegmentDrillYRotation));
        sensor.AddObservation(Mathf.InverseLerp(-28f, -90f, claw1XRotation)); // 1=open, 0=closed
        
        // --- Observe World State (Relative to Agent) ---
        // 9 observations (3 pos * 3 vectors)
        sensor.AddObservation(transform.InverseTransformPoint(endEffector.position));
        sensor.AddObservation(transform.InverseTransformPoint(bottle.position));
        sensor.AddObservation(transform.InverseTransformPoint(targetLocation.position));
        
        // --- Observe Relative Vectors ---
        // 6 observations (2 pos * 3 vectors)
        sensor.AddObservation(bottle.position - endEffector.position); // Vector from effector to bottle
        sensor.AddObservation(targetLocation.position - bottle.position); // Vector from bottle to target
        
        // --- Observe Grab State ---
        // 1 observation
        sensor.AddObservation(IsHoldingObject());
        
        // --- TOTAL: 5 + 9 + 6 + 1 = 21 observations ---
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- Action 1: Control 4 Joints (Continuous) ---
        float rotationSpeed = 100f; // Tunable: How fast joints move
        
        // Action 0: Base Y Rotation
        currentBaseYRotation += actions.ContinuousActions[0] * Time.fixedDeltaTime * rotationSpeed;
        currentBaseYRotation = Mathf.Clamp(currentBaseYRotation, baseRotationLimits.x, baseRotationLimits.y);

        // Action 1: First Segment Y Rotation
        currentFirstSegmentYRotation += actions.ContinuousActions[1] * Time.fixedDeltaTime * rotationSpeed;
        currentFirstSegmentYRotation = Mathf.Clamp(currentFirstSegmentYRotation, firstSegmentRotationLimits.x, firstSegmentRotationLimits.y);

        // Action 2: Small Segment Y Rotation
        currentSmallSegmentYRotation += actions.ContinuousActions[2] * Time.fixedDeltaTime * rotationSpeed;
        currentSmallSegmentYRotation = Mathf.Clamp(currentSmallSegmentYRotation, smallSegmentRotationLimits.x, smallSegmentRotationLimits.y);

        // Action 3: Drill/Claw Y Rotation
        currentSmallSegmentDrillYRotation += actions.ContinuousActions[3] * Time.fixedDeltaTime * rotationSpeed;
        currentSmallSegmentDrillYRotation = Mathf.Clamp(currentSmallSegmentDrillYRotation, drillRotationLimits.x, drillRotationLimits.y);
        
        // --- Action 2: Control Claw (Discrete) ---
        bool isHolding = IsHoldingObject();
        var clawAction = actions.DiscreteActions[0];

        if (clawAction == 1) // Open
        {
            claw1XRotation = -90.0f;
            claw2XRotation = 90.0f;
            Release();

            // Reward for releasing over the target
            if (isHolding && bottleScript.isOverTarget)
            {
                AddReward(2.0f); // Positive reward for correct release
            }
        }
        else if (clawAction == 2) // Close
        {
            claw1XRotation = -28.0f;
            claw2XRotation = 28.0f;
            
            bool didGrab = Grab();
            if (didGrab)
            {
                AddReward(1.0f); // Reward for successful grab
            }
        }
        
        // --- Reward Shaping ---
        if (!IsHoldingObject())
        {
            // Reward for progress towards bottle
            float currentDistance = Vector3.Distance(endEffector.position, bottle.position);
            float progress = lastDistanceToBottle - currentDistance;
            AddReward(progress);
            lastDistanceToBottle = currentDistance;
        }
        else
        {
            // Reward for progress towards target
            float currentDistance = Vector3.Distance(bottle.position, targetLocation.position);
            float progress = lastDistanceToTarget - currentDistance;
            AddReward(progress);
            lastDistanceToTarget = currentDistance;
        }

        // Penalty for existing (encourages speed)
        AddReward(-0.0005f);

        // Penalty for large actions (encourages smooth movement)
        float actionMagnitude = 0f;
        for(int i=0; i<4; i++) { actionMagnitude += actions.ContinuousActions[i] * actions.ContinuousActions[i]; }
        AddReward(-0.001f * actionMagnitude);


        // Check for success (from BottleTarget script)
        if (bottleScript.hasBeenPlaced)
        {
            AddReward(5.0f);
            EndEpisode();
        }
    }

    /// <summary>
    /// Apply the target rotations to the actual Transforms.
    /// This is called from FixedUpdate to stay in sync with physics.
    /// </summary>
    private void ApplyRotationsToTransforms()
    {
        // Apply rotations using the same (unusual) axes as ArmControls.cs
        if (armbase) armbase.localRotation = Quaternion.Euler(0f, currentBaseYRotation, 0f);
        if (firstSegment) firstSegment.localRotation = Quaternion.Euler(0f, currentFirstSegmentYRotation, 0f);
        if (smallSegment) smallSegment.localRotation = Quaternion.Euler(-180f, currentSmallSegmentYRotation, 0f);
        if (smallSegmentDrill) smallSegmentDrill.localRotation = Quaternion.Euler(0f, currentSmallSegmentDrillYRotation, 0f);
        
        // Lerp claws for smooth animation
        if (claw1)
        {
            Quaternion targetClaw1Rotation = Quaternion.Euler(claw1XRotation, 0f, 0f);
            claw1.localRotation = Quaternion.Lerp(claw1.localRotation, targetClaw1Rotation, Time.fixedDeltaTime * 15f);
        }
        if (claw2)
        {
            Quaternion targetClaw2Rotation = Quaternion.Euler(claw2XRotation, 0f, 0f);
            claw2.localRotation = Quaternion.Lerp(claw2.localRotation, targetClaw2Rotation, Time.fixedDeltaTime * 15f);
        }
    }

    void FixedUpdate()
    {
        // Apply the rotations calculated in OnActionReceived
        ApplyRotationsToTransforms();
    }
    
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        var discreteActions = actionsOut.DiscreteActions;
        
        continuousActions.Clear();
        discreteActions.Clear();

        // Manual controls mapping (matches ArmControls.cs)
        // Base: A/D
        continuousActions[0] = Input.GetKey(KeyCode.D) ? 1f : (Input.GetKey(KeyCode.A) ? -1f : 0f);
        // First Segment: W/S
        continuousActions[1] = Input.GetKey(KeyCode.W) ? -1f : (Input.GetKey(KeyCode.S) ? 1f : 0f);
        // Small Segment: Up/Down
        continuousActions[2] = Input.GetKey(KeyCode.UpArrow) ? -1f : (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        // Drill: Left/Right
        continuousActions[3] = Input.GetKey(KeyCode.RightArrow) ? 1f : (Input.GetKey(KeyCode.LeftArrow) ? -1f : 0f);

        // Claw: Space
        if (Input.GetKeyDown(KeyCode.Space) && !Input.GetKey(KeyCode.LeftShift))
        {
            discreteActions[0] = 2; // Close
        }
        else if (Input.GetKeyDown(KeyCode.Space) && Input.GetKey(KeyCode.LeftShift))
        {
            discreteActions[0] = 1; // Open
        }
        else
        {
            discreteActions[0] = 0; // NoOp
        }
    }

    // --- Grabbing Logic (Copied from ArmControls.cs) ---
    // These are needed because this agent no longer uses ArmControls.cs

    public bool Grab()
    {
        if (heldObjectJoint != null || endEffectorRb == null) return false;

        Collider[] colliders = Physics.OverlapSphere(endEffector.position, grabRadius);
        if (colliders.Length == 0) return false;

        foreach (var col in colliders)
        {
            Rigidbody rb = col.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                heldObjectRb = rb;
                heldObjectJoint = heldObjectRb.gameObject.AddComponent<FixedJoint>();
                heldObjectJoint.connectedBody = endEffectorRb;

                BottleTarget bottleScript = heldObjectRb.GetComponent<BottleTarget>();
                if (bottleScript != null) bottleScript.isHeld = true;
                
                return true;
            }
        }
        return false;
    }

    public void Release()
    {
        if (heldObjectJoint == null) return;

        if (heldObjectRb != null)
        {
            BottleTarget bottleScript = heldObjectRb.GetComponent<BottleTarget>();
            if (bottleScript != null) bottleScript.isHeld = false;
        }

        Destroy(heldObjectJoint);
        heldObjectJoint = null;
        heldObjectRb = null;
    }

    public bool IsHoldingObject()
    {
        return heldObjectJoint != null;
    }
    
    // Called if bottle hits the ground
    public void OnBottleDropped()
    {
        if (!bottleScript.hasBeenPlaced)
        {
            AddReward(-1.0f);
            EndEpisode();
        }
    }
}