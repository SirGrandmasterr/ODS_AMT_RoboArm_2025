using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SortingEnvironment_Demo : MonoBehaviour
{
    [Header("Environment References")]
    [Tooltip("The bottle object to be sorted.")]
    public Transform bottle;
    [Tooltip("The Rigidbody of the bottle.")]
    public Rigidbody bottleRb;
    [Tooltip("Renderer for changing bottle material visuals.")]
    public MeshRenderer bottleMeshRenderer;
    [Tooltip("Script component on the bottle for tracking state.")]
    public DemoBottle bottleScript;
    public Transform bottleSpawnPoint;
    public Transform bottleOriginalParent;

    [Header("Targets")]
    public Transform targetBinAluminum;
    public Transform targetBinPlastic;

    [Header("Settings")]
    public BoxCollider randomizationArea;
    public Material plasticMaterial;
    public Material aluminumMaterial;
    
    // Internal State
    public Transform CurrentCorrectTargetBin { get; private set; }
    
    private Quaternion initialBottleRot;

    private void Awake()
    {
        if (bottle) initialBottleRot = bottle.rotation;
        if (bottleRb) bottleRb.sleepThreshold = 0.0f;
    }

    /// <summary>
    /// Resets the demo environment for a new run.
    /// </summary>
    public void ResetEnvironment()
    {
        if (bottleScript) bottleScript.ResetState();
        SetupDemoRun();
    }

    private void SetupDemoRun()
    {
        // Always "Full Task" mode equivalent
        ResetBottlePhysics(GetRandomSpawnPos(randomizationArea.bounds, 0.1f), false);
        RandomizeBottleMaterialAndTarget();
        
        if(targetBinAluminum) targetBinAluminum.gameObject.SetActive(true);
        if(targetBinPlastic) targetBinPlastic.gameObject.SetActive(true);
    }

    // --- Helper Methods ---

    public void ResetBottlePhysics(Vector3 position, bool isKinematic)
    {
        bottle.position = position;
        bottle.rotation = initialBottleRot;

        bottleRb.isKinematic = isKinematic;
        if (!isKinematic)
        {
            bottleRb.linearVelocity = Vector3.zero;
            bottleRb.angularVelocity = Vector3.zero;
        }
        
        if (bottleOriginalParent) bottle.SetParent(bottleOriginalParent);
    }

    private Vector3 GetRandomSpawnPos(Bounds bounds, float yOffset)
    {
        // Simple random position on the conveyor/table area
        return new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            bounds.center.y + yOffset,
            Random.Range(bounds.min.z, bounds.max.z)
        );
    }

    private void SetBottleMaterial(DemoBottle.MaterialType matType)
    {
        if (bottleScript) bottleScript.material = matType;
        
        if (matType == DemoBottle.MaterialType.Plastic)
        {
            CurrentCorrectTargetBin = targetBinPlastic;
            if (bottleMeshRenderer && plasticMaterial) bottleMeshRenderer.material = plasticMaterial;
        }
        else
        {
            CurrentCorrectTargetBin = targetBinAluminum;
            if (bottleMeshRenderer && aluminumMaterial) bottleMeshRenderer.material = aluminumMaterial;
        }
    }

    private void RandomizeBottleMaterialAndTarget()
    {
        var matType = (DemoBottle.MaterialType)Random.Range(0, 2);
        SetBottleMaterial(matType);
    }
    
    // --- Public Query API ---

    public bool IsInBinZone()
    {
        if (!bottleScript) return false;
        return bottleScript.isOverAluminumBin || bottleScript.isOverPlasticBin;
    }

    public bool CheckPlacementSuccess()
    {
        if (!bottleScript) return false;
        if (!IsInBinZone()) return false;

        if (bottleScript.material == DemoBottle.MaterialType.Plastic)
        {
            return bottleScript.isOverPlasticBin;
        }
        else // Aluminum
        {
            return bottleScript.isOverAluminumBin;
        }
    }

    public bool CheckPlacementFailure()
    {
        if (!bottleScript) return false;
        if (!IsInBinZone()) return false;

        if (bottleScript.material == DemoBottle.MaterialType.Plastic)
        {
            return bottleScript.isOverAluminumBin; // Wrong bin
        }
        else
        {
            return bottleScript.isOverPlasticBin; // Wrong bin
        }
    }
}
