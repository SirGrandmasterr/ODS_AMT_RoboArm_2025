using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class ArmAgent : Agent
{
    [Header("References")]
    [SerializeField] private ArmControls armControls;
    [SerializeField] private Transform endEffector;
    [SerializeField] private Transform bottle;
    [SerializeField] private Transform targetLocation;
    [SerializeField] private Transform startLocation;
    [SerializeField] private Rigidbody bottleRb;

    [Header("Training Improvements")]
    [Tooltip("Center of the area to randomize bottle/target positions. Create an empty GameObject and place it in the middle of the arm's workspace.")]
    [SerializeField] private Transform randomizationAreaCenter;
    [Tooltip("Extents of the randomization area (half-size of the box).")]
    [SerializeField] private Vector3 randomizationAreaExtents = new Vector3(0.5f, 0f, 0.5f);
    [Tooltip("The Y-level (height) to place the randomized objects at.")]
    [SerializeField] private float randomizationFloorHeight = -1.46f; // Default to StartBlock's original height

    private BottleTarget bottleScript;
    private Vector3 homeIkTargetPos;
    
    // --- Variables for Reward Shaping ---
    private float lastDistanceToBottle;
    private float lastDistanceToTarget;
    

    public override void Initialize()
    {
        armControls = GetComponent<ArmControls>();
        bottleScript = bottle.GetComponent<BottleTarget>();
        
        // ArmControls.cs now handles this in its Start() method.

        // Store the "home" position for the IK target
        homeIkTargetPos = armControls.GetIKTarget() ? armControls.GetIKTarget().position : endEffector.position;

        // --- Fallback for randomization area ---
        if (randomizationAreaCenter == null)
        {
            Debug.LogWarning("Randomization Area Center not set! Please create an empty GameObject in the workspace and assign it. Defaulting to agent's transform.", this);
            randomizationAreaCenter = this.transform; 
        }
    }

    public override void OnEpisodeBegin()
    {
        // Just tell ArmControls to release anything it might be holding from a failed episode.
        armControls.Release();
        
        // --- Randomize Positions ---
        // This is crucial for generalization.
        startLocation.position = GetRandomPosition();
        targetLocation.position = GetRandomPosition();
        
        // Ensure they aren't too close to each other
        while (Vector3.Distance(startLocation.position, targetLocation.position) < 0.3f) // 30cm
        {
            targetLocation.position = GetRandomPosition();
        }

        // Reset Arm
        armControls.SetAiControl(true);
        armControls.SetIkTargetPosition_AI(homeIkTargetPos);
        armControls.SetClawState_AI(true); // Open claw

        // Reset Bottle
        bottle.position = startLocation.position + Vector3.up * 0.2f; // Place on *new* start pos
        bottle.rotation = Quaternion.identity;
        bottleRb.linearVelocity = Vector3.zero;
        bottleRb.angularVelocity = Vector3.zero;

        // Reset BottleTarget script
        bottleScript.hasBeenPlaced = false;
        bottleScript.isHeld = false; 

        // --- Initialize reward shaping distances ---
        lastDistanceToBottle = Vector3.Distance(endEffector.position, bottle.position);
        lastDistanceToTarget = Vector3.Distance(bottle.position, targetLocation.position);
    }

    /// <summary>
    /// Generates a random position within the defined randomization area.
    /// </summary>
    private Vector3 GetRandomPosition()
    {
        Vector3 center = randomizationAreaCenter.position;
        float randX = Random.Range(-randomizationAreaExtents.x, randomizationAreaExtents.x);
        float randZ = Random.Range(-randomizationAreaExtents.z, randomizationAreaExtents.z);
        
        // Return position on the specified floor height
        return new Vector3(center.x + randX, randomizationFloorHeight, center.z + randZ);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Agent observations (21 total)
        sensor.AddObservation(endEffector.position);             // 3
        sensor.AddObservation(endEffector.rotation);             // 4
        sensor.AddObservation(bottle.position);                  // 3
        sensor.AddObservation(bottle.rotation);                  // 4
        sensor.AddObservation(targetLocation.position);          // 3
        
        // --- NEW OBSERVATION ---
        // This is the CRITICAL fix. The agent can now see
        // where its "intent" (the red ball) actually is after clamping.
        if (armControls.GetIKTarget() != null)
        {
            sensor.AddObservation(armControls.GetIKTarget().position); // 3
        }
        else
        {
            // Failsafe in case IK target isn't ready
            sensor.AddObservation(endEffector.position); // 3
        }
        // -------------------------

        sensor.AddObservation(armControls.GetClawState());       // 1 (1=open, 0=closed)
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- Continuous Actions (Move IK Target) ---
        // 3 continuous actions for X, Y, Z velocity
        float moveSpeed = 5f;
        Vector3 velocity = new Vector3(
            actions.ContinuousActions[0],
            actions.ContinuousActions[1],
            actions.ContinuousActions[2]
        ) * moveSpeed;
        
        armControls.MoveIkTarget_AI(velocity);

        // --- Discrete Actions (Claw Control) ---
        // 1 discrete action with 3 branches: 0=NoOp, 1=Open, 2=Close
        var clawAction = actions.DiscreteActions[0];
        
        // --- Cache holding state ---
        bool isHolding = armControls.IsHoldingObject();

        // --- Refactored to use ArmControls ---
        if (clawAction == 1) // Open
        {
            armControls.SetClawState_AI(true);
            armControls.Release(); // Use public method

            // --- NEW RELEASE REWARD ---
            // If we were holding something, and we are over the target,
            // and we just chose to release, REWARD IT!
            if (isHolding && bottleScript.isOverTarget)
            {
                AddReward(2.0f); // Positive reward for correct release
            }
            // --------------------------
        }
        else if (clawAction == 2) // Close
        {
            armControls.SetClawState_AI(false);
            
            // Try to grab and check if it was successful
            bool didGrab = armControls.Grab(); // Use public method
            
            // Only reward if we *just* grabbed it (i.e., Grab() returned true)
            if (didGrab)
            {
                AddReward(1.0f);
            }
        }
        
        if (!isHolding) // Use cached state
        {
            // Reward for *progress* towards bottle
            float currentDistance = Vector3.Distance(endEffector.position, bottle.position);
            float progress = lastDistanceToBottle - currentDistance;
            AddReward(progress); // Reward getting closer
            lastDistanceToBottle = currentDistance;
        }
        else
        {
            // Reward for *progress* towards target
            float currentDistance = Vector3.Distance(bottle.position, targetLocation.position);
            float progress = lastDistanceToTarget - currentDistance;
            AddReward(progress); // Reward getting closer
            lastDistanceToTarget = currentDistance;
        }

        // Penalty for existing (encourages speed)
        AddReward(-0.0005f);

        // Check for success (from BottleTarget script)
        if (bottleScript.hasBeenPlaced)
        {
            AddReward(5.0f);
            EndEpisode();
        }
    }

    // Called if bottle hits the ground
    public void OnBottleDropped()
    {
        // Only penalize if we weren't *already* placed
        if (!bottleScript.hasBeenPlaced)
        {
            AddReward(-1.0f);
            EndEpisode();
        }
    }
}