using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.VisualScripting;

public class Robot : Agent
{
    [SerializeField] Transform Joint0;
    [SerializeField] Transform Joint1;
    [SerializeField] Transform Joint2;
    [SerializeField] Transform Joint3;
    [SerializeField] Transform EndEffector;

    private float link1Length;
    private float link2Length;
    private float link3Length;

    [SerializeField] Transform Target;

    [SerializeField] Transform ControllerObject;

    [SerializeField] Ground Ground;

    private float minRange;
    private float maxRange;

    [SerializeField] bool trainingMode = true;

    private float rotateSpeed = 100f;

    float beginDistance;
    float prevBestDistance = float.MaxValue;

    void Start()
    {
        link1Length = Vector3.Distance(Joint1.position, Joint2.position);
        link2Length = Vector3.Distance(Joint2.position, Joint3.position);
        link3Length = Vector3.Distance(Joint3.position, EndEffector.position);

        maxRange = link1Length + link2Length - link3Length;
        minRange = 0.8f;
    }

    // Update is called once per frame
    void Update()
    {
    }

    public override void Initialize()
    {
        ResetAllAxis();
        MoveToSafeRandomPosition();
        if (!trainingMode) MaxStep = 0;
    }

    public override void OnEpisodeBegin()
    {
        if (!trainingMode)
            ResetAllAxis();

        MoveToSafeRandomPosition();
        SetTargetToSafeRandomPosition();

        beginDistance = Vector3.Distance(EndEffector.position, Target.position);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        float[] joint_angles =
        {
            Joint0.localRotation.eulerAngles.y / 180f,
            Joint1.localRotation.eulerAngles.z / 90f,
            Joint2.localRotation.eulerAngles.z / 180f,
            Joint3.localRotation.eulerAngles.z / 180f
        };

        Vector3 localEndEffectorPos = transform.InverseTransformPoint(EndEffector.position);

        sensor.AddObservation(joint_angles); // Joint Angles [4]
        sensor.AddObservation(Target.localPosition.normalized); // Target Position [3]
        sensor.AddObservation(transform.localPosition.normalized); // Robot Position [3]
        sensor.AddObservation(localEndEffectorPos.normalized); // Local End Effector Position [3]
        sensor.AddObservation((Target.localPosition - localEndEffectorPos).normalized); // Vector to Target [3]
        sensor.AddObservation(Vector3.Distance(localEndEffectorPos, Target.localPosition) / (maxRange)); // Distance to Target [1]
        sensor.AddObservation(StepCount / (float)MaxStep); // Normalized Step Count [1]
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float[] angles = actions.ContinuousActions.Array;

        // float angle0 = angles[0] * 180f;
        // float angle1 = angles[1] * 90f;
        // float angle2 = angles[2] * 180f;
        // float angle3 = angles[3] * 180f;

        // // Rotate in the direction
        float angle0 = Joint0.localRotation.eulerAngles.y + angles[0] * Time.deltaTime * rotateSpeed;
        float angle1 = Joint1.localRotation.eulerAngles.z + angles[1] * Time.deltaTime * rotateSpeed;
        float angle2 = Joint2.localRotation.eulerAngles.z + angles[2] * Time.deltaTime * rotateSpeed;
        float angle3 = Joint3.localRotation.eulerAngles.z + angles[3] * Time.deltaTime * rotateSpeed;


        Joint0.localRotation = Quaternion.Euler(0, angle0, 0);
        Joint1.localRotation = Quaternion.Euler(0, 0, angle1);
        Joint2.localRotation = Quaternion.Euler(0, 0, angle2);
        Joint3.localRotation = Quaternion.Euler(0, 0, angle3);

        Vector3 localEndEffectorPos = transform.InverseTransformPoint(EndEffector.position);

        float distance_to_target = Vector3.Distance(localEndEffectorPos, Target.localPosition);
        
        AddReward(-0.01f); // Time penalty. Encourage fast success

        if (distance_to_target > prevBestDistance)
        {
            // Penalize for moving away from target
            AddReward(prevBestDistance - distance_to_target);
        }
        else
        {
            // Reward for moving closer to target
            AddReward(beginDistance - distance_to_target);
            prevBestDistance = distance_to_target;
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        InverseKinematicsActionsOut(ControllerObject.localPosition, actionsOut);
    }

    public void OnGroundHit()
    {
        AddReward(-100f);
        Ground.ShowFailure();
        EndEpisode();
    }

    public void OnTargetReached()
    {
        float bonus = 20f * (int)Mathf.Clamp01(Vector3.Dot(Target.up.normalized, EndEffector.up.normalized)); // Bonus for alignment
        AddReward(100f + bonus);
        Ground.ShowSuccess();
        EndEpisode();
    }

    private void SetTargetToSafeRandomPosition()
    {
        Vector3 newRandomPos = RandomPosInRange(minRange + 1e-3f, maxRange - 1e-3f);
        newRandomPos = new Vector3(newRandomPos.x, Mathf.Max(newRandomPos.y, 0.3f), newRandomPos.z);
        Target.localPosition = newRandomPos;
    }

    private void MoveToSafeRandomPosition()
    {
        Vector3 newRandomPos = RandomPosInRange(minRange, maxRange);
        InverseKinematics3D(newRandomPos);
    }

    private Vector3 RandomSphericalPosition(float minR, float maxR)
    {
        float r = Random.Range(minR, maxR);
        float polar = Random.Range(Mathf.PI / 4, Mathf.PI / 2); // 0 - 90 degrees
        float azimuth = Random.Range(0, 2 * Mathf.PI);

        return new Vector3(r, polar, azimuth);
    }

    private Vector3 SphericalToCartesian(Vector3 sphericalPos)
    {
        float x = sphericalPos.x * Mathf.Sin(sphericalPos.y) * Mathf.Cos(sphericalPos.z);
        float y = sphericalPos.x * Mathf.Cos(sphericalPos.y);
        float z = sphericalPos.x * Mathf.Sin(sphericalPos.y) * Mathf.Sin(sphericalPos.z);

        return new Vector3(x, y, z);
    }

    private Vector3 RandomPosInRange(float minR, float maxR)
    {
        Vector3 sphericalPos = RandomSphericalPosition(minR, maxR);
        return SphericalToCartesian(sphericalPos);
    }

    private void ResetAllAxis()
    {
        Joint0.localRotation = Quaternion.Euler(0, 0, 0);
        Joint1.localRotation = Quaternion.Euler(0, 0, 0);
        Joint2.localRotation = Quaternion.Euler(0, 0, 0);
        Joint3.localRotation = Quaternion.Euler(0, 0, 0);
    }

    private void InverseKinematicsActionsOut(Vector3 localTargetPos, in ActionBuffers actionsOut)
    {
        Vector3 globalTargetPos = transform.TransformPoint(localTargetPos);

        Vector3 dirFromBase = globalTargetPos - transform.position;

        float theta0 = -Mathf.Atan2(dirFromBase.z, dirFromBase.x);

        // Calculate joint positions in local Join0 space

        Quaternion invBaseRot = Quaternion.Inverse(Quaternion.Euler(0, theta0 * Mathf.Rad2Deg, 0));
        Vector3 joint1Local = invBaseRot * transform.InverseTransformPoint(Joint1.position);
        Vector3 joint3Local = invBaseRot * transform.InverseTransformPoint(Joint2.position);
        Vector3 targetLocal = invBaseRot * localTargetPos;

        float x = targetLocal.x - joint1Local.x;
        float y = targetLocal.y - joint1Local.y;

        float maxLength = link1Length + link2Length + link3Length - 1e-4f;
        Vector2 targetVector = new Vector2(x, y);

        float L1 = link1Length;
        float L2 = link2Length;
        float L3 = link3Length;

        // float phi_e = Target.eulerAngles.z * Mathf.Deg2Rad;
        // float phi_e = Mathf.Atan2(y, x);
        float phi_e = -Mathf.PI / 2f;
        // float phi_e = Math.Clamp(Mathf.Atan2(joint3Local.y, joint3Local.x), -Mathf.PI / 2f, -Mathf.PI / 4f);

        float xw = x - L3 * Mathf.Cos(phi_e);
        float yw = y - L3 * Mathf.Sin(phi_e);

        // Theta 2
        float D2 = (L1 * L1 + L2 * L2 - (xw * xw) - (yw * yw)) / (2 * L1 * L2);
        D2 = Mathf.Clamp(D2, -1f, 1f);
        float theta2 = Mathf.PI + Mathf.Acos(D2);
        // float theta2 = Mathf.PI + Mathf.Acos((L1 * L1 + L2 * L2 - (xw * xw) - (yw * yw)) / (2 * L1 * L2));

        // Theta 1
        float D1 = (xw * xw + yw * yw + L1 * L1 - L2 * L2) / (2 * L1 * Mathf.Sqrt(xw * xw + yw * yw));
        D1 = Mathf.Clamp(D1, -1f, 1f);
        float theta1 = Mathf.Atan2(yw, xw) + Mathf.Acos(D1); ;
        // float theta1 = Mathf.Atan2(yw, xw) + Mathf.Acos((xw * xw + yw * yw + L1 * L1 - L2 * L2) / (2 * L1 * Mathf.Sqrt(xw * xw + yw * yw)));

        // Theta 2
        float theta3 = phi_e - theta1 - theta2;


        var continuousActions = actionsOut.ContinuousActions;
        
        float error0 = Mathf.DeltaAngle(Joint0.localEulerAngles.y, theta0 * Mathf.Rad2Deg);
        float error1 = Mathf.DeltaAngle(Joint1.localEulerAngles.z, theta1 * Mathf.Rad2Deg);
        float error2 = Mathf.DeltaAngle(Joint2.localEulerAngles.z, theta2 * Mathf.Rad2Deg);
        float error3 = Mathf.DeltaAngle(Joint3.localEulerAngles.z, theta3 * Mathf.Rad2Deg);

        float P_GAIN = 0.05f;
        
        continuousActions[0] = error0 * P_GAIN;//Mathf.Clamp(error0 * P_GAIN, -1f, 1f);
        continuousActions[1] = error1 * P_GAIN;//Mathf.Clamp(error1 * P_GAIN,-1f, 1f);
        continuousActions[2] = error2 * P_GAIN;//Mathf.Clamp(error2 * P_GAIN,-1f, 1f);
        continuousActions[3] = error3 * P_GAIN;//Mathf.Clamp(error3 * P_GAIN,-1f, 1f);
    }

    private void InverseKinematics3D(Vector3 localTargetPos)
    {
        Vector3 globalTargetPos = transform.TransformPoint(localTargetPos);

        Vector3 dirFromBase = globalTargetPos - transform.position;

        float theta0 = -Mathf.Atan2(dirFromBase.z, dirFromBase.x);

        // Calculate joint positions in local Join0 space

        Quaternion invBaseRot = Quaternion.Inverse(Quaternion.Euler(0, theta0 * Mathf.Rad2Deg, 0));
        // Vector3 joint1Local = invBaseRot * (Joint1.position - transform.position);
        // Vector3 joint3Local = invBaseRot * (Joint3.position - transform.position);
        // Vector3 targetLocal = invBaseRot * (globalTargetPos - transform.position);
        Vector3 joint1Local = invBaseRot * transform.InverseTransformPoint(Joint1.position);
        Vector3 joint3Local = invBaseRot * transform.InverseTransformPoint(Joint2.position);
        Vector3 targetLocal = invBaseRot * localTargetPos;

        float x = targetLocal.x - joint1Local.x;
        float y = targetLocal.y - joint1Local.y;

        float maxLength = link1Length + link2Length + link3Length - 1e-4f;
        Vector2 targetVector = new Vector2(x, y);

        // if (targetVector.magnitude > maxLength)
        // {

        //     Vector2 clampedTargetVector = targetVector.normalized * maxLength;
        //     x = clampedTargetVector.x;
        //     y = clampedTargetVector.y;
        // }

        float L1 = link1Length;
        float L2 = link2Length;
        float L3 = link3Length;

        // float phi_e = Target.eulerAngles.z * Mathf.Deg2Rad;
        // float phi_e = Mathf.Atan2(y, x);
        float phi_e = -Mathf.PI / 2f;
        // float phi_e = Math.Clamp(Mathf.Atan2(joint3Local.y, joint3Local.x), -Mathf.PI / 2f, -Mathf.PI / 4f);

        float xw = x - L3 * Mathf.Cos(phi_e);
        float yw = y - L3 * Mathf.Sin(phi_e);

        // Theta 2
        float D2 = (L1 * L1 + L2 * L2 - (xw * xw) - (yw * yw)) / (2 * L1 * L2);
        D2 = Mathf.Clamp(D2, -1f, 1f);
        float theta2 = Mathf.PI + Mathf.Acos(D2);
        // float theta2 = Mathf.PI + Mathf.Acos((L1 * L1 + L2 * L2 - (xw * xw) - (yw * yw)) / (2 * L1 * L2));

        // Theta 1
        float D1 = (xw * xw + yw * yw + L1 * L1 - L2 * L2) / (2 * L1 * Mathf.Sqrt(xw * xw + yw * yw));
        D1 = Mathf.Clamp(D1, -1f, 1f);
        float theta1 = Mathf.Atan2(yw, xw) + Mathf.Acos(D1); ;
        // float theta1 = Mathf.Atan2(yw, xw) + Mathf.Acos((xw * xw + yw * yw + L1 * L1 - L2 * L2) / (2 * L1 * Mathf.Sqrt(xw * xw + yw * yw)));

        // Theta 2
        float theta3 = phi_e - theta1 - theta2;

        Joint0.localRotation = Quaternion.Euler(0, theta0 * Mathf.Rad2Deg, 0);
        Joint1.localRotation = Quaternion.Euler(0, 0, theta1 * Mathf.Rad2Deg);
        Joint2.localRotation = Quaternion.Euler(0, 0, theta2 * Mathf.Rad2Deg);
        Joint3.localRotation = Quaternion.Euler(0, 0, theta3 * Mathf.Rad2Deg);
    }



}
