/*
    HOW TO USE:
    1. Create a folder named "Editor" at the root of your Assets folder 
       (if it doesn't already exist).
    2. Place this C# script inside that "Editor" folder.
    3. Wait for Unity to compile.
    4. A new menu item "Tools/Find High-Poly Meshes in Scene" will appear
       at the top of the Unity editor.
    5. Click it to run the scan on your currently open scene.
    6. The results will be printed to your Console window.
*/

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class HighPolyMeshFinder : EditorWindow
{
    // Set the vertex count threshold you consider "too high"
    private static int vertexThreshold = 20000;

    // A list to store the results
    private static List<MeshInfo> highPolyMeshes = new List<MeshInfo>();

    [MenuItem("Tools/Find High-Poly Meshes in Scene")]
    public static void FindHighPolyMeshes()
    {
        ClearResults();
        Debug.Log($"--- Scanning Scene for Meshes with > {vertexThreshold} Vertices ---");

        // Find all MeshFilter components in the active scene
        MeshFilter[] meshFilters = FindObjectsOfType<MeshFilter>();

        foreach (MeshFilter mf in meshFilters)
        {
            // Get the sharedMesh to avoid creating instances
            Mesh mesh = mf.sharedMesh;

            if (mesh == null)
            {
                Debug.LogWarning($"GameObject '{mf.gameObject.name}' has a MeshFilter but no mesh.", mf.gameObject);
                continue;
            }

            if (mesh.vertexCount > vertexThreshold)
            {
                highPolyMeshes.Add(new MeshInfo(mf.gameObject, mesh.name, mesh.vertexCount));
            }
        }
        
        // Also check SkinnedMeshRenderers (for animated characters, etc.)
        SkinnedMeshRenderer[] skinnedRenderers = FindObjectsOfType<SkinnedMeshRenderer>();
        foreach (SkinnedMeshRenderer smr in skinnedRenderers)
        {
            Mesh mesh = smr.sharedMesh;

            if (mesh == null)
            {
                Debug.LogWarning($"GameObject '{smr.gameObject.name}' has a SkinnedMeshRenderer but no mesh.", smr.gameObject);
                continue;
            }

            if (mesh.vertexCount > vertexThreshold)
            {
                highPolyMeshes.Add(new MeshInfo(smr.gameObject, mesh.name, mesh.vertexCount));
            }
        }

        // --- Report Results ---
        if (highPolyMeshes.Count == 0)
        {
            Debug.Log($"--- Scan Complete: No meshes found with more than {vertexThreshold} vertices. Good job! ---");
            return;
        }

        // Sort the list from highest vertex count to lowest
        highPolyMeshes = highPolyMeshes.OrderByDescending(m => m.vertexCount).ToList();

        Debug.LogWarning($"--- Scan Complete: Found {highPolyMeshes.Count} HIGH-POLY MESHES. (Click to select) ---");
        
        foreach (MeshInfo info in highPolyMeshes)
        {
            Debug.LogWarning($"<b>{info.vertexCount.ToString("N0")} vertices</b> | Mesh: '{info.meshName}' | GameObject: '<b>{info.gameObject.name}</b>'", info.gameObject);
        }
        
        // Select the worst offender in the hierarchy
        if (highPolyMeshes.Count > 0)
        {
            Selection.activeGameObject = highPolyMeshes[0].gameObject;
        }
    }

    private static void ClearResults()
    {
        highPolyMeshes.Clear();
    }
    
    // Helper class to store the results
    private class MeshInfo
    {
        public GameObject gameObject;
        public string meshName;
        public int vertexCount;

        public MeshInfo(GameObject go, string name, int count)
        {
            gameObject = go;
            meshName = name;
            vertexCount = count;
        }
    }
}