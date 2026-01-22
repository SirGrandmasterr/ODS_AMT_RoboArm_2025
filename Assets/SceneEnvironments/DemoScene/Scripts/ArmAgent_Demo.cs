using System;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Random = UnityEngine.Random;

[RequireComponent(typeof(DecisionRequester))]
public class ArmAgent_Demo : Agent
{
    [Header("Environment Connection")]
    [Tooltip("Drag the SortingEnvironment_Demo object here.")]
    [SerializeField] private SortingEnvironment_Demo environment;

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
    
    public override void Initialize()
    {
        // 1. Check Environment
        if (environment == null)
        {
            environment = GetComponent<SortingEnvironment_Demo>();
            if (environment == null)
            {
                environment = FindFirstObjectByType<SortingEnvironment_Demo>();
            }

            if (environment == null)
            {
                Debug.LogError($"<color=red><b>[ArmAgent_Demo] FATAL ERROR:</b> 'Environment' field is not assigned in the Inspector!</color>", this);
            }
        }

        // 2. Check Joints
        if (armbase == null || firstSegment == null || smallSegment == null || smallSegmentDrill == null)
        {
             Debug.LogError($"<color=red><b>[ArmAgent_Demo] ERROR:</b> One or more Arm Joints (Transforms) are not assigned in the Inspector!</color>", this);
        }

        if (endEffector != null)
        {
            if (endEffectorRb == null) endEffectorRb = endEffector.GetComponent<Rigidbody>();
            if (endEffectorRb == null) endEffectorRb = endEffector.gameObject.AddComponent<Rigidbody>();
            endEffectorRb.isKinematic = true; 
        }
    }

    public override void OnEpisodeBegin()
    {
        if (environment == null) return;

        // 1. Reset Environment Logic
        environment.ResetEnvironment();

        // 2. Reset Robot Join Config
        ResetJointRotations();
        ApplyRotationsToTransforms();
        Release(); // Drop anything we might be holding
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

        // 1 Lesson - Hardcoded to 3.0 (Full Task) for Demo
        sensor.AddObservation(3.0f);
    }

    [Header("Demo Control")]
    public bool isOperating = true; 
    public event Action OnJobFinished;

    public void SetDemoBottle(GameObject bottleObj)
    {
        // For the isolated demo, the Environment usually holds the reference.
        // We can ensure the Environment knows about this bottle if it changed.
        if (environment != null && bottleObj != null)
        {
            environment.bottle = bottleObj.transform;
            environment.bottleRb = bottleObj.GetComponent<Rigidbody>();
            environment.bottleScript = bottleObj.GetComponent<DemoBottle>();
            // environment.bottleAudio = bottleObj.GetComponent<AudioSource>(); // Not defined in Demo Environment
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (!isOperating) return;

        // --- 1. MOVEMENT ---
        float rotationSpeed = 100f; 
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
                TryGrab();
            }
        }
        else // Open
        {
            claw1XRotation = -90.0f;
            claw2XRotation = 90.0f;
            
            if (wasHolding)
            {
                Release();
                // Demo Logic: Check Success immediately on release
                if (environment.CheckPlacementSuccess())
                {
                    Debug.Log("<color=green>[ArmAgent_Demo] SUCCESS: Bottle sorted correctly!</color>");
                    isOperating = false; // Stop acting
                    OnJobFinished?.Invoke();
                    // EndEpisode(); // REMOVED: Wait for Manager to reset
                }
                else if (environment.CheckPlacementFailure())
                {
                     // Only fail if we are actually in a bin zone (wrong one)
                    Debug.Log("<color=orange>[ArmAgent_Demo] FAIL: Bottle sorted incorrectly.</color>");
                    isOperating = false;
                    OnJobFinished?.Invoke();
                    // EndEpisode(); // REMOVED: Wait for Manager to reset
                }
            }
        }

         // Verify Bounds (FailSafe)
        if (!IsHoldingObject() && (environment.bottle.position.y < (environment.bottleSpawnPoint.position.y - 0.5f)))
        {
             // If it fell and wasn't in a bin
            if (!environment.IsInBinZone())
            {
                 Debug.Log($"<color=red>[ArmAgent_Demo] FAILED: Bottle fell out of bounds.</color>");
                 isOperating = false;
                 OnJobFinished?.Invoke(); // Fail is also a finished job for the demo loop
                 // EndEpisode(); // REMOVED: Wait for Manager to reset
            }
        }
    }

    public void ManualEndEpisode()
    {
        EndEpisode();
    }

    public void OnPartCollision(string hitTag)
    {
        // Optional: Reset on heavy collision?
        // For demo visual smoothness, maybe ignore or just log.
        if (hitTag == "Conveyor" || hitTag == "RobotPart" || hitTag == "Ground")
        {
            // Debug.Log($"[ArmAgent_Demo] Bumped {hitTag}");
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
        currentBaseYRotation = Random.Range(baseRotationLimits.x, baseRotationLimits.y);
        currentFirstSegmentYRotation = Random.Range(firstSegmentRotationLimits.x, firstSegmentRotationLimits.y);
        currentSmallSegmentYRotation = Random.Range(smallSegmentRotationLimits.x, smallSegmentRotationLimits.y);
        currentSmallSegmentDrillYRotation = Random.Range(drillRotationLimits.x, drillRotationLimits.y);
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
