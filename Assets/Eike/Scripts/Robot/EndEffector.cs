using Unity.MLAgents;
using UnityEngine;

public class EndEffector : MonoBehaviour
{
    [SerializeField] private Robot robot;

    void OnTriggerEnter(Collider other)
    {
        if (other.tag.Equals("Target"))
        {
            robot.OnTargetReached();
        }
        else if (other.tag.Equals("Ground"))
        {
            robot.OnGroundHit();
        }
        else
        {
            Debug.Log("EndEffector hit unknown object: " + other.name);
        }
    }
}
