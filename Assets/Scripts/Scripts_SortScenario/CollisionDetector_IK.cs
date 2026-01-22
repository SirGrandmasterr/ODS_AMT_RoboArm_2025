using UnityEngine;

public class CollisionDetectorIK : MonoBehaviour
{
    [Tooltip("Reference to the main Agent script.")]
    public ArmAgent_IK agent;

    // CHANGED: Use OnTriggerEnter instead of OnCollisionEnter
    // This works better for Kinematic Rigidbodies (like your robot arm).
    private void OnTriggerEnter(Collider other)
    {
        if (agent != null)
        {
            // Pass the tag of the object we hit to the agent
            agent.OnPartCollision(other.gameObject.tag);
        }
    }
}