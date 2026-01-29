using UnityEngine;

public class CollisionDetector_Demo : MonoBehaviour
{
    [Tooltip("Drag the ArmAgent_Demo script here.")]
    public ArmAgent_Demo agent;

    private void OnCollisionEnter(Collision collision)
    {
        if (agent != null)
        {
            agent.OnPartCollision(collision.gameObject.tag);
        }
    }
}
