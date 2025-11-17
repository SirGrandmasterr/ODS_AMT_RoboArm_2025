/*
 * MODIFIED BOTTLE SCRIPT
 * BottleTarget.cs (Now for Sorting)
 *
 * Major Changes:
 * 1. Added MaterialType enum and 'material' field.
 * 2. Replaced 'isOverTarget' with 'isOverAluminumBin' and 'isOverPlasticBin'.
 * 3. Replaced 'hasBeenPlaced' with 'hasBeenPlacedCorrectly' and 'hasBeenPlacedIncorrectly'.
 * 4. OnTriggerStay now checks for new tags ("TargetBinAluminum", "TargetBinPlastic") 
 * and sets the correct/incorrect flags based on the bottle's material.
 * 5. Added a 'ResetState()' method for the agent to call OnEpisodeBegin.
*/

using UnityEngine;

public class BottleTarget : MonoBehaviour
{
    // NEW: Define the material types
    public enum MaterialType
    {
        Plastic,
        Aluminum
    }
    
    [Header("Bottle State")]
    public MaterialType material;
    public bool isHeld = false;

    [HideInInspector]
    public bool hasBeenPlacedCorrectly = false;
    [HideInInspector]
    public bool hasBeenPlacedIncorrectly = false;
    [HideInInspector]
    public bool isOverAluminumBin = false;
    [HideInInspector]
    public bool isOverPlasticBin = false;

    private ArmAgent_FK agent;

    private void Start()
    {
        // Find the agent in the parent environment
        if (agent == null)
        {
            agent = GetComponentInParent<ArmAgent_FK>();
        }
    }

    /// <summary>
    /// Public method to reset all flags at the start of an episode.
    /// </summary>
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

        // Dropped on the ground (assuming ground has "Default" layer or a specific tag)
        if (collision.gameObject.layer == 0) // Default layer
        {
            if (agent != null)
            {
                agent.OnBottleDropped();
            }
        }
    }

    private void OnTriggerStay(Collider other)
    {
        // Check which bin we are over
        if (other.CompareTag("TargetBinAluminum"))
        {
            isOverAluminumBin = true;
        }
        if (other.CompareTag("TargetBinPlastic"))
        {
            isOverPlasticBin = true;
        }

        // Check for placement *after* setting the flags
        bool isOverAnyBin = isOverAluminumBin || isOverPlasticBin;
        bool hasNotBeenPlaced = !hasBeenPlacedCorrectly && !hasBeenPlacedIncorrectly;

        if (!isHeld && hasNotBeenPlaced && isOverAnyBin)
        {
            // Check if we are relatively upright and stable
            if (Vector3.Dot(transform.up, Vector3.up) > 0.9f && GetComponent<Rigidbody>().linearVelocity.magnitude < 0.1f)
            {
                // Now, check if the placement is correct
                if (material == MaterialType.Aluminum && isOverAluminumBin)
                {
                    hasBeenPlacedCorrectly = true;
                }
                else if (material == MaterialType.Plastic && isOverPlasticBin)
                {
                    hasBeenPlacedCorrectly = true;
                }
                else
                {
                    // We are stable over a bin, but it's the wrong one
                    hasBeenPlacedIncorrectly = true;
                }
            }
        }
    }
    
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("TargetBinAluminum"))
        {
            isOverAluminumBin = false;
        }
        if (other.CompareTag("TargetBinPlastic"))
        {
            isOverPlasticBin = false;
        }
    }
}