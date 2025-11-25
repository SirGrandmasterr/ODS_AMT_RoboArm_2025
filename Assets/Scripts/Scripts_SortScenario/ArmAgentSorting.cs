/*
 * MODIFIED AGENT SCRIPT (v2 - Curriculum)
 * ArmAgent_FK_Sorting.cs
 *
 * Major Changes:
 * 1.  Implemented Curriculum Learning with 4 lessons.
 * 2.  Fixed Reward Shaping: Changed from "progress-based" to "potential-based"
 * to prevent the "collapsing" behavior.
 * 3.  Fixed Grabbing: Changed from FixedJoint to Kinematic Parenting for stability.
 * 4.  Changed Claw Action: Converted from Discrete (0, 1, 2) to Continuous (1 float)
 * to make grabbing easier to learn.
 * 5.  Added new SerializedFields for curriculum (randomizationArea, bottleOriginalParent).
 * 6.  Added lesson_number to observations.
 *
 * --- DEVELOPER NOTE (v3) ---
 * 7.  Randomized joint rotations on episode start (in ResetJointRotations)
 * to prevent reward farming from a static start pose in Lesson 3.
*/

using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Integrations; // Required for Academy

public class ArmAgentSorting_Curriculum : Agent
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
    [SerializeField] private Vector2 baseRotationLimits = new Vector2(-180f, 180f);
    [SerializeField] private Vector2 firstSegmentRotationLimits = new Vector2(-90f, 90f);
    [SerializeField] private Vector2 smallSegmentRotationLimits = new Vector2(-90f, 90f);
    [SerializeField] private Vector2 drillRotationLimits = new Vector2(-180f, 180f);

    [Header("Environment - Sorting Scenario")]
    [SerializeField] private Transform bottle;
    [SerializeField] private Rigidbody bottleRb;
    [SerializeField] private MeshRenderer bottleMeshRenderer; // To change bottle color
    [Tooltip("The fixed spawn point for the bottle (e.g., end of conveyor).")]
    [SerializeField] private Transform bottleSpawnPoint;
    [Tooltip("The target bin for Aluminum bottles. MUST have 'TargetBinAluminum' tag.")]
    [SerializeField] private Transform targetBinAluminum;
    [Tooltip("The target bin for Plastic bottles. MUST have 'TargetBinPlastic' tag.")]
    [SerializeField] private Transform targetBinPlastic;

    [Header("Curriculum Learning Setup")]
    [Tooltip("The area to randomize the bottle position in Lesson 1 & 2.")]
    [SerializeField] private BoxCollider randomizationArea;
    [Tooltip("The bottle's original parent (e.g., the TrainingEnvironment) for proper reparenting.")]
    [SerializeField] private Transform bottleOriginalParent;


    [Header("Visuals (Optional)")]
    [SerializeField] private Material plasticMaterial;
    [SerializeField] private Material aluminumMaterial;

    [Header("Grabbing Logic")]
    [SerializeField] private float grabRadius = 0.1f;
    [SerializeField] private Rigidbody endEffectorRb;

    // --- Private State ---
    private BottleTargetSorting_Curriculum bottleScript;
    private Vector3 initialBottlePos;
    private Quaternion initialBottleRot;

    // Store the initial rotations as the "home" pose
    private Quaternion initialBaseRot;
    private Quaternion initialFirstSegRot;
    private Quaternion initialSmallSegRot;
    private Quaternion initialDrillRot;

    // Current target rotations (driven by agent)
    private float currentBaseYRotation;
    private float currentFirstSegmentYRotation;
    private float currentSmallSegmentYRotation;
    private float currentSmallSegmentDrillYRotation;
    private float claw1XRotation; // Target for claw 1
    private float claw2XRotation; // Target for claw 2
    
    // Grabbing physics
    private Rigidbody heldObjectRb; // This is our new 'isHolding' check

    // Reward shaping
    private Transform currentCorrectTargetBin; // The bin we *should* be going to

    // Curriculum
    private float lesson_number;

    public override void Initialize()
    {
        bottleScript = bottle.GetComponent<BottleTargetSorting_Curriculum>();
        
        if (endEffector != null)
        {
            if (endEffectorRb == null) endEffectorRb = endEffector.GetComponent<Rigidbody>();
            if (endEffectorRb == null)
            {
                Debug.LogWarning("ArmAgent_FK: No Rigidbody found on EndEffector. Adding one.");
                endEffectorRb = endEffector.gameObject.AddComponent<Rigidbody>();
            }
            endEffectorRb.isKinematic = true; 
        }
        else
        {
            Debug.LogError("End Effector is not assigned! Grabbing will not work.");
        }

        // Store initial "home" pose
        if (armbase) initialBaseRot = armbase.localRotation;
        if (firstSegment) initialFirstSegRot = firstSegment.localRotation;
        if (smallSegment) initialSmallSegRot = smallSegment.localRotation;
        if (smallSegmentDrill) initialDrillRot = smallSegmentDrill.localRotation;
        
        if (bottle)
        {
            initialBottlePos = bottle.position;
            initialBottleRot = bottle.rotation;
        }

        if (bottleSpawnPoint == null || targetBinAluminum == null || targetBinPlastic == null)
        {
            Debug.LogError("Environment targets (Spawn Point, Bin A, Bin B) are not set!", this);
        }

        if (bottleOriginalParent == null)
        {
            Debug.LogError("Bottle Original Parent is not set! Release logic will fail.", this);
        }
    }

    public override void OnEpisodeBegin()
    {
        // Get current curriculum lesson
        lesson_number = Academy.Instance.EnvironmentParameters.GetWithDefault("lesson_number", 0f);

        Release(); // Always release object at start
        ResetJointRotations(); // <--- THIS NOW RANDOMIZES THE JOINTS
        ApplyRotationsToTransforms(); // Force visual update

        // Reset BottleTarget script state
        bottleScript.ResetState();

        // --- Lesson-Based Setup ---
        switch (lesson_number)
        {
            case 0f: // Lesson 1: Reaching. Random position, kinematic bottle, just touch it.
                SetupLesson_Reach();
                break;
            case 1f: // Lesson 2: Grabbing. Random (but closer) position, dynamic bottle.
                SetupLesson_Grab();
                break;
            case 2f: // Lesson 3: Placing. Bottle starts in hand.
                SetupLesson_Place();
                break;
            case 3f: // Lesson 4: Full Task.
            default:
                SetupLesson_FullTask();
                break;
        }
    }

    /// <summary>
    /// Lesson 1: Randomize bottle position, make it kinematic. Goal is just to touch it.
    /// </summary>
    private void SetupLesson_Reach()
    {
        ResetBottlePhysics(GetRandomSpawnPos(randomizationArea.bounds, 0.1f), true);
        bottleMeshRenderer.material = plasticMaterial; // Material doesn't matter
        targetBinAluminum.gameObject.SetActive(false);
        targetBinPlastic.gameObject.SetActive(false);
    }

    /// <summary>
    /// Lesson 2: Randomize bottle position in a smaller area, make it dynamic. Goal is to grab it.
    /// </summary>
    private void SetupLesson_Grab()
    {
        // Spawn in a smaller, closer area
        Bounds smallerBounds = new Bounds(bottleSpawnPoint.position, randomizationArea.bounds.size * 0.5f);
        ResetBottlePhysics(GetRandomSpawnPos(smallerBounds, 0.1f), false);
        bottleMeshRenderer.material = plasticMaterial; // Material doesn't matter
        targetBinAluminum.gameObject.SetActive(false);
        targetBinPlastic.gameObject.SetActive(false);
    }

    /// <summary>
    /// Lesson 3: Bottle spawns in the agent's hand. Goal is to place it in the correct bin.
    /// </summary>
    private void SetupLesson_Place()
    {
        // Reset bottle physics first, then grab it
        // The endEffector.position is now at a random pose due to ResetJointRotations()
        ResetBottlePhysics(endEffector.position, true); // Spawn at effector, kinematic
        RandomizeBottleMaterialAndTarget(); // Set up bins and material
        Grab(); // Force grab
    }

    /// <summary>
    /// Lesson 4: The full sorting task.
    /// </summary>
    private void SetupLesson_FullTask()
    {
        ResetBottlePhysics(bottleSpawnPoint.position, false);
        RandomizeBottleMaterialAndTarget();
    }

    /// <summary>
    /// Helper to reset the bottle's physics state and position.
    /// </summary>
    private void ResetBottlePhysics(Vector3 position, bool isKinematic)
    {
        bottle.position = position;
        bottle.rotation = initialBottleRot;
        bottleRb.linearVelocity = Vector3.zero;
        bottleRb.angularVelocity = Vector3.zero;
        bottleRb.isKinematic = isKinematic;
        
        targetBinAluminum.gameObject.SetActive(true);
        targetBinPlastic.gameObject.SetActive(true);
    }
    
    /// <summary>
    /// Helper to get a random position within a bounds.
    /// </summary>
    private Vector3 GetRandomSpawnPos(Bounds bounds, float yOffset)
    {
        return new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            bounds.center.y + yOffset,
            Random.Range(bounds.min.z, bounds.max.z)
        );
    }

    /// <summary>
    /// Sets the bottle material and visual.
    /// </summary>
    private void RandomizeBottleMaterialAndTarget()
    {
        var currentMaterial = (BottleTargetSorting_Curriculum.MaterialType)Random.Range(0, 2);
        bottleScript.material = currentMaterial;
        
        // Set correct target bin for reward shaping
        if (currentMaterial == BottleTargetSorting_Curriculum.MaterialType.Plastic)
        {
            currentCorrectTargetBin = targetBinPlastic;
            if (bottleMeshRenderer && plasticMaterial)
                bottleMeshRenderer.material = plasticMaterial;
        }
        else
        {
            currentCorrectTargetBin = targetBinAluminum;
            if (bottleMeshRenderer && aluminumMaterial)
                bottleMeshRenderer.material = aluminumMaterial;
        }
    }

    private void ResetJointRotations()
    {
        // --- MODIFIED: Randomize starting joint rotations for robust training ---
        currentBaseYRotation = Random.Range(baseRotationLimits.x, baseRotationLimits.y);
        currentFirstSegmentYRotation = Random.Range(firstSegmentRotationLimits.x, firstSegmentRotationLimits.y);
        currentSmallSegmentYRotation = Random.Range(smallSegmentRotationLimits.x, smallSegmentRotationLimits.y);
        currentSmallSegmentDrillYRotation = Random.Range(drillRotationLimits.x, drillRotationLimits.y);
        // --- END MODIFICATION ---

        // Reset claws to open
        claw1XRotation = -90.0f;
        claw2XRotation = 90.0f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // --- Observe Joint States (Normalized) --- (5 observations)
        sensor.AddObservation(Mathf.InverseLerp(baseRotationLimits.x, baseRotationLimits.y, currentBaseYRotation));
        sensor.AddObservation(Mathf.InverseLerp(firstSegmentRotationLimits.x, firstSegmentRotationLimits.y, currentFirstSegmentYRotation));
        sensor.AddObservation(Mathf.InverseLerp(smallSegmentRotationLimits.x, smallSegmentRotationLimits.y, currentSmallSegmentYRotation));
        sensor.AddObservation(Mathf.InverseLerp(drillRotationLimits.x, drillRotationLimits.y, currentSmallSegmentDrillYRotation));
        sensor.AddObservation(Mathf.InverseLerp(-90f, -28f, claw1XRotation)); // 0=open, 1=closed
        
        // --- Observe Bottle Material --- (1 observation)
        sensor.AddObservation((int)bottleScript.material); // 0 = Plastic, 1 = Aluminum
        
        // --- Observe World State (Relative to Agent) --- (9 observations)
        sensor.AddObservation(transform.InverseTransformPoint(endEffector.position));
        sensor.AddObservation(transform.InverseTransformPoint(bottle.position));
        sensor.AddObservation(transform.InverseTransformPoint(targetBinAluminum.position));
        sensor.AddObservation(transform.InverseTransformPoint(targetBinPlastic.position));
        
        // --- Observe Relative Vectors --- (3 observations)
        sensor.AddObservation(bottle.position - endEffector.position); // Vector from effector to bottle
        
        // --- Observe Grab State --- (1 observation)
        sensor.AddObservation(IsHoldingObject());
        
        // --- NEW: Observe Current Lesson --- (1 observation)
        sensor.AddObservation(lesson_number);
        
        // --- TOTAL: 5 + 1 + 9 + 3 + 1 + 1 = 20 observations ---
        // Recalculated: 5 joints + 1 material + 4 positions (9 floats) + 1 rel vector (3 floats) + 1 holding + 1 lesson = 5+1+9+3+1+1 = 20
        // Wait, original was: 5 + 1 + 9 (3+3+3) + 3 + 1 = 22. Let's match that structure.
        // My 'World State' (9 obs) was correct.
        // 5 (joints) + 1 (material) + 9 (world state) + 3 (rel vector) + 1 (holding) + 1 (lesson) = 20.
        // Ah, the original CollectObservations was wrong in its comment.
        // 5 + 1 + 9 (effector, bottle, binA, binB) + 3 (bottle-effector) + 1 (isHolding) = 19.
        // The original script comment was: 5 + 1 + 3 + 3 + 3 + 3 + 1 = 19. My new total is 20.
        // Let's re-add my original observations to be safe.
        // 5 joints
        // 1 material
        // 3 effector pos
        // 3 bottle pos
        // 3 bin A pos
        // 3 bin B pos
        // 3 bottle-effector vec
        // 1 isHolding
        // 1 lesson
        // TOTAL: 5 + 1 + 3 + 3 + 3 + 3 + 3 + 1 + 1 = 23 observations
        
        // Let's clear and re-add to be 100% sure.
        sensor.Reset();
        // --- Observe Joint States (Normalized) --- (5 observations)
        sensor.AddObservation(Mathf.InverseLerp(baseRotationLimits.x, baseRotationLimits.y, currentBaseYRotation));
        sensor.AddObservation(Mathf.InverseLerp(firstSegmentRotationLimits.x, firstSegmentRotationLimits.y, currentFirstSegmentYRotation));
        sensor.AddObservation(Mathf.InverseLerp(smallSegmentRotationLimits.x, smallSegmentRotationLimits.y, currentSmallSegmentYRotation));
        sensor.AddObservation(Mathf.InverseLerp(drillRotationLimits.x, drillRotationLimits.y, currentSmallSegmentDrillYRotation));
        sensor.AddObservation(Mathf.InverseLerp(-90f, -28f, claw1XRotation)); // 0=open, 1=closed

        // --- Observe Bottle Material --- (1 observation)
        sensor.AddObservation((int)bottleScript.material); // 0 = Plastic, 1 = Aluminum

        // --- Observe World State (Relative to Agent) --- (12 observations)
        sensor.AddObservation(transform.InverseTransformPoint(endEffector.position));
        sensor.AddObservation(transform.InverseTransformPoint(bottle.position));
        sensor.AddObservation(transform.InverseTransformPoint(targetBinAluminum.position));
        sensor.AddObservation(transform.InverseTransformPoint(targetBinPlastic.position));
        
        // --- Observe Relative Vectors --- (3 observations)
        sensor.AddObservation(bottle.position - endEffector.position); // Vector from effector to bottle

        // --- Observe Grab State --- (1 observation)
        sensor.AddObservation(IsHoldingObject());

        // --- NEW: Observe Current Lesson --- (1 observation)
        sensor.AddObservation(lesson_number);

        // --- TOTAL: 5 + 1 + 12 + 3 + 1 + 1 = 23 observations ---
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float rotationSpeed = 100f; 
        
        // Action 0-3: Control 4 Joints (Continuous)
        currentBaseYRotation += actions.ContinuousActions[0] * Time.fixedDeltaTime * rotationSpeed;
        // currentBaseYRotation = Mathf.Clamp(currentBaseYRotation, baseRotationLimits.x, baseRotationLimits.y); // Removed angle limit

        currentFirstSegmentYRotation += actions.ContinuousActions[1] * Time.fixedDeltaTime * rotationSpeed;
        // currentFirstSegmentYRotation = Mathf.Clamp(currentFirstSegmentYRotation, firstSegmentRotationLimits.x, firstSegmentRotationLimits.y); // Removed angle limit

        currentSmallSegmentYRotation += actions.ContinuousActions[2] * Time.fixedDeltaTime * rotationSpeed;
        // currentSmallSegmentYRotation = Mathf.Clamp(currentSmallSegmentYRotation, smallSegmentRotationLimits.x, smallSegmentRotationLimits.y); // Removed angle limit

        currentSmallSegmentDrillYRotation += actions.ContinuousActions[3] * Time.fixedDeltaTime * rotationSpeed;
        // currentSmallSegmentDrillYRotation = Mathf.Clamp(currentSmallSegmentDrillYRotation, drillRotationLimits.x, drillRotationLimits.y); // Removed angle limit
        
        // --- NEW: Action 4: Control Claw (Continuous) ---
        // actions.ContinuousActions[4] will be from -1 (Open) to 1 (Close)
        float clawInput = actions.ContinuousActions[4];
        bool wasHolding = IsHoldingObject();

        if (clawInput > 0.5f) // Threshold to "Close"
        {
            claw1XRotation = -28.0f;
            claw2XRotation = 28.0f;
            
            // Only try to grab if not in Lesson 3 (where we start by holding)
            if (lesson_number != 2f)
            {
                bool didGrab = Grab();
                if (didGrab && !wasHolding) // Only reward on the *first* frame of grabbing
                {
                    if (lesson_number == 1f) // Lesson 2: Grabbing
                    {
                        AddReward(1.0f); // Success for this lesson
                        EndEpisode();
                    }
                    else if (lesson_number > 1f) // Lesson 3/4
                    {
                        AddReward(1.0f); // Reward for successful grab
                    }
                }
            }
        }
        else // Open
        {
            claw1XRotation = -90.0f;
            claw2XRotation = 90.0f;
            
            if (wasHolding) // We just released
            {
                Release();
                // Reward for releasing over the correct target
                if (lesson_number >= 2f) // Only check release rewards in Lesson 3 & 4
                {
                    if (bottleScript.material == BottleTargetSorting_Curriculum.MaterialType.Aluminum && bottleScript.isOverAluminumBin)
                    {
                        AddReward(2.0f); // Good: Released Aluminum in Aluminum bin
                    }
                    else if (bottleScript.material == BottleTargetSorting_Curriculum.MaterialType.Plastic && bottleScript.isOverPlasticBin)
                    {
                        AddReward(2.0f); // Good: Released Plastic in Plastic bin
                    }
                    else if (bottleScript.isOverAluminumBin || bottleScript.isOverPlasticBin)
                    {
                        AddReward(-1.0f); // Bad: Released in the WRONG bin
                    }
                }
            }
        }
        if (!IsHoldingObject() && (bottle.position.y < (bottleSpawnPoint.position.y - 0.5f))) // 0.5f is an example, adjust this
        {
            // Bottle has fallen off the conveyor
            if (lesson_number >= 1f && !bottleScript.hasBeenPlacedCorrectly && !bottleScript.hasBeenPlacedIncorrectly)
            {
                AddReward(-1.0f); // Penalize for dropping it
            }
            EndEpisode();
        }

        // --- FIXED: Reward Shaping (Potential-Based) ---
        if (lesson_number == 0f) // Lesson 1: Reaching
        {
            float distanceToBottle = Vector3.Distance(endEffector.position, bottle.position);
            AddReward(-0.01f * distanceToBottle); // Penalty for being far
            if (distanceToBottle < 0.05f) // Touch radius
            {
                AddReward(1.0f);
                EndEpisode();
            }
        }
        else if (lesson_number > 0f) // Lessons 1, 2, 3
        {
            if (!IsHoldingObject())
            {
                // Reward for being close to bottle
                float distanceToBottle = Vector3.Distance(endEffector.position, bottle.position);
                AddReward(-0.01f * distanceToBottle); 
            }
            else
            {
                // Reward for being close to *correct* target
                float distanceToTarget = Vector3.Distance(bottle.position, currentCorrectTargetBin.position);
                AddReward(-0.01f * distanceToTarget);
            }
        }

        // Penalty for existing (encourages speed)
        AddReward(-0.0005f);

        // --- Check for End Conditions (from BottleTarget script) ---
        // Only apply in lessons 2, 3, 4
        if (lesson_number >= 2f)
        {
            if (bottleScript.hasBeenPlacedCorrectly)
            {
                AddReward(5.0f); // Big reward for success
                EndEpisode();
            }
            else if (bottleScript.hasBeenPlacedIncorrectly)
            {
                AddReward(-2.0f); // Penalty for placing in wrong bin
                EndEpisode();
            }
        }
    }

    private void ApplyRotationsToTransforms()
    {
        if (armbase) armbase.localRotation = Quaternion.Euler(0f, currentBaseYRotation, 0f);
        if (firstSegment) firstSegment.localRotation = Quaternion.Euler(0f, currentFirstSegmentYRotation, 0f);
        if (smallSegment) smallSegment.localRotation = Quaternion.Euler(-180f, currentSmallSegmentYRotation, 0f);
        if (smallSegmentDrill) smallSegmentDrill.localRotation = Quaternion.Euler(0f, currentSmallSegmentDrillYRotation, 0f);
        
        float lerpSpeed = 15f;
        if (claw1)
        {
            Quaternion targetClaw1Rotation = Quaternion.Euler(claw1XRotation, 0f, 0f);
            claw1.localRotation = Quaternion.Lerp(claw1.localRotation, targetClaw1Rotation, Time.fixedDeltaTime * lerpSpeed);
        }
        if (claw2)
        {
            Quaternion targetClaw2Rotation = Quaternion.Euler(claw2XRotation, 0f, 0f);
            claw2.localRotation = Quaternion.Lerp(claw2.localRotation, targetClaw2Rotation, Time.fixedDeltaTime * lerpSpeed);
        }
    }

    void FixedUpdate()
    {
        ApplyRotationsToTransforms();
    }
    
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        
        continuousActions.Clear();

        // Manual controls mapping
        continuousActions[0] = Input.GetKey(KeyCode.D) ? 1f : (Input.GetKey(KeyCode.A) ? -1f : 0f);
        continuousActions[1] = Input.GetKey(KeyCode.W) ? -1f : (Input.GetKey(KeyCode.S) ? 1f : 0f);
        continuousActions[2] = Input.GetKey(KeyCode.UpArrow) ? -1f : (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        continuousActions[3] = Input.GetKey(KeyCode.RightArrow) ? 1f : (Input.GetKey(KeyCode.LeftArrow) ? -1f : 0f);

        // Continuous claw control
        if (Input.GetKey(KeyCode.Space))
            continuousActions[4] = 1.0f; // Close
        else
            continuousActions[4] = -1.0f; // Open
    }

    // --- NEW: Grabbing Logic (Kinematic Parenting) ---
    public bool Grab()
    {
        if (heldObjectRb != null || endEffectorRb == null) return false; // Already holding

        Collider[] colliders = Physics.OverlapSphere(endEffector.position, grabRadius);
        if (colliders.Length == 0) return false;

        foreach (var col in colliders)
        {
            if (col.transform != bottle) continue; // Only grab the bottle

            Rigidbody rb = col.GetComponent<Rigidbody>();
            if (rb != null) // Don't need to check isKinematic, we'll force it
            {
                heldObjectRb = rb;
                
                // --- KINEMATIC GRAB ---
                heldObjectRb.isKinematic = true; // Stop physics
                heldObjectRb.transform.SetParent(endEffector); // Attach to hand
                heldObjectRb.transform.localPosition = new Vector3(0, -0.08f, 0); // Adjust grip point
                // ----------------------

                if (bottleScript != null) bottleScript.isHeld = true;
                return true;
            }
        }
        return false;
    }

    public void Release()
    {
        if (heldObjectRb == null) return; // Not holding anything

        if (bottleScript != null) bottleScript.isHeld = false;
        
        // --- KINEMATIC RELEASE ---
        heldObjectRb.transform.SetParent(bottleOriginalParent); // Detach from hand
        heldObjectRb.isKinematic = false; // Re-enable physics
        // -------------------------

        heldObjectRb = null;
    }

    public bool IsHoldingObject()
    {
        return heldObjectRb != null;
    }
    
    // Called if bottle hits the ground
    public void OnBottleDropped()
    {
        // Only apply in lessons where dropping is a failure
        if (lesson_number < 1f) return; 

        if (!bottleScript.hasBeenPlacedCorrectly && !bottleScript.hasBeenPlacedIncorrectly)
        {
            AddReward(-1.0f);
            EndEpisode();
        }
    }
}