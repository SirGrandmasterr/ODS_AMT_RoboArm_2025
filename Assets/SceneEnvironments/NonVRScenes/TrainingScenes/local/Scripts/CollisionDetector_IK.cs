using UnityEngine;

/// <summary>
/// Attaches to robot parts for the IK Agent.
/// </summary>
public class CollisionDetector_IK : MonoBehaviour
{
    public ArmAgent_IK agent;

    private void OnCollisionEnter(Collision collision)
    {
        if (agent != null)
        {
            agent.OnPartCollision(collision.gameObject.tag);
        }
    }
}
