/*
 * DEBUG BOTTLE SCRIPT (v5.1 - Collision Validation & Logging)
 * BottleTargetSorting_Curriculum.cs
 */

using UnityEngine;

public class BottleTargetSorting_Curriculum : MonoBehaviour
{
    public enum MaterialType { Plastic, Aluminum }

    [Header("Bottle State")]
    public MaterialType material;
    public bool isHeld = false;

    [HideInInspector] public bool hasBeenPlacedCorrectly = false;
    [HideInInspector] public bool hasBeenPlacedIncorrectly = false;
    [HideInInspector] public bool isOverAluminumBin = false;
    [HideInInspector] public bool isOverPlasticBin = false;

    private ArmAgentSorting_Curriculum agent;
    private Rigidbody rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (agent == null) agent = GetComponentInParent<ArmAgentSorting_Curriculum>();
    }

    public void ResetState()
    {
        isHeld = false;
        hasBeenPlacedCorrectly = false;
        hasBeenPlacedIncorrectly = false;
        isOverAluminumBin = false;
        isOverPlasticBin = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isHeld) return;

        bool isInBinZone = isOverAluminumBin || isOverPlasticBin;

        // Only fail if we hit ground and are NOT in a bin zone
        if (collision.gameObject.layer == 0 && agent != null && !isInBinZone)
        {
            agent.OnBottleDropped();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("TargetBinAluminum"))
        {
            isOverAluminumBin = true;
            Debug.Log("<color=magenta>[Bottle] Entered ALUMINUM Bin Zone</color>");
        }
        if (other.CompareTag("TargetBinPlastic"))
        {
            isOverPlasticBin = true;
            Debug.Log("<color=magenta>[Bottle] Entered PLASTIC Bin Zone</color>");
        }
    }

    // Retain OnTriggerStay for logic consistency if other scripts rely on it
    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("TargetBinAluminum")) isOverAluminumBin = true;
        if (other.CompareTag("TargetBinPlastic")) isOverPlasticBin = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("TargetBinAluminum"))
        {
            isOverAluminumBin = false;
            Debug.Log("<color=grey>[Bottle] Exited ALUMINUM Bin Zone</color>");
        }
        if (other.CompareTag("TargetBinPlastic"))
        {
            isOverPlasticBin = false;
            Debug.Log("<color=grey>[Bottle] Exited PLASTIC Bin Zone</color>");
        }
    }
}