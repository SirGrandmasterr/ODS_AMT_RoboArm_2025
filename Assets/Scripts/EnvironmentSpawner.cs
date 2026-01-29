using UnityEngine;

/// <summary>
/// This script dynamically instantiates a grid of training environments
/// to accelerate ML-Agents training.
/// </summary>
public class EnvironmentSpawner : MonoBehaviour
{
    [Header("Spawning Configuration")]
    [Tooltip("The self-contained Training Environment prefab to be spawned.")]
    public GameObject trainingEnvPrefab;

    [Tooltip("The total number of environments to spawn (e.g., 16). This INCLUDES the one already in the scene.")]
    public int totalEnvironmentCount = 16;
    
    [Tooltip("The number of environments per row/column (e.g., 4 for a 4x4 grid).")]
    public int gridSize = 4;

    [Tooltip("The distance to leave between each environment.")]
    public float spacing = 15.0f;

    void Start()
    {
        // Check if the prefab is assigned
        if (trainingEnvPrefab == null)
        {
            Debug.LogError("Training Env Prefab is not assigned in the EnvironmentSpawner!", this);
            return;
        }

        // We assume one environment (the original) already exists in the scene at (0,0,0).
        // So we start our loop at 1.
        int existingEnvironments = 1;

        for (int i = existingEnvironments; i < totalEnvironmentCount; i++)
        {
            // Calculate grid position
            int x = i % gridSize;
            int z = i / gridSize;
            float y = trainingEnvPrefab.transform.localPosition.y;

            // Calculate the world position for the new environment
            Vector3 position = new Vector3(x * spacing, y, z * spacing);

            // Instantiate the new environment preserving prefab's rotation
            GameObject newEnv = Instantiate(trainingEnvPrefab, position, trainingEnvPrefab.transform.rotation);
            
            // Optional: Name it for easier debugging in the Hierarchy
            newEnv.name = $"TrainingEnvironment_{i}";
            
            Debug.Log($"Spawned Environment {i} at {position} with Rotation {newEnv.transform.rotation.eulerAngles}");
        }
    }
}