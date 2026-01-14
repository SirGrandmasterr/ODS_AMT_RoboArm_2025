/*
 * OPTIMIZED AGENT SCRIPT (v4.7 - Added Demo Configuration Helper)
 * ArmAgentSorting_Curriculum.cs
*/

using System;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Random = UnityEngine.Random;

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
    [SerializeField] private MeshRenderer bottleMeshRenderer; 
    [SerializeField] private Transform bottleSpawnPoint;
    [SerializeField] private Transform targetBinAluminum;
    [SerializeField] private Transform targetBinPlastic;

    [Header("Curriculum Learning Setup")]
    [SerializeField] private BoxCollider randomizationArea;
    [SerializeField] private Transform bottleOriginalParent;

    [Header("Visuals")]
    [SerializeField] private Material plasticMaterial;
    [SerializeField] private Material aluminumMaterial;

    [Header("Grabbing Logic")]
    [SerializeField] private float grabRadius = 0.1f;
    [SerializeField] private Rigidbody endEffectorRb;
    
    [Header("Demo Settings")]
    public bool isDemoMode = false; // Check this in the Inspector for the Demo Scene
    public bool isBrainActive = false; // The DemoManager will toggle this
    public event Action OnJobFinished;
    
    // --- Private State ---
    private BottleTargetSorting_Curriculum bottleScript;
    private Quaternion initialBottleRot;

    // Joint Control
    private float currentBaseYRotation;
    private float currentFirstSegmentYRotation;
    private float currentSmallSegmentYRotation;
    private float currentSmallSegmentDrillYRotation;
    private float claw1XRotation; 
    private float claw2XRotation; 
    
    // Grabbing
    private Rigidbody heldObjectRb; 

    // Optimization: Delta Reward Tracking
    private float previousDistanceToBottle;
    private float previousDistanceToBin;
    private Transform currentCorrectTargetBin; 
    private float lesson_number;

    // --- DEBUG / MANUAL MODE STATE ---
    private bool manualDebugMode = false;

    public void SetManualDebugMode(bool active)
    {
        manualDebugMode = active;
        Debug.Log($"<color=cyan><b>[Agent]</b> Manual Debug Mode set to: {active}</color>");
        EndEpisode(); // Trigger OnEpisodeBegin to apply changes
    }
    
    // --- NEW: Helper to configure bottle type from DemoManager ---
    public void ForceConfigureBottle(GameObject bottleObj, BottleTargetSorting_Curriculum.MaterialType matType)
    {
        if (bottleObj == null) return;

        var script = bottleObj.GetComponent<BottleTargetSorting_Curriculum>();
        var rend = bottleObj.GetComponent<MeshRenderer>();

        // 1. Set Logic Type
        if (script != null) script.material = matType;

        // 2. Set Visual Material
        if (rend != null)
        {
            if (matType == BottleTargetSorting_Curriculum.MaterialType.Plastic && plasticMaterial != null)
            {
                rend.material = plasticMaterial;
            }
            else if (matType == BottleTargetSorting_Curriculum.MaterialType.Aluminum && aluminumMaterial != null)
            {
                rend.material = aluminumMaterial;
            }
        }
    }

    public void SetDemoBottle(GameObject newBottle)
    {
        if (newBottle == null) return;
        
        // 1. Update Transforms
        bottle = newBottle.transform;
        bottleRb = newBottle.GetComponent<Rigidbody>();
        bottleScript = newBottle.GetComponent<BottleTargetSorting_Curriculum>();
        
        // 2. CRITICAL FIX: Determine the Target Bin based on Material
        // The Agent needs this to calculate rewards and check for success
        if (bottleScript != null)
        {
            if (bottleScript.material == BottleTargetSorting_Curriculum.MaterialType.Plastic)
            {
                currentCorrectTargetBin = targetBinPlastic;
                // Update renderer if needed for visuals, though likely already set on the prefab
                if (bottleMeshRenderer && plasticMaterial) bottleMeshRenderer.material = plasticMaterial;
            }
            else
            {
                currentCorrectTargetBin = targetBinAluminum;
                if (bottleMeshRenderer && aluminumMaterial) bottleMeshRenderer.material = aluminumMaterial;
            }
        }
        else
        {
            Debug.LogError("[Agent] The Demo Bottle is missing the 'BottleTargetSorting_Curriculum' script!");
        }

        Debug.Log($"[Agent] Demo Bottle Updated. Target Bin set to: {(currentCorrectTargetBin != null ? currentCorrectTargetBin.name : "NULL")}");
    }


    public override void Initialize()
    {
        if (bottle) bottleScript = bottle.GetComponent<BottleTargetSorting_Curriculum>();
        
        if (endEffector != null)
        {
            if (endEffectorRb == null) endEffectorRb = endEffector.GetComponent<Rigidbody>();
            if (endEffectorRb == null)
            {
                endEffectorRb = endEffector.gameObject.AddComponent<Rigidbody>();
            }
            endEffectorRb.isKinematic = true; 
        }

        if (bottle) initialBottleRot = bottle.rotation;
        if (bottleRb) bottleRb.sleepThreshold = 0.0f;
    }

    // --- Helper for Horizontal Distance ---
    private float GetHorizontalDistance(Vector3 p1, Vector3 p2)
    {
        return Vector2.Distance(new Vector2(p1.x, p1.z), new Vector2(p2.x, p2.z));
    }

    public override void OnEpisodeBegin()
    {
        if (isDemoMode)
        {
            lesson_number = 3.0f; 
            return;
        }
        // Check standard curriculum unless Manual Mode is on
        lesson_number = Academy.Instance.EnvironmentParameters.GetWithDefault("lesson_number", 0f);

        Release(); 
        ResetJointRotations(); 
        ApplyRotationsToTransforms(); 
        if (bottleScript) bottleScript.ResetState();

        // --- Priority check for Manual Debug Mode ---
        if (manualDebugMode)
        {
            Debug.Log("<color=cyan><b>[Agent]</b> Configuring for Manual Debug: Full Task, Infinite Steps.</color>");
            SetupLesson_FullTask();
            MaxStep = 20000; // Extremely high max step for manual testing
        }
        else
        {
            // Standard Training Logic
            switch (lesson_number)
            {
                case 0f: SetupLesson_Reach(); MaxStep = 500; break;
                case 1f: SetupLesson_Grab(); MaxStep = 1000; break;
                case 2f: SetupLesson_Place(); MaxStep = 2000; break;
                default: SetupLesson_FullTask(); MaxStep = 5000; break;
            }
        }

        // Initialize Delta Tracking
        previousDistanceToBottle = Vector3.Distance(endEffector.position, bottle.position);
        if (currentCorrectTargetBin != null)
        {
            previousDistanceToBin = GetHorizontalDistance(bottle.position, currentCorrectTargetBin.position);
        }
    }

    // --- Lesson Logic ---
    private void SetupLesson_Reach()
    {
        ResetBottlePhysics(GetRandomSpawnPos(randomizationArea.bounds, 0.1f), true);
        bottleMeshRenderer.material = plasticMaterial;
        currentCorrectTargetBin = targetBinPlastic; 
        targetBinAluminum.gameObject.SetActive(false);
        targetBinPlastic.gameObject.SetActive(false);
    }

    private void SetupLesson_Grab()
    {
        Bounds smallerBounds = new Bounds(bottleSpawnPoint.position, randomizationArea.bounds.size * 0.5f);
        ResetBottlePhysics(GetRandomSpawnPos(smallerBounds, 0.1f), false);
        bottleMeshRenderer.material = plasticMaterial;
        currentCorrectTargetBin = targetBinPlastic;
        targetBinAluminum.gameObject.SetActive(false);
        targetBinPlastic.gameObject.SetActive(false);
    }

    private void SetupLesson_Place()
    {
        // Place bottle at end effector and set Kinematic=true
        ResetBottlePhysics(endEffector.position, true); 
        RandomizeBottleMaterialAndTarget(); 
        
        // Use ForceGrab to attach bottle immediately for the placement lesson
        ForceGrab(bottleRb); 
    }

    private void SetupLesson_FullTask()
    {
        ResetBottlePhysics(bottleSpawnPoint.position, false);
        RandomizeBottleMaterialAndTarget();
    }

    private void ResetBottlePhysics(Vector3 position, bool isKinematic)
    {
        bottle.position = position;
        bottle.rotation = initialBottleRot;

        bottleRb.isKinematic = isKinematic;

        if (!isKinematic)
        {
            bottleRb.linearVelocity = Vector3.zero;
            bottleRb.angularVelocity = Vector3.zero;
        }
        
        // Ensure bins are active for Full Task / Manual Mode
        targetBinAluminum.gameObject.SetActive(true);
        targetBinPlastic.gameObject.SetActive(true);
    }
    
    private Vector3 GetRandomSpawnPos(Bounds bounds, float yOffset)
    {
        return new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            bounds.center.y + yOffset,
            Random.Range(bounds.min.z, bounds.max.z)
        );
    }

    private void RandomizeBottleMaterialAndTarget()
    {
        var currentMaterial = (BottleTargetSorting_Curriculum.MaterialType)Random.Range(0, 2);
        bottleScript.material = currentMaterial;
        
        if (currentMaterial == BottleTargetSorting_Curriculum.MaterialType.Plastic)
        {
            currentCorrectTargetBin = targetBinPlastic;
            if (bottleMeshRenderer && plasticMaterial) bottleMeshRenderer.material = plasticMaterial;
        }
        else
        {
            currentCorrectTargetBin = targetBinAluminum;
            if (bottleMeshRenderer && aluminumMaterial) bottleMeshRenderer.material = aluminumMaterial;
        }
        
        if(manualDebugMode)
        {
            Debug.Log($"<color=orange>[Debug] Spawned Material: {bottleScript.material}</color>");
        }
    }

    private void ResetJointRotations()
    {
        currentBaseYRotation = Random.Range(baseRotationLimits.x, baseRotationLimits.y);
        currentFirstSegmentYRotation = Random.Range(firstSegmentRotationLimits.x, firstSegmentRotationLimits.y);
        currentSmallSegmentYRotation = Random.Range(smallSegmentRotationLimits.x, smallSegmentRotationLimits.y);
        currentSmallSegmentDrillYRotation = Random.Range(drillRotationLimits.x, drillRotationLimits.y);
        claw1XRotation = -90.0f;
        claw2XRotation = 90.0f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 5 Joint Angles
        sensor.AddObservation(Mathf.InverseLerp(baseRotationLimits.x, baseRotationLimits.y, currentBaseYRotation));
        sensor.AddObservation(Mathf.InverseLerp(firstSegmentRotationLimits.x, firstSegmentRotationLimits.y, currentFirstSegmentYRotation));
        sensor.AddObservation(Mathf.InverseLerp(smallSegmentRotationLimits.x, smallSegmentRotationLimits.y, currentSmallSegmentYRotation));
        sensor.AddObservation(Mathf.InverseLerp(drillRotationLimits.x, drillRotationLimits.y, currentSmallSegmentDrillYRotation));
        sensor.AddObservation(Mathf.InverseLerp(-90f, -28f, claw1XRotation)); 

        // 1 Material => Camera
        sensor.AddObservation((int)bottleScript.material);

        // 12 World State (Positions)
        sensor.AddObservation(transform.InverseTransformPoint(endEffector.position));
        sensor.AddObservation(transform.InverseTransformPoint(bottle.position));
        sensor.AddObservation(transform.InverseTransformPoint(targetBinAluminum.position));
        sensor.AddObservation(transform.InverseTransformPoint(targetBinPlastic.position));
        
        // 3 Relative Vector
        sensor.AddObservation(bottle.position - endEffector.position); 

        // 3 Bottle Orientation => Camera
        sensor.AddObservation(bottle.up);

        // 1 Holding State
        sensor.AddObservation(IsHoldingObject());

        // 1 Lesson
        sensor.AddObservation(lesson_number);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (_isExternallyControlled) return;
        if (isDemoMode && !isBrainActive) return;
        float rotationSpeed = 100f; 
        
        currentBaseYRotation += actions.ContinuousActions[0] * Time.fixedDeltaTime * rotationSpeed;
        currentFirstSegmentYRotation += actions.ContinuousActions[1] * Time.fixedDeltaTime * rotationSpeed;
        currentSmallSegmentYRotation += actions.ContinuousActions[2] * Time.fixedDeltaTime * rotationSpeed;
        currentSmallSegmentDrillYRotation += actions.ContinuousActions[3] * Time.fixedDeltaTime * rotationSpeed;
        
        float clawInput = actions.ContinuousActions[4];
        bool wasHolding = IsHoldingObject();

        if (clawInput > 0.5f) // Close
        {
            claw1XRotation = -28.0f;
            claw2XRotation = 28.0f;
            
            if (!wasHolding)
            {
                // In manual mode, we allow grab regardless of lesson number
                if (manualDebugMode || lesson_number != 2f)
                {
                    if (Grab()) 
                    {
                        AddReward(1.0f); 
                        if (!manualDebugMode && lesson_number == 1f) EndEpisode(); 
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
                
                if (isDemoMode)
                {
                    isBrainActive = false; // Shut off immediately
                    OnJobFinished?.Invoke();
                }
               
                // If we are in the placement phase (Lesson 2+) and we release the bottle
                // while NOT in a bin zone, we fail immediately.
                // This prevents wasting time watching the bottle fall to the conveyor.
                if (!manualDebugMode && lesson_number >= 2f)
                {
                    bool isInBinZone = bottleScript.isOverAluminumBin || bottleScript.isOverPlasticBin;
                    
                    if (!isInBinZone)
                    {
                        AddReward(-1.0f); // Penalty for bad release
                        EndEpisode();
                        return; // Stop execution for this step
                    }
                }
            }
        }

        // --- REWARDS ---
        
        float currentDistanceToBottle = Vector3.Distance(endEffector.position, bottle.position);
        float currentDistanceToBin = GetHorizontalDistance(bottle.position, currentCorrectTargetBin.position);

        if (!manualDebugMode && lesson_number == 0f) 
        {
            float delta = previousDistanceToBottle - currentDistanceToBottle;
            AddReward(delta); 

            if (currentDistanceToBottle < 0.05f) { AddReward(1.0f); EndEpisode(); }
        }
        else 
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
                
                if (currentDistanceToBin < 0.3f)
                {
                     AddReward(-0.005f); 
                }
            }
        }

        previousDistanceToBottle = currentDistanceToBottle;
        previousDistanceToBin = currentDistanceToBin;

        AddReward(-0.0001f);

        // --- SUCCESS CHECK (Instant - No Timer) ---
        if (manualDebugMode || lesson_number >= 2f)
        {
            // Only check placement if we are NOT holding the object (it has been released)
            if (!IsHoldingObject())
            {
                bool isInBinZone = bottleScript.isOverAluminumBin || bottleScript.isOverPlasticBin;

                if (isInBinZone)
                {
                    bool isCorrectPlacement = false;
                    bool isWrongPlacement = false;

                    if (bottleScript.material == BottleTargetSorting_Curriculum.MaterialType.Plastic)
                    {
                        if (bottleScript.isOverPlasticBin) isCorrectPlacement = true;
                        else if (bottleScript.isOverAluminumBin) isWrongPlacement = true;
                    }
                    else // Aluminum
                    {
                        if (bottleScript.isOverAluminumBin) isCorrectPlacement = true;
                        else if (bottleScript.isOverPlasticBin) isWrongPlacement = true;
                    }

                    if (isCorrectPlacement)
                    {
                        Debug.Log($"<color=green>WIN @ Step {StepCount}</color>\n" +
                                  $"Material: {bottleScript.material}\n" +
                                  $"Correct Bin!");

                        AddReward(5.0f); 
                        EndEpisode();
                        return;
                    }
                    else if (isWrongPlacement)
                    {
                        Debug.Log($"<color=red>FAIL @ Step {StepCount}</color>\n" +
                                  $"Material: {bottleScript.material}\n" +
                                  $"Wrong Bin!");

                        AddReward(-2.0f);
                        EndEpisode();
                        return;
                    }
                }
            }
        }
        
        
        // (This runs as a backup if the Early Termination above didn't trigger
        // or for lessons < 2)
        bool isInBinZone2 = bottleScript.isOverAluminumBin || bottleScript.isOverPlasticBin;

        if (!IsHoldingObject() && (bottle.position.y < (bottleSpawnPoint.position.y - 0.5f)))
        {
            if (!isInBinZone2)
            {
                if (lesson_number >= 1f) AddReward(-1.0f);
                
                if (!manualDebugMode) EndEpisode();
                else 
                {
                   Debug.Log("<color=red>[Debug] Bottle Fell - Resetting in Manual Mode</color>");
                   EndEpisode();
                }
            }
        }
    }

    public void OnPartCollision(string hitTag)
    {
        if (hitTag == "Conveyor" || hitTag == "RobotPart" || hitTag == "Ground")
        {
            AddReward(-1.0f); 
            if(!manualDebugMode) EndEpisode();
        }
    }

    private void ApplyRotationsToTransforms()
    {
        if (armbase) armbase.localRotation = Quaternion.Euler(0f, currentBaseYRotation, 0f);
        if (firstSegment) firstSegment.localRotation = Quaternion.Euler(0f, currentFirstSegmentYRotation, 0f);
        if (smallSegment) smallSegment.localRotation = Quaternion.Euler(-180f, currentSmallSegmentYRotation, 0f);
        if (smallSegmentDrill) smallSegmentDrill.localRotation = Quaternion.Euler(0f, currentSmallSegmentDrillYRotation, 0f);
        
        ApplyClawRotations();
    }

    private void ApplyClawRotations()
    {
        float lerpSpeed = 15f;
        if (claw1) claw1.localRotation = Quaternion.Lerp(claw1.localRotation, Quaternion.Euler(claw1XRotation, 0f, 0f), Time.fixedDeltaTime * lerpSpeed);
        if (claw2) claw2.localRotation = Quaternion.Lerp(claw2.localRotation, Quaternion.Euler(claw2XRotation, 0f, 0f), Time.fixedDeltaTime * lerpSpeed);
    }

    void FixedUpdate()
    {
        // Always animate claws
        ApplyClawRotations();

        if (!_isExternallyControlled)
        {
             if (armbase) armbase.localRotation = Quaternion.Euler(0f, currentBaseYRotation, 0f);
             if (firstSegment) firstSegment.localRotation = Quaternion.Euler(0f, currentFirstSegmentYRotation, 0f);
             if (smallSegment) smallSegment.localRotation = Quaternion.Euler(-180f, currentSmallSegmentYRotation, 0f);
             if (smallSegmentDrill) smallSegmentDrill.localRotation = Quaternion.Euler(0f, currentSmallSegmentDrillYRotation, 0f);
        }
    }
    
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        continuousActions.Clear();
        continuousActions[0] = Input.GetKey(KeyCode.D) ? 1f : (Input.GetKey(KeyCode.A) ? -1f : 0f);
        continuousActions[1] = Input.GetKey(KeyCode.W) ? -1f : (Input.GetKey(KeyCode.S) ? 1f : 0f);
        continuousActions[2] = Input.GetKey(KeyCode.UpArrow) ? -1f : (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        continuousActions[3] = Input.GetKey(KeyCode.RightArrow) ? 1f : (Input.GetKey(KeyCode.LeftArrow) ? -1f : 0f);
        continuousActions[4] = Input.GetKey(KeyCode.Space) ? 1.0f : -1.0f;
    }

    public bool Grab()
    {
        if (heldObjectRb != null || endEffectorRb == null) return false; 

        Collider[] colliders = Physics.OverlapSphere(endEffector.position, grabRadius);
        if (colliders.Length == 0) 
        {
            if(manualDebugMode) Debug.Log("[Debug] Grab Failed: No objects in range.");
            return false;
        }

        foreach (var col in colliders)
        {
            if (col.transform != bottle) continue; 

            Rigidbody rb = col.GetComponent<Rigidbody>();
            if (rb != null) 
            {
                // --- DEBUG LOGGING ---
                if (manualDebugMode)
                {
                    string binStatus = "None";
                    if (bottleScript.isOverAluminumBin) binStatus = "Aluminum Bin";
                    if (bottleScript.isOverPlasticBin) binStatus = "Plastic Bin";
                    Debug.Log($"<color=green>[Debug] GRAB SUCCESSFUL.</color> Bin Status at Grab: {binStatus}");
                }
                // ---------------------

                ForceGrab(rb);
                return true;
            }
        }
        return false;
    }

    public void ForceGrab(Rigidbody targetRb)
    {
        heldObjectRb = targetRb;
        heldObjectRb.isKinematic = true; 
        heldObjectRb.transform.SetParent(endEffector); 
        heldObjectRb.transform.localPosition = new Vector3(0, -0.08f, 0); 
        if (bottleScript != null) bottleScript.isHeld = true;
    }

    public void Release()
    {
        if (heldObjectRb == null) return; 

        if (manualDebugMode) Debug.Log($"[Debug] Released Object.");

        if (bottleScript != null) bottleScript.isHeld = false;
        heldObjectRb.transform.SetParent(bottleOriginalParent); 
        heldObjectRb.isKinematic = false; 
        heldObjectRb = null;
    }

    public bool IsHoldingObject()
    {
        return heldObjectRb != null;
    }
    
    public void OnBottleDropped()
    {
        if (lesson_number < 1f && !manualDebugMode) return; 
        if (!bottleScript.hasBeenPlacedCorrectly && !bottleScript.hasBeenPlacedIncorrectly)
        {
            AddReward(-1.0f);
            if(!manualDebugMode) EndEpisode();
            else Debug.Log("[Debug] Bottle Dropped on Ground.");
        }
    }

    // --- EXTERNAL CLAW CONTROL ---
    public void SetClawState(bool closed)
    {
        if (closed)
        {
            claw1XRotation = -28.0f;
            claw2XRotation = 28.0f;
        }
        else
        {
            claw1XRotation = -90.0f;
            claw2XRotation = 90.0f;
        }
    }

    // --- EXTERNAL CONTROL (For VR / IK Driving) ---
    private bool _isExternallyControlled = false;

    public void SetExternalControl(bool isActive)
    {
        _isExternallyControlled = isActive;
        if (isActive)
        {
            // Optional: You might want to stop the RB from sleeping or ensure it stays kinematic
        }
        else
        {
            // When returning control to Agent, we should sync internal state to current transform to avoid snapping
            SyncInternalStateFromTransforms();
        }
    }

    private void SyncInternalStateFromTransforms()
    {
        // Reverse of ApplyRotations. 
        // Note: This assumes simple 1-axis rotations as per existing logic.
        if(armbase) currentBaseYRotation = NormalizeAngle(armbase.localEulerAngles.y);
        if(firstSegment) currentFirstSegmentYRotation = NormalizeAngle(firstSegment.localEulerAngles.y);
        if(smallSegment) currentSmallSegmentYRotation = NormalizeAngle(smallSegment.localEulerAngles.y); // Note the -180 offset in Apply
        if(smallSegmentDrill) currentSmallSegmentDrillYRotation = NormalizeAngle(smallSegmentDrill.localEulerAngles.y);
        
        // Claws likely need similar logic if we want to sync them
        // For now, we leave claws as is or reset them.
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}