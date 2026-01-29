using UnityEngine;

/// <summary>
/// Attaches to robot parts for the new RL Agent.
/// </summary>
public class CollisionDetector_RL : MonoBehaviour
{
    public ArmAgent_RL agent;

    private void OnCollisionEnter(Collision collision)
    {
        if (agent != null)
        {
            agent.OnPartCollision(collision.gameObject.tag);
        }
    }
}
