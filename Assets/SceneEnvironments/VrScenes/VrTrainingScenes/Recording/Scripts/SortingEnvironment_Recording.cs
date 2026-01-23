using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;

public class SortingEnvironment_Recording : MonoBehaviour
{
    [Header("Environment References")]
    [Tooltip("The bottle object to be sorted.")]
    public Transform bottle;
    [Tooltip("The Rigidbody of the bottle.")]
    public Rigidbody bottleRb;
    [Tooltip("Renderer for changing bottle material visuals.")]
    public MeshRenderer bottleMeshRenderer;
    [Tooltip("Script component on the bottle for tracking state.")]
    public BottleTargetSorting_Curriculum bottleScript;
    public Transform bottleSpawnPoint;
    public Transform bottleOriginalParent;

    [Header("Targets")]
    public Transform targetBinAluminum;
    public Transform targetBinPlastic;

    [Header("Settings")]
    public BoxCollider randomizationArea;
    public Material plasticMaterial;
    public Material aluminumMaterial;

    [Header("Debug / Recording")]
    public bool forceFullTaskMode = false;
    
    [Header("Audio")]
    public AudioClip successSound;
    public AudioSource audioSource;
    
    // Internal State
    public float CurrentLessonNumber { get; private set; }
    public Transform CurrentCorrectTargetBin { get; private set; }
    
    private Quaternion initialBottleRot;

    private void Awake()
    {
        if (bottle) initialBottleRot = bottle.rotation;
        if (bottleRb) bottleRb.sleepThreshold = 0.0f;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
    }
    
    public void PlaySuccessSound()
    {
        if (audioSource && successSound)
        {
            audioSource.PlayOneShot(successSound);
        }
    }

    /// <summary>
    /// Called by the Agent at the start of an episode to reset the scene.
    /// Uses the Academy curriculum 'lesson_number'.
    /// </summary>
    public void ResetEnvironment()
    {
        CurrentLessonNumber = Academy.Instance.EnvironmentParameters.GetWithDefault("lesson_number", 0f);

        // Recording Mode Override
        if (forceFullTaskMode) CurrentLessonNumber = 3.0f; // Treat as Full Task

        if (bottleScript) bottleScript.ResetState();

        switch (CurrentLessonNumber)
        {
            case 0f: SetupLesson_Reach(); break;
            case 1f: SetupLesson_Grab(); break;
            case 2f: SetupLesson_Place(); break;
            default: SetupLesson_FullTask(); break;
        }
    }

    // --- Lesson Setup Logic ---

    private void SetupLesson_Reach()
    {
        // Lesson 0: Reach - Bottle spawns in random spot, bins disabled. 
        // Goal: Just get close to the bottle.
        ResetBottlePhysics(GetRandomSpawnPos(randomizationArea.bounds, 0.1f), true);
        SetBottleMaterial(BottleTargetSorting_Curriculum.MaterialType.Plastic);
        
        if(targetBinAluminum) targetBinAluminum.gameObject.SetActive(false);
        if(targetBinPlastic) targetBinPlastic.gameObject.SetActive(false);
    }

    private void SetupLesson_Grab()
    {
        // Lesson 1: Grab - Spawns in smaller area. Goal: Pick it up.
        // Derived from original: Bounds smallerBounds = new Bounds(bottleSpawnPoint.position, randomizationArea.bounds.size * 0.5f);
        Bounds smallerBounds = new Bounds(bottleSpawnPoint.position, randomizationArea.bounds.size * 0.5f);
        ResetBottlePhysics(GetRandomSpawnPos(smallerBounds, 0.1f), false);
        SetBottleMaterial(BottleTargetSorting_Curriculum.MaterialType.Plastic);

        if(targetBinAluminum) targetBinAluminum.gameObject.SetActive(false);
        if(targetBinPlastic) targetBinPlastic.gameObject.SetActive(false);
    }

    private void SetupLesson_Place(Transform agentEndEffector = null)
    {
        ResetBottlePhysics(bottleSpawnPoint.position, true);
        RandomizeBottleMaterialAndTarget();
        
        if(targetBinAluminum) targetBinAluminum.gameObject.SetActive(true);
        if(targetBinPlastic) targetBinPlastic.gameObject.SetActive(true);
    }

    private void SetupLesson_FullTask()
    {
        ResetBottlePhysics(bottleSpawnPoint.position, false);
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
        
        // Reset hierarchy if it was held
        if (bottleOriginalParent) bottle.SetParent(bottleOriginalParent);
    }

    private Vector3 GetRandomSpawnPos(Bounds bounds, float yOffset)
    {
        return new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            bounds.center.y + yOffset,
            Random.Range(bounds.min.z, bounds.max.z)
        );
    }

    private void SetBottleMaterial(BottleTargetSorting_Curriculum.MaterialType matType)
    {
        if (bottleScript) bottleScript.material = matType;
        
        if (matType == BottleTargetSorting_Curriculum.MaterialType.Plastic)
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
        var matType = (BottleTargetSorting_Curriculum.MaterialType)Random.Range(0, 2);
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

        if (bottleScript.material == BottleTargetSorting_Curriculum.MaterialType.Plastic)
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

        if (bottleScript.material == BottleTargetSorting_Curriculum.MaterialType.Plastic)
        {
            return bottleScript.isOverAluminumBin; // Wrong bin
        }
        else
        {
            return bottleScript.isOverPlasticBin; // Wrong bin
        }
    }

    public float GetHorizontalDistanceToBin(Vector3 fromPos)
    {
        if (CurrentCorrectTargetBin == null) return float.MaxValue;
        return Vector2.Distance(new Vector2(fromPos.x, fromPos.z), new Vector2(CurrentCorrectTargetBin.position.x, CurrentCorrectTargetBin.position.z));
    }
}
