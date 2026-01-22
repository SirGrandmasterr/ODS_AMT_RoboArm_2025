
using UnityEngine;

public class InitializationConfig : MonoBehaviour
{
    public enum StartPoseType
    {
        Fixed,         // Always the same start pose
        Random,        // Completely random (within limits)
        SlightRandom   // Home pose + small random noise
    }

    [Header("Initialization Settings")]
    public StartPoseType startPoseType = StartPoseType.SlightRandom;
    
    [Header("Slight Random Parameters")]
    [Tooltip("Degrees of variation for 'SlightRandom' mode.")]
    public float slightRandomRange = 15f; 

    [Header("Home Pose (Degrees)")]
    public float homeBase = 0f;
    public float homeFirst = 0f;
    public float homeSmall = -0f; // Often -180 relative internally, but let's assume 0 offset for now or strict angles
    public float homeDrill = 0f;

    public void GetStartRotations(
        Vector2 baseLimits, Vector2 firstLimits, Vector2 smallLimits, Vector2 drillLimits,
        out float baseRot, out float firstRot, out float smallRot, out float drillRot)
    {
        switch (startPoseType)
        {
            case StartPoseType.Random:
                baseRot = Random.Range(baseLimits.x, baseLimits.y);
                firstRot = Random.Range(firstLimits.x, firstLimits.y);
                smallRot = Random.Range(smallLimits.x, smallLimits.y);
                drillRot = Random.Range(drillLimits.x, drillLimits.y);
                break;

            case StartPoseType.SlightRandom:
                baseRot = ClampAngle(homeBase + Random.Range(-slightRandomRange, slightRandomRange), baseLimits);
                firstRot = ClampAngle(homeFirst + Random.Range(-slightRandomRange, slightRandomRange), firstLimits);
                smallRot = ClampAngle(homeSmall + Random.Range(-slightRandomRange, slightRandomRange), smallLimits);
                drillRot = ClampAngle(homeDrill + Random.Range(-slightRandomRange, slightRandomRange), drillLimits);
                break;

            case StartPoseType.Fixed:
            default:
                baseRot = ClampAngle(homeBase, baseLimits);
                firstRot = ClampAngle(homeFirst, firstLimits);
                smallRot = ClampAngle(homeSmall, smallLimits);
                drillRot = ClampAngle(homeDrill, drillLimits);
                break;
        }
    }

    private float ClampAngle(float angle, Vector2 limits)
    {
        return Mathf.Clamp(angle, limits.x, limits.y);
    }
}
