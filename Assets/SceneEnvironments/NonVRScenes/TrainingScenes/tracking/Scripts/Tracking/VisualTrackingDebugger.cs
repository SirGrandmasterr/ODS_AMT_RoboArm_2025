using System;
using UnityEngine;
using UnityEngine.UI;

public static class VisualTrackingDebugger
{
    public static void LogMaxConfidence(float[] trackingResults, int classIndex)
    {
        float maxScore = -1f;
        int numAnchors = 8400;

        // Loop through all anchors
        for (int i = 0; i < numAnchors; i += 10) // Step 10 is enough for debug
        {
            // Calculate index in the flattened array
            int rowIndex = 4 + classIndex;

            float score = trackingResults[rowIndex * numAnchors + i];

            if (score > maxScore)
            {
                maxScore = score;
            }
        }

        Debug.Log($"Max score for class {classIndex}: {maxScore}");
    }

    public static void UpdateDebugViewWithBox(RawImage debugView, RenderTexture rt, Rect box, Color color, int modelSize)
    {
        // 1. Create a temporary Texture2D to read pixels
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);

        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        // 2. Draw Box
        if (box.width > 0 && box.height > 0)
        {
            DrawDebugBox(tex, box, color, 3, modelSize);
        }

        // 3. Apply to UI
        if (debugView.texture != null && debugView.texture is Texture2D oldTex)
        {
            GameObject.Destroy(oldTex); // Cleanup previous frame's texture to avoid leaks
        }
        debugView.texture = tex;
    }

    public static void DrawDebugBox(Texture2D tex, Rect box, Color color, int thickness, int modelSize)
    {
        // We need to FLIP Y for Texture2D drawing
        float x = box.x;
        float y = modelSize - box.y - box.height; // Flip y
        float w = box.width;
        float h = box.height;

        // Draw Horizontal Lines
        for (int i = 0; i < thickness; i++)
        {
            for (int px = (int)x; px < x + w; px++)
            {
                tex.SetPixel(px, (int)y + i, color); // Bottom
                tex.SetPixel(px, (int)(y + h - 1 - i), color); // Top
            }
        }

        // Draw Vertical Lines
        for (int i = 0; i < thickness; i++)
        {
            for (int py = (int)y; py < y + h; py++)
            {
                tex.SetPixel((int)x + i, py, color); // Left
                tex.SetPixel((int)(x + w - 1 - i), py, color); // Right
            }
        }
        tex.Apply();
    }
}