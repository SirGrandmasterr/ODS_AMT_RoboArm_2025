using Unity.InferenceEngine;
using UnityEngine;

public class VisualTrackingController : MonoBehaviour
{
    public Vector3 LastDetectedBottlePosition { get; private set; }

    [SerializeField] private Camera _camera1;
    [SerializeField] private Camera _camera2;
    [SerializeField] private ModelAsset _modelAsset;

    private Model _runtimeModel;
    private Worker _worker;
    private RenderTexture _renderTexture1;
    private RenderTexture _renderTexture2;

    [Header("Settings")]
    [SerializeField] private bool normalizeInputTo255 = false; // Toggle to multiply input by 255

    [Header("Debugging")]
    [SerializeField] private GameObject _bottle;

    [Header("Static Image")]
    [SerializeField] private bool useStaticImage = false;
    [SerializeField] private Texture2D testImage; // Drag your downloaded bottle.jpg here

    [Header("Visualization")]
    [SerializeField] private bool visualize = false;
    [SerializeField] private UnityEngine.UI.RawImage debugView1;
    [SerializeField] private UnityEngine.UI.RawImage debugView2;

    const int MODEL_SIZE = 640;


    private void Start()
    {
        LoadModel();
        SetupRenderTextures();

        debugView1.gameObject.SetActive(visualize);
        debugView2.gameObject.SetActive(visualize);
    }

    private void LoadModel()
    {
        _runtimeModel = ModelLoader.Load(_modelAsset);
        _worker = new Worker(_runtimeModel, BackendType.GPUCompute);
    }

    private void SetupRenderTextures()
    {
        _renderTexture1 = new RenderTexture(MODEL_SIZE, MODEL_SIZE, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        _camera1.targetTexture = _renderTexture1;

        _renderTexture2 = new RenderTexture(MODEL_SIZE, MODEL_SIZE, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        _camera2.targetTexture = _renderTexture2;
    }

    private void Update()
    {
        if (useStaticImage) ProcessStaticImage();
        else ProcessCameraImages();
    }

    private void ProcessStaticImage()
    {
        Texture texture = testImage;
        float[] result = ProcessCameraImage(texture);
        VisualTrackingDebugger.LogMaxConfidence(result, 39); // Log max confidence for bottle class

        Rect box = BottleDetector.GetBottlePosition(result);
    }

    private void ProcessCameraImages()
    {
        // Camera 1
        float[] result1 = ProcessCameraImage(_renderTexture1);
        float[] result2 = ProcessCameraImage(_renderTexture2);

        Rect box1 = BottleDetector.GetBottlePosition(result1);
        Rect box2 = BottleDetector.GetBottlePosition(result2);

        bool hasBox1 = box1.width > 0 && box1.height > 0;
        bool hasBox2 = box2.width > 0 && box2.height > 0;

        if (hasBox1 && hasBox2)
        {
            // TRIANGULATION
            Vector3 worldPos = VisualTrackingTriangulator.GetWorldPosition(box1, box2, _camera1, _camera2, MODEL_SIZE, visualize);
            LastDetectedBottlePosition = worldPos;

            if (visualize)
            {
                Debug.DrawLine(_camera1.transform.position, worldPos, Color.green);
                Debug.DrawLine(_camera2.transform.position, worldPos, Color.green);
                Debug.Log($"[Tracking] Triangulated: {worldPos} vs. Real: {_bottle.transform.position}");
            }
        }

        // VISUALIZATION
        if (visualize)
        {
            VisualTrackingDebugger.UpdateDebugViewWithBox(debugView1, _renderTexture1, box1, Color.green, MODEL_SIZE);
            VisualTrackingDebugger.UpdateDebugViewWithBox(debugView2, _renderTexture2, box2, Color.green, MODEL_SIZE);
        }
    }

    private float[] ProcessCameraImage(Texture texture)
    {
        if (texture == null) return null;

        // Get input image as tensor
        using Tensor<float> inputTensor = new Tensor<float>(new TensorShape(1, 3, 640, 640));
        TextureConverter.ToTensor(texture, inputTensor);

        if (normalizeInputTo255)
        {
            float[] data = inputTensor.DownloadToArray();
            for (int i = 0; i < data.Length; i++)
            {
                data[i] *= 255.0f;
            }

            // Create a new temporary tensor from the modified data
            using Tensor<float> scaledTensor = new Tensor<float>(new TensorShape(1, 3, 640, 640), data);
            _worker.Schedule(scaledTensor);

            // Get Output
            using Tensor<float> outputTensor = _worker.PeekOutput() as Tensor<float>;
            return outputTensor.DownloadToArray();
        }
        else
        {
            // Standard Path
            _worker.Schedule(inputTensor);

            using Tensor<float> outputTensor = _worker.PeekOutput() as Tensor<float>;
            return outputTensor.DownloadToArray();
        }
    }



    void OnDestroy()
    {
        _worker?.Dispose();
        if (_renderTexture1 != null) _renderTexture1.Release();
        if (_renderTexture2 != null) _renderTexture2.Release();
    }

}
