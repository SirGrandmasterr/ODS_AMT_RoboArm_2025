using System.Collections.Generic;
using UnityEngine;
using Unity.InferenceEngine;
using System.Linq;

public class BottleDetector : MonoBehaviour
{

    // The "Bottle" class is index 39 in COCO dataset
    private const int BOTTLE_CLASS_INDEX = 39;

    // Call this after you get your float[] results
    public static Detection GetBottlePosition(float[] results)
    {

        List<Detection> candidates = new List<Detection>();

        int numAnchors = 8400;
        int numClasses = 80;

        // 1. GATHER CANDIDATES
        for (int i = 0; i < numAnchors; i++)
        {
            // The score for 'bottle' is at index: 4 (x,y,w,h) + 39 (bottle index)
            // But the array is flattened: [row * 8400 + col]
            // Row 0-3: Box coords
            // Row 4-83: Class scores

            // Get Bottle Score (Row 43)
            float score = results[43 * numAnchors + i];

            if (score > 0.01f) // Threshold
            {
                // Decode Box (Row 0,1,2,3)
                float x = results[0 * numAnchors + i];
                float y = results[1 * numAnchors + i];
                float w = results[2 * numAnchors + i];
                float h = results[3 * numAnchors + i];

                // Convert from Center/Size to TopLeft/Size (standard Rect)
                float xMin = x - (w / 2);
                float yMin = y - (h / 2);

                candidates.Add(new Detection
                {
                    box = new Rect(xMin, yMin, w, h),
                    score = score,
                    isValid = w > 0 && h > 0
                });
            }
        }

        // 2. APPLY NMS (Non-Maximum Suppression)
        // This removes overlapping duplicates
        List<Detection> finalDetections = NMS(candidates, 0.45f);

        // 3. RETURN RESULT
        if (finalDetections.Count > 0)
        {
            // Return the highest scoring bottle
            return finalDetections.OrderByDescending(d => d.score).First();
        }

        return null; // No bottle found
    }

    // The "Magic" Math function to clean up duplicates
    private static List<Detection> NMS(List<Detection> boxes, float iouThreshold)
    {
        List<Detection> result = new List<Detection>();
        // Sort by score (highest first)
        boxes.Sort((a, b) => b.score.CompareTo(a.score));

        while (boxes.Count > 0)
        {
            Detection current = boxes[0];
            result.Add(current);
            boxes.RemoveAt(0);

            // Remove all other boxes that overlap significantly with this one
            for (int i = boxes.Count - 1; i >= 0; i--)
            {
                float intersection = GetIoU(current.box, boxes[i].box);
                if (intersection > iouThreshold)
                {
                    boxes.RemoveAt(i);
                }
            }
        }
        return result;
    }

    // Intersection over Union (IoU) Helper
    private static float GetIoU(Rect boxA, Rect boxB)
    {
        float xA = Mathf.Max(boxA.x, boxB.x);
        float yA = Mathf.Max(boxA.y, boxB.y);
        float xB = Mathf.Min(boxA.x + boxA.width, boxB.x + boxB.width);
        float yB = Mathf.Min(boxA.y + boxA.height, boxB.y + boxB.height);

        float interArea = Mathf.Max(0, xB - xA) * Mathf.Max(0, yB - yA);
        float boxAArea = boxA.width * boxA.height;
        float boxBArea = boxB.width * boxB.height;

        return interArea / (boxAArea + boxBArea - interArea);
    }
}