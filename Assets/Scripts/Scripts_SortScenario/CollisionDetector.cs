using UnityEngine;

/// <summary>
/// Attaches to individual robot parts (Claws, Segments) to detect collisions
/// and report them back to the main Agent.
/// </summary>
public class CollisionDetector : MonoBehaviour
{
    [Tooltip("Reference to the main Agent script.")]
    public ArmAgentSorting_Curriculum agent;

    private void OnCollisionEnter(Collision collision)
    {
        if (agent != null)
        {
            // Pass the tag of the object we hit to the agent
            agent.OnPartCollision(collision.gameObject.tag);
        }
    }
}