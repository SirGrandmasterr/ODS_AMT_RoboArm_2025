using UnityEngine;

public class ConveyorBelt : MonoBehaviour
{
    [Header("Settings")]
    public float speed = 1.0f;
    public Vector3 direction = Vector3.forward;
    public Material conveyorMat;
    
    [Header("Interaction")]
    public Rigidbody targetObject; // Assign the Bottle here dynamically or in inspector
    public bool isMoving = false;

    private void Update()
    {
        if (!isMoving) return;

        // Visual Texture Scrolling
        if (conveyorMat != null)
        {
            float offset = Time.time * speed * 0.5f;
            conveyorMat.SetTextureOffset("_BaseColorMap", new Vector2(0, -offset)); // Adjust axis as needed
        }
    }

    private void FixedUpdate()
    {
        if (isMoving && targetObject != null)
        {
            // Move the bottle physically
            Vector3 pos = targetObject.position;
            targetObject.MovePosition(pos + (direction.normalized * speed * Time.fixedDeltaTime));
        }
    }
}