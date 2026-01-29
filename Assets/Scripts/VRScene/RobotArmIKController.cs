using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RobotArm_IK_Controller : MonoBehaviour
{
    [Header("Joint References")]
    [SerializeField] private Transform armbase;
    [SerializeField] private Transform firstSegment;
    [SerializeField] private Transform smallSegment;
    [SerializeField] private Transform smallSegmentDrill;
    [SerializeField] private Transform endEffector;

    [Header("Mounting")]
    [Tooltip("Check this if the robot base is mounted upside down (e.g. on a ceiling).")]
    public bool isUpsideDown = false; 

    [Header("IK Settings")]
    [SerializeField] private int ikIterations = 20; 
    [SerializeField] private float stopDistanceThreshold = 0.01f;
    [SerializeField] private float damping = 0.8f; 
    [Tooltip("If true, the path is shifted to start at the Robot's current End Effector position.")]
    [SerializeField] private bool useRelativeMotion = true;

    [Header("Visual Debug")]
    [SerializeField] private GameObject trackingBallPrefab;
    private GameObject _activePlaybackBall;

    [Header("Rotation Limits (Must match Agent)")]
    public Vector2 baseLimits = new Vector2(-180f, 180f);
    public Vector2 firstSegLimits = new Vector2(-180f, 180f);
    public Vector2 smallSegLimits = new Vector2(-180f, 180f);
    public Vector2 drillLimits = new Vector2(-180f, 180f);

    [Header("Debug Output")]
    public float out_BaseY;
    public float out_FirstY;
    public float out_SmallY;
    public float out_DrillY;

    private bool isExecuting = false;

    public void ProcessRecordedPath(List<Vector3> relativePath)
    {
        if (isExecuting) StopAllCoroutines();
        StartCoroutine(PlaybackRoutine(relativePath));
    }

    public void SetLiveTarget(Vector3 targetPos, float? targetDrillY = null)
    {
        // Direct IK solve for one frame
        SolveIK(targetPos, targetDrillY);
        UpdateFKValues();
    }

    private IEnumerator PlaybackRoutine(List<Vector3> path)
    {
        isExecuting = true;
        
        if (path.Count == 0) { isExecuting = false; yield break; }

        // Setup Visual Ball
        if (trackingBallPrefab != null)
        {
            _activePlaybackBall = Instantiate(trackingBallPrefab);
            if(_activePlaybackBall.GetComponent<Collider>()) Destroy(_activePlaybackBall.GetComponent<Collider>());
            if(_activePlaybackBall.GetComponent<Rigidbody>()) Destroy(_activePlaybackBall.GetComponent<Rigidbody>());
        }
        else
        {
            _activePlaybackBall = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _activePlaybackBall.transform.localScale = Vector3.one * 0.1f;
            Destroy(_activePlaybackBall.GetComponent<Collider>());
            var r = _activePlaybackBall.GetComponent<Renderer>();
            if(r) r.material.color = Color.green;
        }

        // Relative Motion Logic
        Vector3 pathOffset = Vector3.zero;
        if (useRelativeMotion)
        {
            // Because the Robot is rotated in World Space (Upside Down),
            // TransformPoint automatically handles the coordinate flip for us
            // as long as the input 'path' was recorded in a matching coordinate system 
            // (which CameraMove_Imitation now handles).
            Vector3 recordedStartPos = transform.TransformPoint(path[0]);
            Vector3 actualStartPos = endEffector.position;
            pathOffset = actualStartPos - recordedStartPos;
        }

        foreach (Vector3 relativePoint in path)
        {
            Vector3 worldTarget = transform.TransformPoint(relativePoint);
            worldTarget += pathOffset;

            // Move Debug Ball
            if (_activePlaybackBall != null) _activePlaybackBall.transform.position = worldTarget;

            SolveIK(worldTarget);
            UpdateFKValues();
            yield return new WaitForFixedUpdate(); 
        }

        if (_activePlaybackBall != null) Destroy(_activePlaybackBall);
        isExecuting = false;
    }

    private void UpdateFKValues()
    {
        out_BaseY = GetRobustJointYAngle(armbase, 0f);
        out_FirstY = GetRobustJointYAngle(firstSegment, 0f);
        out_SmallY = GetRobustJointYAngle(smallSegment, -180f); 
        out_DrillY = GetRobustJointYAngle(smallSegmentDrill, 0f);
    }

    private void SolveIK(Vector3 targetPos, float? targetDrillY = null)
    {
        for (int i = 0; i < ikIterations; i++)
        {
            if (Vector3.Distance(endEffector.position, targetPos) < stopDistanceThreshold) break;

            if (targetDrillY.HasValue)
            {
                 // Direct Control with Limits
                 float clampedY = Mathf.Clamp(NormalizeAngle(targetDrillY.Value), drillLimits.x, drillLimits.y);
                 smallSegmentDrill.localRotation = Quaternion.Euler(0f, clampedY, 0f); // Assuming 0 offset
            }
            else
            {
                SolveJointRotation(smallSegmentDrill, targetPos, drillLimits, 0f);
            }

            // RESTORED -180f offset here to fix the visual "floating" issue
            SolveJointRotation(smallSegment, targetPos, smallSegLimits, -180f);
            SolveJointRotation(firstSegment, targetPos, firstSegLimits, 0f);
            SolveJointRotation(armbase, targetPos, baseLimits, 0f);
        }
    }

    private void SolveJointRotation(Transform joint, Vector3 target, Vector2 limits, float xOffset)
    {
        Vector3 toEnd = endEffector.position - joint.position;
        Vector3 toTarget = target - joint.position;

        Vector3 axis = joint.up;

        // Project vectors onto the rotation plane
        Vector3 toEndProj = Vector3.ProjectOnPlane(toEnd, axis);
        Vector3 toTargetProj = Vector3.ProjectOnPlane(toTarget, axis);

        // --- SINGULARITY CHECK ---
        // If the end effector is too close to the axis (like arm pointing straight up/down),
        // the angle calculation becomes unstable.
        if (toEndProj.sqrMagnitude < 0.001f || toTargetProj.sqrMagnitude < 0.001f)
        {
            return; 
        }

        toEndProj.Normalize();
        toTargetProj.Normalize();

        float angle = Vector3.SignedAngle(toEndProj, toTargetProj, axis);

        // --- AXIS FLIP CORRECTION ---
        // If xOffset is ~180 (like -180f), the local Y axis is inverted relative to the solver's Up.
        // We must negate the calculated angle to rotate in the correct direction.
        if (Mathf.Abs(xOffset) > 90f)
        {
            angle = -angle;
        }

        // --- DAMPING ---
        float maxStep = 15f * damping;
        angle = Mathf.Clamp(angle, -maxStep, maxStep);

        // Apply Rotation
        float currentY = GetRobustJointYAngle(joint, xOffset);
        float newY = currentY + angle;
        newY = NormalizeAngle(newY);
        newY = Mathf.Clamp(newY, limits.x, limits.y);

        joint.localRotation = Quaternion.Euler(xOffset, newY, 0f);
    }

    private float GetRobustJointYAngle(Transform t, float xOffset)
    {
        Quaternion offsetRot = Quaternion.Euler(xOffset, 0, 0);
        // Using right-multiplication to correctly strip the offset
        Quaternion cleanRot = t.localRotation * Quaternion.Inverse(offsetRot);
        return NormalizeAngle(cleanRot.eulerAngles.y);
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }
}