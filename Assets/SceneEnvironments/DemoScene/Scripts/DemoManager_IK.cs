using UnityEngine;
using System.Collections;

public class DemoManager_IK : MonoBehaviour
{
    [Header("References")]
    public ArmAgent_IK_Demo robotAgent;
    public SortingEnvironment_Demo environment;
    public ConveyorBelt_Demo conveyor;
    public Transform conveyorStartPoint;
    public Transform pickupZoneTrigger; // An empty object marking where the robot starts

    private bool waitingForReset = false;

    private void Start()
    {
        if (robotAgent == null || environment == null || conveyor == null)
        {
            Debug.LogError("<color=red>[DemoManager_IK] Missing References!</color>");
            return;
        }

        // Hook up events
        robotAgent.OnJobFinished += HandleJobFinished;
        robotAgent.isOperating = false; // Start disabled until bottle arrives

        // Start the loop
        StartCoroutine(SpawnAndTransportSequence());
    }

    private void Update()
    {
        // Reset Logic - Check for Inputs or Auto-Reset
        if (waitingForReset)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                waitingForReset = false;
                StartCoroutine(ResetSequence(DemoBottle.MaterialType.Plastic));
            }
            if (Input.GetKeyDown(KeyCode.T))
            {
                waitingForReset = false;
                StartCoroutine(ResetSequence(DemoBottle.MaterialType.Aluminum));
            }
        }
    }

    private IEnumerator SpawnAndTransportSequence()
    {
        Debug.Log("[DemoManager_IK] Sequence Started: SpawnAndTransport");

        // 1. Setup Bottle via Environment
        environment.ResetBottlePhysics(conveyorStartPoint.position, true); // Kinematic = true for conveyor
        
        // 2. Start Conveyor
        if (conveyor != null)
        {
            conveyor.targetObject = environment.bottleRb;
            conveyor.isMoving = true;
        }

        // 3. Wait until bottle reaches the robot
        // Simple distance check
        float dist = Vector3.Distance(new Vector3(environment.bottle.position.x, 0, environment.bottle.position.z), 
                                      new Vector3(pickupZoneTrigger.position.x, 0, pickupZoneTrigger.position.z));
        
        while (dist > 0.25f)
        {
             dist = Vector3.Distance(new Vector3(environment.bottle.position.x, 0, environment.bottle.position.z), 
                                      new Vector3(pickupZoneTrigger.position.x, 0, pickupZoneTrigger.position.z));
            yield return null;
        }

        // 4. Stop Conveyor
        conveyor.isMoving = false;
        environment.ResetBottlePhysics(environment.bottle.position, false); // Kinematic = false for grabbing
        
        // 5. Activate Robot
        yield return new WaitForSeconds(0.5f); 
        robotAgent.isOperating = true;
        Debug.Log("[DemoManager_IK] Robot Activated.");
    }

    private void HandleJobFinished()
    {
        Debug.Log("<color=green>[DemoManager_IK] Job Finished. Press 'R' to reset (Plastic) or 'T' (Aluminum).</color>");
        waitingForReset = true;
    }

    private IEnumerator ResetSequence(DemoBottle.MaterialType requestedType)
    {
        // 1. Reset Agent to clear internal state
        if (robotAgent != null)
        {
            robotAgent.ManualEndEpisode();
        }

        yield return null; // Wait for frame so OnEpisodeBegin runs

        // 2. Force the specific setup
        if (environment.bottleScript != null)
        {
            environment.bottleScript.material = requestedType; 
        }
        
        environment.ResetBottlePhysics(conveyorStartPoint.position, true); // Overwrites whatever OnEpisodeBegin did

        yield return new WaitForSeconds(0.2f);
        StartCoroutine(SpawnAndTransportSequence());
    }
}
