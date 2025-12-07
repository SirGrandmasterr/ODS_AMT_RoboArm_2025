using UnityEngine;

public class Test : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        Vector3 sphere = RandomSphericalPosition(1.0f, 2.0f);
        Vector3 pos = SphericalToCartesian(sphere);

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.localScale = Vector3.one * 0.1f;

        Vector3 clampedPos = new Vector3(pos.x, Mathf.Max(pos.y, .5f), pos.z);
        go.transform.position = clampedPos;
    }

    private Vector3 RandomSphericalPosition(float minR, float maxR)
    {
        float r = Random.Range(minR, maxR);
        float polar = Random.Range(0, Mathf.PI / 2);
        float azimuth = Random.Range(0, 2 * Mathf.PI);

        return new Vector3(r, polar, azimuth);
    }

    private Vector3 SphericalToCartesian(Vector3 sphericalPos)
    {
        float x = sphericalPos.x * Mathf.Sin(sphericalPos.y) * Mathf.Cos(sphericalPos.z);
        float y = sphericalPos.x * Mathf.Cos(sphericalPos.y);
        float z = sphericalPos.x * Mathf.Sin(sphericalPos.y) * Mathf.Sin(sphericalPos.z);

        return new Vector3(x, y, z);
    }
}
