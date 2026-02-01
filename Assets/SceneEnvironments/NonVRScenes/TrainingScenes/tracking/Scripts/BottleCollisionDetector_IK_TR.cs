using UnityEngine;

public class BottleCollisionDetector_IK_TR : MonoBehaviour
{
    private ArmAgent_IK agent;

    void Start()
    {
        // Find the IK agent in the scene
        agent = FindFirstObjectByType<ArmAgent_IK>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (agent == null) return;

        // Check for "Ground" tag specifically, or Layer 0 (Default) as a fallback for simple planes.
        // It's best if the Ground object is Tagged "Ground".
        if (collision.gameObject.CompareTag("Ground") || collision.gameObject.layer == 0)
        {
            // Ignore collisions with Conveyor or Bins if they are on Layer 0 but not tagged "Ground".
            // Typically Conveyors have their own tag. 
            // If the user said "Simple Plane", Layer 0 is likely.

            // Filter out the Conveyor if it happens to be Layer 0 (Safety check)
            if (collision.gameObject.name.Contains("Conveyor")) return;
            if (collision.gameObject.name.Contains("Bin")) return;
            if (collision.gameObject.name.Contains("Robot")) return;

            agent.OnBottleHitGround();
        }
    }
}
