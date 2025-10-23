using UnityEngine;

/// <summary>
/// Controls a windmill-like object with two axes of rotation using keyboard inputs.
/// Attach this script to the root GameObject of the windmill.
/// </summary>
public class WindmillController : MonoBehaviour
{
    // --- PUBLIC FIELDS ---
    // Assign these in the Unity Inspector.

    /// <summary>
    /// The transform for the stem (or base).
    /// This will rotate the entire construct around a vertical (Y) axis.
    /// </summary>
    [Tooltip("The main vertical part of the windmill that turns left and right.")]
    public Transform armbase; 

    /// <summary>
    /// The transform for the wings.
    /// This should be a child of the 'stem' GameObject.
    /// It will rotate on a horizontal (X) axis to change the wing pitch.
    /// </summary>
    [Tooltip("The wings of the windmill that tilt up and down. MUST be a child of the Stem.")]
    public Transform firstSegment;

    public Transform smallSegment;
    public Transform smallSegmentDrill;

    /// <summary>
    /// The speed at which the parts will rotate in degrees per second.
    /// </summary>
    [Tooltip("How fast the parts rotate.")]
    public float rotationSpeed = 45.0f;

    // --- PRIVATE FIELDS ---

    // The current rotation values for each part.
    private float BaseYRotation = 0.0f;
    private float LargeSegmentRotation = 0.0f;
    private float SmallSegmentRotation = 0.0f;
    private float SmallSegmentClawRotation = 0.0f;


    /// <summary>
    /// Called once by Unity before the first frame update.
    /// We initialize our rotation variables with the object's starting rotation
    /// to prevent it from snapping to a zero rotation at startup.
    /// </summary>
    void Start()
    {
        if (armbase != null)
        {
            BaseYRotation = armbase.localEulerAngles.y;
        }
        if (firstSegment != null)
        {
            LargeSegmentRotation = firstSegment.localEulerAngles.x;
        }
        if (smallSegment != null)
        {
            SmallSegmentRotation = smallSegment.localEulerAngles.y;
        }
        if (smallSegmentDrill != null)
        {
            SmallSegmentClawRotation = smallSegmentDrill.localEulerAngles.y;
        }
        
    }

    /// <summary>
    /// Called once per frame by Unity.
    /// We check for input and update the windmill's rotation here.
    /// </summary>
    void Update()
    {
        // --- STEM CONTROL (Turns Left/Right) ---
        // 'A' and 'D' keys rotate the stem around the Y-axis.
        if (Input.GetKey(KeyCode.A))
        {
            BaseYRotation -= rotationSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.D))
        {
            BaseYRotation += rotationSpeed * Time.deltaTime;
        }

        // --- WINGS CONTROL (Tilts Up/Down) ---
        // 'W' and 'S' keys rotate the wings around the X-axis.
        if (Input.GetKey(KeyCode.W))
        {
            LargeSegmentRotation -= rotationSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.S))
        {
            LargeSegmentRotation += rotationSpeed * Time.deltaTime;
        }
        // ====
        if (Input.GetKey(KeyCode.UpArrow))
        {
            SmallSegmentRotation -= rotationSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.DownArrow))
        {
            SmallSegmentRotation += rotationSpeed * Time.deltaTime;
        }
        // ====
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            SmallSegmentClawRotation -= rotationSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.RightArrow))
        {
            SmallSegmentClawRotation += rotationSpeed * Time.deltaTime;
        }
        
        
        // --- APPLY ROTATIONS ---
        // Apply the calculated rotations back to the transforms.
        // We use localRotation so the movements are relative to the parent object.
        if (armbase != null)
        {
            // Rotate the stem on its local Y-axis.
            armbase.localRotation = Quaternion.Euler(0f, BaseYRotation, 0f);
        }

        if (firstSegment != null)
        {
            // Rotate the wings on their local X-axis. Because the wings are a child
            // of the stem, this rotation will be combined with the stem's rotation.
            firstSegment.localRotation = Quaternion.Euler(0f, LargeSegmentRotation, 0f);
        }
        if (smallSegment != null)
        {
            // Rotate the wings on their local X-axis. Because the wings are a child
            // of the stem, this rotation will be combined with the stem's rotation.
            smallSegment.localRotation = Quaternion.Euler(-180f, SmallSegmentRotation, 0f);
        }

        if (smallSegmentDrill != null)
        {
            smallSegmentDrill.localRotation = Quaternion.Euler(0f, SmallSegmentClawRotation, 0f);
        }
    }
}
