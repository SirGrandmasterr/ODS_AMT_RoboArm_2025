using UnityEngine;
using System.Collections.Generic;

public static class VisualTrackingTriangulator
{

    public static Vector3 GetWorldPosition(List<Detection> detections, float fallbackHeight, bool visualize)
    {
        List<Ray> rays = new List<Ray>();
        List<float> scores = new List<float>();

        foreach (var detection in detections)
        {
            Vector2 center = detection.box.center;
            float u = center.x / 640; // Assuming model size is 640
            float v = 1.0f - (center.y / 640); // Flip Y

            Ray ray = detection.camera.ViewportPointToRay(new Vector3(u, v, 0));
            rays.Add(ray);
            scores.Add(detection.score);
        }

        return GetWorldPosition(rays, scores, fallbackHeight, visualize);
    }
    public static Vector3 GetWorldPosition(Rect detectionBox, Camera visionCamera, float height, int modelSize)
    {
        // 1. Get the center of the box (in Model Pixels, e.g., 0 to 640)
        Vector2 center = detectionBox.center;

        // 2. Convert to "Viewport" coordinates (0 to 1 range)
        float u = center.x / modelSize;
        float v = 1.0f - (center.y / modelSize); // Flip Y

        // 3. Create a Ray from the camera
        Ray ray = visionCamera.ViewportPointToRay(new Vector3(u, v, 0));

        // 4. Define the Table Plane
        Plane tablePlane = new Plane(Vector3.up, new Vector3(0, height, 0));

        // 5. Calculate Intersection
        float distance;
        if (tablePlane.Raycast(ray, out distance))
        {
            Vector3 worldPosition = ray.GetPoint(distance);
            Debug.DrawLine(visionCamera.transform.position, worldPosition, Color.red);
            return worldPosition;
        }

        return Vector3.zero;
    }

    public static Vector3 GetWorldPosition(List<Ray> rays, List<float> scores, float fallbackHeight, bool visualize)
    {
        if (rays == null || rays.Count == 0) return Vector3.zero;

        // Case 1: Single Camera -> 2D -> 3D via Plane Intersection
        if (rays.Count == 1)
        {
            // Use the fallback height (last known Y) to intersect the single ray
            Plane tablePlane = new Plane(Vector3.up, new Vector3(0, fallbackHeight, 0));
            float dist;
            if (tablePlane.Raycast(rays[0], out dist))
            {
                Vector3 worldPos = rays[0].GetPoint(dist);
                if (visualize) Debug.DrawLine(rays[0].origin, worldPos, Color.red);
                return worldPos;
            }
            return Vector3.zero;
        }

        // Case 2: Multi-Camera -> Triangulate all pairs and average
        Vector3 sumPoints = Vector3.zero;
        int pairCount = 0;

        for (int i = 0; i < rays.Count; i++)
        {
            for (int j = i + 1; j < rays.Count; j++)
            {
                Vector3 p = CalculateTriangulation(rays[i], rays[j], visualize);
                if (p != Vector3.zero)
                {
                    sumPoints += p;
                    pairCount++;
                }
            }
        }

        if (pairCount > 0)
        {
            return sumPoints / pairCount;
        }

        return Vector3.zero;
    }

    // Kept for backward compatibility or direct calls
    public static Vector3 GetWorldPosition(Rect boxA, Rect boxB, Camera camA, Camera camB, int modelSize, bool visualize)
    {
        // Calculate Rays for both cameras

        // Cam A
        Vector2 centerA = boxA.center;
        float uA = centerA.x / modelSize;
        float vA = 1.0f - (centerA.y / modelSize);
        Ray rayA = camA.ViewportPointToRay(new Vector3(uA, vA, 0));

        // Cam B
        Vector2 centerB = boxB.center;
        float uB = centerB.x / modelSize;
        float vB = 1.0f - (centerB.y / modelSize);
        Ray rayB = camB.ViewportPointToRay(new Vector3(uB, vB, 0));

        return CalculateTriangulation(rayA, rayB, visualize);
    }

    public static Vector3 CalculateTriangulation(Ray rayA, Ray rayB, bool visualize)
    {
        Vector3 p1 = rayA.origin;
        Vector3 d1 = rayA.direction;
        Vector3 p2 = rayB.origin;
        Vector3 d2 = rayB.direction;

        if (visualize)
        {
            // Visual debug of the rays
            Debug.DrawRay(p1, d1 * 50, Color.yellow);
            Debug.DrawRay(p2, d2 * 50, Color.yellow);
        }

        Vector3 n = Vector3.Cross(d1, d2);
        float n2 = Vector3.Dot(n, n);

        // If lines are parallel
        if (n2 < 0.0001f)
        {
            return Vector3.zero;
        }

        Vector3 p1_p2 = p1 - p2;

        float v1v1 = Vector3.Dot(d1, d1);
        float v1v2 = Vector3.Dot(d1, d2);
        float v2v2 = Vector3.Dot(d2, d2);
        float v1_p2p1 = Vector3.Dot(d1, p2 - p1);
        float v2_p2p1 = Vector3.Dot(d2, p2 - p1);

        float det = v1v1 * v2v2 - v1v2 * v1v2;

        if (Mathf.Abs(det) < 0.0001f) return Vector3.zero;

        // Formula for closest point on two lines (Symbolic solution)
        // s = (b*e - c*d) / D
        // t = (a*e - b*d) / D
        // a=v1v1, b=v1v2, c=v2v2, d=v1_p2p1, e=v2_p2p1

        // s_numer = v1v2 * v2_p2p1 - v2v2 * v1_p2p1
        // t_numer = v1v1 * v2_p2p1 - v1v2 * v1_p2p1

        float s_numer = v2v2 * v1_p2p1 - v1v2 * v2_p2p1;
        float t_numer = v1v2 * v1_p2p1 - v1v1 * v2_p2p1;

        float final_s = s_numer / det;
        float final_t = t_numer / det;

        Vector3 closestPoint1 = p1 + final_s * d1;
        Vector3 closestPoint2 = p2 + final_t * d2;

        return (closestPoint1 + closestPoint2) * 0.5f;
    }
}