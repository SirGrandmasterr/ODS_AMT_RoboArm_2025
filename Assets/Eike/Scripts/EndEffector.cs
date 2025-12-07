using Unity.MLAgents;
using UnityEngine;

public class EndEffector : MonoBehaviour
{
    [SerializeField] private Robot Robot;

    void OnTriggerEnter(Collider other)
    {
        if (other.tag.Equals("Target"))
        {
            Robot.OnTargetReached();
        }
        else if (other.tag.Equals("Ground"))
        {
            Robot.OnGroundHit();
        }
        else
        {
            Debug.Log("EndEffector hit unknown object: " + other.name);
        }
    }
}
