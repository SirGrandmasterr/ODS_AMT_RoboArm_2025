using UnityEngine;

public class Test : MonoBehaviour
{
    [SerializeField] protected HingeJoint HingeJoint0;
    [SerializeField] protected HingeJoint HingeJoint1;
    [SerializeField] protected HingeJoint HingeJoint2;
    [SerializeField] protected HingeJoint HingeJoint3;

    [SerializeField] protected Rigidbody Rigidbody0;
    [SerializeField] protected Rigidbody Rigidbody1;
    [SerializeField] protected Rigidbody Rigidbody2;
    [SerializeField] protected Rigidbody Rigidbody3;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        Rigidbody1.AddTorque(new Vector3(0, 0, 1));
    }

}
