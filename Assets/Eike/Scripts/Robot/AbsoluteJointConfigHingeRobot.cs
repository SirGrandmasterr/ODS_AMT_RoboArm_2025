using Unity.MLAgents.Actuators;
using UnityEngine;

public class AbsoluteJointConfigHingeRobot : HingeRobot
{
    // public override void OnActionReceived(ActionBuffers actions)
    // {
    //     float[] angles  = actions.ContinuousActions.Array;
    //
    //     // // Rotate in the direction
    //     // float angle0 = Joint0.localRotation.eulerAngles.y + angles[0] * Time.deltaTime * rotateSpeed;
    //     // float angle1 = Joint1.localRotation.eulerAngles.z + angles[1] * Time.deltaTime * rotateSpeed;
    //     // float angle2 = Joint2.localRotation.eulerAngles.z + angles[2] * Time.deltaTime * rotateSpeed;
    //     // float angle3 = Joint3.localRotation.eulerAngles.z + angles[3] * Time.deltaTime * rotateSpeed;
    //     //
    //     //
    //     // Joint0.localRotation = Quaternion.Euler(0, angle0, 0);
    //     // Joint1.localRotation = Quaternion.Euler(0, 0, angle1);
    //     // Joint2.localRotation = Quaternion.Euler(0, 0, angle2);
    //     // Joint3.localRotation = Quaternion.Euler(0, 0, angle3);
    //
    //     DistributeDenseReward();
    // }
}
