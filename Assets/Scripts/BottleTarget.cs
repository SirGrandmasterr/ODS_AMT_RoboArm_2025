using UnityEngine;

public class BottleTarget : MonoBehaviour
{
    public ArmAgent agent;
    public bool isHeld = false;
    public bool hasBeenPlaced = false;

    [HideInInspector]
    public bool isOverTarget = false;
    private void Start()
    {
        // Find the agent in the parent environment
        if (agent == null)
        {
            agent = GetComponentInParent<ArmAgent>();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isHeld) return;

        // Dropped on the ground (assuming ground has "Default" layer or a specific tag)
        if (collision.gameObject.layer == 0) // Default layer
        {
            if (agent != null)
            {
                agent.OnBottleDropped();
            }
        }
    }

    private void OnTriggerStay(Collider other)
    {
        // Check if we are on the target
        if (other.CompareTag("TargetLocation"))
        {
           
            isOverTarget = true;


            // Check placement *after* setting the flag
            if (!isHeld && !hasBeenPlaced)
            {
                // Check if we are relatively upright and stable
                if (Vector3.Dot(transform.up, Vector3.up) > 0.9f && GetComponent<Rigidbody>().linearVelocity.magnitude < 0.1f)
                {
                    hasBeenPlaced = true;
                }
            }
        }
    }
    
    /// <summary>
    /// This fires when the bottle leaves the target trigger area.
    /// </summary>
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("TargetLocation"))
        {
            isOverTarget = false;
        }
    }
}