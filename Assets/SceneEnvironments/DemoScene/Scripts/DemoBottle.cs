using UnityEngine;

public class DemoBottle : MonoBehaviour
{
    public enum MaterialType { Plastic, Aluminum }
    
    [Header("State")]
    public MaterialType material;
    public bool isHeld = false;
    
    [Header("Bin Detection")]
    public bool isOverPlasticBin = false;
    public bool isOverAluminumBin = false;

    // Tags expected in the scene
    private const string TAG_BIN_PLASTIC = "TargetBinPlastic";
    private const string TAG_BIN_ALUMINUM = "TargetBinAluminum";

    public void ResetState()
    {
        isHeld = false;
        isOverPlasticBin = false;
        isOverAluminumBin = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(TAG_BIN_PLASTIC))
        {
            isOverPlasticBin = true;
        }
        else if (other.CompareTag(TAG_BIN_ALUMINUM))
        {
            isOverAluminumBin = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(TAG_BIN_PLASTIC))
        {
            isOverPlasticBin = false;
        }
        else if (other.CompareTag(TAG_BIN_ALUMINUM))
        {
            isOverAluminumBin = false;
        }
    }
}
