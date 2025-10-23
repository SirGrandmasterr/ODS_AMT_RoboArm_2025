using UnityEngine;

/// <summary>
/// This script controls a simple two-joint robotic arm using keyboard inputs.
/// Attach this script to a root GameObject of your robot arm.
/// </summary>
public class RobotArmController : MonoBehaviour
{
    // --- PUBLIC FIELDS ---
    // Assign these in the Unity Inspector.

    /// <summary>
    /// The transform for the first joint (the base).
    /// This will rotate the entire arm around the Y-axis.
    /// </summary>
    public Transform joint1; // Base of the arm

    /// <summary>
    /// The transform for the second joint.
    /// This will control the angle between the first and second arm segments.
    /// </summary>
    public Transform joint2; // The "elbow" of the arm

    public Transform joint3;
    public Transform joint4;
    public Transform claw1;
    public Transform claw2;

    /// <summary>
    /// The speed at which the joints will rotate.
    /// </summary>
    public float rotationSpeed = 30.0f; // Degrees per second

    // --- PRIVATE FIELDS ---

    // The current rotation values for each joint.
    private float joint1Rotation = 0.0f;
    private float joint2Rotation = 0.0f; // This will now control only the Z-axis of joint 2
    private float joint3Rotation = -27f;
    private float joint4Rotation = 0.0f;
    private float claw1Rotation = 0.0f;
    private float claw2Rotation = 0.0f;


    /// <summary>
    /// Called once by Unity before the first frame update.
    /// We initialize our rotation variables with the arm's starting rotation.
    /// </summary>
    void Start()
    {
        // Initialize the rotation variables with the initial rotation of the joints in the scene
        // This prevents the arm from "jumping" to a zero rotation at startup.
        if (joint1 != null)
        {
            joint1Rotation = joint1.localEulerAngles.y;
        }
        // For joint 2, we will use hardcoded values for the initial X and Y rotation
        // in the Update() method, so we don't need to initialize anything for it here.
        // joint2Rotation will start at 0 and control the Z-axis movement.
    }

    /// <summary>
    /// Called once per frame by Unity.
    /// We check for input and update the arm's rotation here.
    /// </summary>
    void Update()
    {
        // --- JOINT 1 CONTROL (Base Rotation) ---
        // 'A' key for counter-clockwise rotation
        if (Input.GetKey(KeyCode.A))
        {
            // Decrease the rotation angle for Joint 1
            joint1Rotation -= rotationSpeed * Time.deltaTime;
        }

        // 'D' key for clockwise rotation
        if (Input.GetKey(KeyCode.D))
        {
            // Increase the rotation angle for Joint 1
            joint1Rotation += rotationSpeed * Time.deltaTime;
        }


        // --- JOINT 2 CONTROL (Arm Angle) ---
        // 'W' key to raise the second part of the arm
        if (Input.GetKey(KeyCode.W))
        {
            // Decrease the rotation angle for Joint 2 (moving it "up")
            joint2Rotation -= rotationSpeed * Time.deltaTime;
        }

        // 'S' key to lower the second part of the arm
        if (Input.GetKey(KeyCode.S))
        {
            // Increase the rotation angle for Joint 2 (moving it "down")
            joint2Rotation += rotationSpeed * Time.deltaTime;
        }
        
       
       /* if (Input.GetKey(KeyCode.UpArrow))
        {
           
            joint3Rotation -= rotationSpeed * Time.deltaTime;
        }

       
        if (Input.GetKey(KeyCode.DownArrow))
        {
            
            joint3Rotation += rotationSpeed * Time.deltaTime;
        }*/

       
        if (joint1 != null)
        {
            joint1.localRotation = Quaternion.Euler(0f, joint1Rotation, 0f);
        }

        
        if (joint2 != null)
        {
            
            joint2.localRotation = Quaternion.Euler(90f, joint2Rotation, 0f );
        }

        /*if (joint3 != null)
        {
            joint3.localRotation = Quaternion.Euler(-180f, joint3Rotation, 0f);
        }*/
    }
}

