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

    private Rigidbody endEffectorRb;
    private FixedJoint bottleGrabJoint;
    private BottleTarget bottleScript;
    private Vector3 homeIkTargetPos;
    private bool isBottleHeld = false;

    public override void Initialize()
    {
        armControls = GetComponent<ArmControls>();
        bottleScript = bottle.GetComponent<BottleTarget>();
        
        // Add a kinematic Rigidbody to the end effector for the joint
        endEffectorRb = endEffector.gameObject.AddComponent<Rigidbody>();
        endEffectorRb.isKinematic = true;

        // Store the "home" position for the IK target
        homeIkTargetPos = armControls.GetIKTarget() ? armControls.GetIKTarget().position : endEffector.position;
    }

    public override void OnEpisodeBegin()
    {
        // Detach bottle if held
        if (isBottleHeld)
        {
            ReleaseBottle();
        }

        // Reset Arm
        armControls.SetAiControl(true);
        armControls.SetIkTargetPosition_AI(homeIkTargetPos);
        armControls.SetClawState_AI(true); // Open claw

        // Reset Bottle
        bottle.position = startLocation.position + Vector3.up * 0.2f;
        bottle.rotation = Quaternion.identity;
        bottleRb.linearVelocity = Vector3.zero;
        bottleRb.angularVelocity = Vector3.zero;

        // Reset Target
        // You can randomize this position for better training
        // targetLocation.position = ...

        isBottleHeld = false;
        bottleScript.hasBeenPlaced = false;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Agent observations (14 total)
        sensor.AddObservation(endEffector.position);             // 3
        sensor.AddObservation(endEffector.rotation);             // 4
        sensor.AddObservation(bottle.position);                  // 3
        sensor.AddObservation(targetLocation.position);          // 3
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
        if (clawAction == 1) // Open
        {
            armControls.SetClawState_AI(true);
            ReleaseBottle();
        }
        else if (clawAction == 2) // Close
        {
            armControls.SetClawState_AI(false);
            TryGrabBottle();
        }

        // --- Rewards ---
        if (!isBottleHeld)
        {
            // Reward for moving effector towards bottle
            float distanceToBottle = Vector3.Distance(endEffector.position, bottle.position);
            AddReward(-distanceToBottle * 0.01f);
        }
        else
        {
            // Reward for moving bottle towards target
            float distanceToTarget = Vector3.Distance(bottle.position, targetLocation.position);
            AddReward(-distanceToTarget * 0.01f);
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

    private void TryGrabBottle()
    {
        if (isBottleHeld) return;

        // Check if effector is close enough to grab
        if (Vector3.Distance(endEffector.position, bottle.position) < 0.1f)
        {
            // Create joint
            bottleGrabJoint = bottle.gameObject.AddComponent<FixedJoint>();
            bottleGrabJoint.connectedBody = endEffectorRb;
            isBottleHeld = true;
            bottleScript.isHeld = true;
            AddReward(1.0f);
        }
    }

    public void ReleaseBottle()
    {
        if (!isBottleHeld || bottleGrabJoint == null) return;

        Destroy(bottleGrabJoint);
        bottleGrabJoint = null;
        isBottleHeld = false;
        bottleScript.isHeld = false;
    }

    // Called if bottle hits the ground
    public void OnBottleDropped()
    {
        AddReward(-1.0f);
        EndEpisode();
    }
}