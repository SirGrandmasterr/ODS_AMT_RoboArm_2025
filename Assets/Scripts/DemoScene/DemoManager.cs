using UnityEngine;
using System.Collections;

public class DemoManager : MonoBehaviour
{
    [Header("References")]
    public ArmAgentSorting_Curriculum robotAgent;
    public ConveyorBelt conveyor;
    public GameObject bottlePrefab; // Optional: If you want to instantiate new ones
    public Transform conveyorStartPoint;
    public Transform pickupZoneTrigger; // An empty object marking where the robot starts

    [Header("Bottle Reference")]
    public GameObject currentBottle;
    public Rigidbody currentBottleRb;

    private bool waitingForReset = false;

    private void Start()
    {
        LogSceneSetup();

        // Ensure Robot is in Demo Mode
        if (robotAgent != null)
        {
            robotAgent.isDemoMode = true;
            robotAgent.isBrainActive = false;
            robotAgent.OnJobFinished += HandleJobFinished;
        }
        else
        {
            Debug.LogError("<color=red>[DemoManager] CRITICAL ERROR: Robot Agent reference is missing!</color>");
        }

        // Start the loop
        StartCoroutine(SpawnAndTransportSequence());
    }

    private void LogSceneSetup()
    {
        Debug.Log("<b><color=yellow>--- DEMO SCENE INITIALIZATION ---</color></b>");
        
        // Log Objects
        Debug.Log($"[Reference] Robot Agent: {(robotAgent != null ? robotAgent.name : "<color=red>NULL</color>")}");
        Debug.Log($"[Reference] Conveyor Belt: {(conveyor != null ? conveyor.name : "<color=red>NULL</color>")}");
        Debug.Log($"[Reference] Start Point: {(conveyorStartPoint != null ? conveyorStartPoint.position.ToString() : "<color=red>NULL</color>")}");
        Debug.Log($"[Reference] Pickup Trigger: {(pickupZoneTrigger != null ? pickupZoneTrigger.position.ToString() : "<color=red>NULL</color>")}");
        
        // Log Bottle
        if (currentBottle != null)
        {
            Debug.Log($"[Reference] Current Bottle: {currentBottle.name} | Position: {currentBottle.transform.position}");
            if (currentBottleRb == null) Debug.LogError("<color=red>[DemoManager] Bottle Rigidbody is missing!</color>");
        }
        else
        {
            Debug.LogError("<color=red>[DemoManager] Current Bottle is NULL!</color>");
        }

        Debug.Log("<b><color=yellow>-----------------------------------</color></b>");
    }

    private void Update()
    {
        // Reset Logic - Check for Inputs
        if (waitingForReset)
        {
            // R for Plastic (Default Reset)
            if (Input.GetKeyDown(KeyCode.R))
            {
                Debug.Log("[DemoManager] User Input: Reset (R) detected - Spawning PLASTIC.");
                waitingForReset = false;
                StartCoroutine(ResetSequence(BottleTargetSorting_Curriculum.MaterialType.Plastic));
            }
            
            // T for Aluminum (Test Mode)
            if (Input.GetKeyDown(KeyCode.T))
            {
                Debug.Log("[DemoManager] User Input: Test (T) detected - Spawning ALUMINUM.");
                waitingForReset = false;
                StartCoroutine(ResetSequence(BottleTargetSorting_Curriculum.MaterialType.Aluminum));
            }
        }
    }

    private IEnumerator SpawnAndTransportSequence()
    {
        Debug.Log("[DemoManager] Sequence Started: SpawnAndTransport");

        // 1. Setup Bottle
        if (currentBottle == null)
        {
             Debug.LogError("[DemoManager] Sequence Aborted: Bottle is null.");
             yield break;
        }

        currentBottle.transform.position = conveyorStartPoint.position;
        currentBottle.transform.rotation = Quaternion.identity;
        currentBottleRb.isKinematic = true; // Temporary kinematic to prevent falling through moving belt

        Debug.Log($"[DemoManager] Bottle Teleported to Start: {conveyorStartPoint.position}. Physics Disabled (Kinematic=true).");

        // 2. Start Conveyor
        if (conveyor != null)
        {
            conveyor.targetObject = currentBottleRb;
            conveyor.isMoving = true;
            Debug.Log("[DemoManager] Conveyor Belt ACTIVATED. Target: " + currentBottle.name);
        }
        else
        {
             Debug.LogError("[DemoManager] Sequence Aborted: Conveyor reference is null.");
             yield break;
        }

        // 3. Wait until bottle reaches the robot
        float dist = Vector2.Distance(new Vector2(currentBottle.transform.position.x, currentBottle.transform.position.z), 
                                      new Vector2(pickupZoneTrigger.position.x, pickupZoneTrigger.position.z));
        
        Debug.Log($"[DemoManager] Waiting for transport. Initial Distance to Trigger: {dist:F3}");

        while (dist > 0.25f)
        {
            dist = Vector2.Distance(new Vector2(currentBottle.transform.position.x, currentBottle.transform.position.z), 
                                    new Vector2(pickupZoneTrigger.position.x, pickupZoneTrigger.position.z));
            // Note: We don't log every frame here to avoid freezing the editor, 
            // but if the bottle gets stuck, check the Scene View to see if it's moving.
            yield return null;
        }

        Debug.Log($"[DemoManager] Arrival Detected! Distance: {dist:F3} (Threshold: 0.25).");

        // 4. Stop Conveyor
        conveyor.isMoving = false;
        currentBottleRb.isKinematic = false; // Physics back on for the robot to grab it
        Debug.Log("[DemoManager] Conveyor STOPPED. Bottle Physics Re-enabled (Kinematic=false).");

        // 5. Activate Robot
        yield return new WaitForSeconds(0.5f); // Small dramatic pause
        
        Debug.Log("<b><color=cyan>[DemoManager] Activating Robot Brain...</color></b>");
        if (robotAgent != null)
        {
            // --- CRITICAL FIX: Inject the bottle reference ---
            // This ensures the Robot's internal 'bottleScript' variable is NOT null
            robotAgent.SetDemoBottle(currentBottle);
            // -------------------------------------------------

            robotAgent.isBrainActive = true;
            Debug.Log($"[DemoManager] Robot is now ACTIVE. \n" +
                      $"    - Aware of Bottle: {currentBottle.name} @ {currentBottle.transform.position}\n" +
                      $"    - Robot Mode: {(robotAgent.isDemoMode ? "Demo" : "Training")}");
        }
    }

    private void HandleJobFinished()
    {
        Debug.Log("<color=green>[DemoManager] Event Received: Sorting Job Finished.</color> Press 'R' for Plastic or 'T' for Aluminum.");
        waitingForReset = true;
        // Optional: Show UI "Press R to Reset"
    }

    private IEnumerator ResetSequence(BottleTargetSorting_Curriculum.MaterialType requestedType)
    {
        Debug.Log($"[DemoManager] Resetting Sequence... Type: {requestedType}");
        
        currentBottle.SetActive(false);
        
        var bottleScript = currentBottle.GetComponent<BottleTargetSorting_Curriculum>();
        if (bottleScript != null)
        {
            bottleScript.ResetState();
        }
       
        if (robotAgent != null)
        {
            robotAgent.ForceConfigureBottle(currentBottle, requestedType);
        }

        yield return new WaitForSeconds(0.2f);
        
        currentBottle.SetActive(true);
       
        StartCoroutine(SpawnAndTransportSequence());
    }
}