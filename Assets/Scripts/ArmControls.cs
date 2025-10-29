using UnityEngine;
// We need this namespace to access the CameraMove script and its enum
using UnityFactorySceneHDRP;

public class ArmControls : MonoBehaviour
{

    [Header("Arm Components")]

    
    public Transform armbase; 

 
    public Transform firstSegment;

    public Transform smallSegment;
    public Transform smallSegmentDrill;
    
    [Tooltip("Reference to the first claw component.")]
    public Transform claw1;
    [Tooltip("Reference to the second claw component.")]
    public Transform claw2;

    [Tooltip("How fast the parts rotate.")]
    public float rotationSpeed = 45.0f;

    [Header("Rotation Limits")]
    [Tooltip("Symmetrical angle limit for the first segment (in degrees). Set to 45 to allow rotation between -45 and +45.")]
    public float firstSegmentAngleLimit = 45.0f;

    [Header("State Control")]
    [Tooltip("Reference to the CameraMove script on the player. Controls will only activate when the camera is locked.")]
    public CameraMove playerCameraMove; 
    
    // The current rotation values for each part.
    private float BaseYRotation = 0.0f;
    private float LargeSegmentRotation = 0.0f;
    private float SmallSegmentRotation = 0.0f;
    private float SmallSegmentClawRotation = 0.0f;
    
    // The current target rotation values for the claws.
    private float claw1XRotation = -90.0f; // Default to open state
    private float claw2XRotation = 90.0f;  // Default to open state
    
    void Start()
    {
        if (armbase != null)
        {
            BaseYRotation = armbase.localEulerAngles.y;
        }
        if (firstSegment != null)
        {
            // Per instructions, not changing this logic, even if it looks unusual.
            LargeSegmentRotation = firstSegment.localEulerAngles.y;
        }
        if (smallSegment != null)
        {
            SmallSegmentRotation = smallSegment.localEulerAngles.y;
        }
        if (smallSegmentDrill != null)
        {
            SmallSegmentClawRotation = smallSegmentDrill.localEulerAngles.y;
        }
        
        // Initialize claw rotations to their default (open) state.
        // We set the target here, and Update() will move them into position.
        if (claw1 != null)
        {
            claw1XRotation = -90.0f;
        }
        if (claw2 != null)
        {
            claw2XRotation = 90.0f;
        }
        if (playerCameraMove == null)
        {
            Debug.LogWarning("ArmControls: 'Player Camera Move' reference is not set. Arm controls will be disabled.", this);
        }
    }

    
    void Update()
    {
       
        if (playerCameraMove == null || playerCameraMove.CurrentState != CameraMove.CameraState.Locked)
        {
          
            return;
        }

       
        if (Input.GetKey(KeyCode.A))
        {
            BaseYRotation -= rotationSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.D))
        {
            BaseYRotation += rotationSpeed * Time.deltaTime;
        }

   
        if (Input.GetKey(KeyCode.W))
        {
            LargeSegmentRotation -= rotationSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.S))
        {
            LargeSegmentRotation += rotationSpeed * Time.deltaTime;
        }
        
        LargeSegmentRotation = Mathf.Clamp(LargeSegmentRotation, -firstSegmentAngleLimit, firstSegmentAngleLimit);

        if (Input.GetKey(KeyCode.UpArrow))
        {
            SmallSegmentRotation -= rotationSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.DownArrow))
        {
            SmallSegmentRotation += rotationSpeed * Time.deltaTime;
        }
  
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            SmallSegmentClawRotation -= rotationSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.RightArrow))
        {
            SmallSegmentClawRotation += rotationSpeed * Time.deltaTime;
        }
        
        // Check for Space (Grab) - but not if Shift is also held
        if (Input.GetKeyDown(KeyCode.Space) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
        {
            // Set target to "Grab" position
            claw1XRotation = -28.0f;
            claw2XRotation = 28.0f;
        }
        
        // Check for Shift + Space (Release)
        if (Input.GetKeyDown(KeyCode.Space) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
        {
            // Set target to "Open" position
            claw1XRotation = -90.0f;
            claw2XRotation = 90.0f;
        }



        // --- APPLY ROTATIONS ---
       
        if (armbase != null)
        {
            // Rotate the stem on its local Y-axis.
            armbase.localRotation = Quaternion.Euler(0f, BaseYRotation, 0f);
        }

        if (firstSegment != null)
        {
            firstSegment.localRotation = Quaternion.Euler(0f, LargeSegmentRotation, 0f);
        }
        if (smallSegment != null)
        {
           
            smallSegment.localRotation = Quaternion.Euler(-180f, SmallSegmentRotation, 0f);
        }

        if (smallSegmentDrill != null)
        {
            smallSegmentDrill.localRotation = Quaternion.Euler(0f, SmallSegmentClawRotation, 0f);
        }


        
        if (claw1 != null)
        {
            // Create the target rotation quaternion based on our target X angle
            Quaternion targetClaw1Rotation = Quaternion.Euler(claw1XRotation, 0f, 0f);
            // Smoothly move from the current rotation towards the target rotation
            claw1.localRotation = Quaternion.Lerp(claw1.localRotation, targetClaw1Rotation, rotationSpeed * Time.deltaTime);
        }

        if (claw2 != null)
        {
            // Create the target rotation quaternion based on our target X angle
            Quaternion targetClaw2Rotation = Quaternion.Euler(claw2XRotation, 0f, 0f);
            // Smoothly move from the current rotation towards the target rotation
            claw2.localRotation = Quaternion.Lerp(claw2.localRotation, targetClaw2Rotation, rotationSpeed * Time.deltaTime);
        }
    }
}
