using System;
using Eike.Scripts;
using TMPro;
using UnityEngine;

public class JointController : MonoBehaviour
{
    public HingeJoint Joint { get; private set; }
    private Rigidbody _rb;
    public PIDController PID { get; private set; }

    [Header("PID Tuning")] 
    public float Kp = 1f;
    public float Ki = 0.1f;
    public float Kd = 20f;
    public float maxTorque = 10f;

    private float _targetAngle;
    
    void Awake()
    {
        Joint = GetComponent<HingeJoint>();
        _rb = GetComponent<Rigidbody>();
        
        PID = new PIDController(Kp, Ki, Kd, maxTorque);
    }

    public void MoveToAngle(float targetAngle)
    {
        float currentAngle = Joint.angle;
        
        float controlTorque = PID.CalculateOutput(
            targetAngle, currentAngle, Time.fixedDeltaTime);
        
        _rb.AddRelativeTorque(Joint.axis * controlTorque, ForceMode.Force);
    }

    public void SetAngle(float angle)
    {
        if (Joint == null) return;
        // Debug.Log($"{this.name} Set Angle Before: {angle}");
        
        transform.localRotation = Quaternion.AngleAxis(angle, Joint.axis.normalized);
        // Debug.Log($"{this.name} Get Angle Directly after:: {transform.localRotation}, {Joint.angle}, {Joint.axis.normalized}");
        
        // MoveToAngle(angle);
    }

    public float GetAngle()
    {
        if (!Joint) return 0;
        Vector3 eulerAngles = transform.localRotation.eulerAngles;
        eulerAngles.Scale(Joint.axis.normalized);

        return eulerAngles.magnitude;
        Debug.Log($"{this.name} Get Angle: {Joint?.angle ??-1000}, {transform.localRotation.eulerAngles}");
        if (float.IsNaN(Joint.angle))
        {
            Debug.Log("Hey");
        }
        return Joint.angle;
    }

    public void Reset()
    {
        PID?.Reset();
    }
    
    
}
