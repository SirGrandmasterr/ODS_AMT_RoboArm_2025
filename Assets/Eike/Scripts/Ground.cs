using UnityEngine;

public class Ground : MonoBehaviour
{
    private MeshRenderer meshRenderer;
    void Start()
    {
        meshRenderer = GetComponent<MeshRenderer>();
    }

    public void ShowSuccess()
    {
        meshRenderer.material.color = Color.green;
    }

    public void ShowFailure()
    {
        meshRenderer.material.color = Color.red;
    }
}
