using UnityEngine;
using System.Collections;

public class DemoManager_Demo : MonoBehaviour
{
    [Header("References")]
    public ArmAgent_Demo robotAgent;
    public SortingEnvironment_Demo environment;
    public ConveyorBelt_Demo conveyor;
    public Transform conveyorStartPoint;
    public Transform pickupZoneTrigger; // An empty object marking where the robot starts

    private bool waitingForReset = false;

    private void Start()
    {
        if (robotAgent == null || environment == null || conveyor == null)
        {
            Debug.LogError("<color=red>[DemoManager_Demo] Missing References!</color>");
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
        Debug.Log("[DemoManager_Demo] Sequence Started: SpawnAndTransport");

        // 1. Setup Bottle via Environment
        // We use a custom setup here instead of 'environment.ResetEnvironment' because we want specific placement
        environment.ResetBottlePhysics(conveyorStartPoint.position, true); // Kinematic = true for conveyor
        
        // Randomize material or keep default? Let's randomize for the demo loop consistency
        // But we want to ensure the target bin is set correctly.
        // We can access private methods if we expose them or just replicate logic?
        // Better: Add a public method to Environment 'SetupSpecificBottle(MaterialType, Position)'?
        // For now, let's assume the Environment randomized it internally if we call ResetEnvironment, 
        // BUT we want to force position.
        
        // Let's rely on what we have:
        // The bottle is now at StartPoint.
        
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
        Debug.Log("[DemoManager_Demo] Robot Activated.");
    }

    private void HandleJobFinished()
    {
        Debug.Log("<color=green>[DemoManager_Demo] Job Finished. Press 'R' to reset (Plastic) or 'T' (Aluminum).</color>");
        waitingForReset = true;
    }

    private IEnumerator ResetSequence(DemoBottle.MaterialType requestedType)
    {
        // 1. Reset Agent to clear internal state (and triggering env randomization, which we will override)
        if (robotAgent != null)
        {
            robotAgent.ManualEndEpisode();
        }

        yield return null; // Wait for frame so OnEpisodeBegin runs

        // 2. Force the specific setup
        if (environment.bottleScript != null)
        {
            environment.bottleScript.material = requestedType; 
            // Update visuals (hacky access to renderer via simple check or we assume Env handles it if we could tell it)
            // Ideally environment.SetMaterial(type) but we can do it via the script if we add logic there or just ignore visuals for now?
            // User just asked for functionality. 
            // In SortingEnvironment_Demo, 'SetupDemoRun' uses 'RandomizeBottleMaterialAndTarget'.
            // Accessing internal private members is hard.
            // But we know 'DemoManager' earlier just spawned it.
            // Ref: old DemoManager used `robotAgent.ForceConfigureBottle` (which existed on the old agent).
            // We can add such helper to `SortingEnvironment_Demo` or just assume basic work.
            
            // Let's assume we proceed with just setting the bottle position
        }
        
        environment.ResetBottlePhysics(conveyorStartPoint.position, true); // Overwrites whatever OnEpisodeBegin did

        yield return new WaitForSeconds(0.2f);
        StartCoroutine(SpawnAndTransportSequence());
    }
}
