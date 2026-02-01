using System.Collections.Generic;
using System.Linq;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UI;

public class VisualTrackingController : MonoBehaviour
{
    public Vector3 LastDetectedBottlePosition { get; private set; }

    [SerializeField] private List<Camera> _cameras = new List<Camera>();

    [Header("Model")]
    [SerializeField] private ModelAsset _modelAsset;

    private Model _runtimeModel;
    private Worker _worker;
    private List<RenderTexture> _renderTextures = new List<RenderTexture>();

    [Header("Settings")]
    [SerializeField] private bool normalizeInputTo255 = false; // Toggle to multiply input by 255

    [Header("Debugging")]
    [SerializeField] private GameObject _bottle;

    [Header("Static Image")]
    [SerializeField] private bool useStaticImage = false;
    [SerializeField] private Texture2D testImage;

    [Header("Visualization")]
    [SerializeField] private bool visualize = false;
    [SerializeField] private List<RawImage> _debugViews = new List<RawImage>();

    const int MODEL_SIZE = 640;

    private float startTime;
    private int count = 0;



    private void Start()
    {
        LoadModel();
        SetupRenderTextures();

        foreach (var debugView in _debugViews)
        {
            debugView.gameObject.SetActive(visualize);
        }

        startTime = Time.time;
    }

    private void LoadModel()
    {
        _runtimeModel = ModelLoader.Load(_modelAsset);
        _worker = new Worker(_runtimeModel, BackendType.GPUCompute);
    }

    private void SetupRenderTextures()
    {
        for (int i = 0; i < _cameras.Count; i++)
        {
            _renderTextures.Add(new RenderTexture(MODEL_SIZE, MODEL_SIZE, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));
            _cameras[i].targetTexture = _renderTextures[i];
        }
    }

    private void Update()
    {
        if (useStaticImage) ProcessStaticImage();
        else ProcessCameraImages();

        count++;

        if (count % 50 == 0)
        {
            float elapsed = Time.time - startTime;
            Debug.Log($"[Tracking] FPS: {count / elapsed:F2}");
            count = 0;
            startTime = Time.time;
        }
    }

    private void ProcessStaticImage()
    {
        Texture texture = testImage;
        float[] result = ProcessCameraImage(texture);
        VisualTrackingDebugger.LogMaxConfidence(result, 39); // Log max confidence for bottle class

        // Rect box = BottleDetector.GetBottlePosition(result);
    }

    private void ProcessCameraImages()
    {
        List<Detection> detections = new List<Detection>();

        for (int i = 0; i < _cameras.Count; i++)
        {
            Camera cam = _cameras[i];
            RenderTexture renderTexture = _renderTextures[i];

            float[] result = ProcessCameraImage(renderTexture);
            Detection detection = BottleDetector.GetBottlePosition(result);

            if (detection == null) continue;

            detection.renderTexture = renderTexture;
            detection.camera = cam;
            detection.cameraIndex = i;

            if (detection.isValid) detections.Add(detection);
        }

        Vector3 worldPos = VisualTrackingTriangulator.GetWorldPosition(detections, LastDetectedBottlePosition.y, visualize);

        if (worldPos == Vector3.zero)
        {
            Debug.Log($"[Tracking] No valid triangulation. Available detections was {detections.Count}");
        }

        // VISUALIZATION
        if (visualize)
        {
            foreach (var detection in detections)
            {
                Debug.DrawLine(detection.camera.transform.position, worldPos, Color.green);
                VisualTrackingDebugger.UpdateDebugViewWithBox(_debugViews[detection.cameraIndex], detection.renderTexture, detection.box, Color.green, MODEL_SIZE);
            }
            Debug.Log($"[Tracking] Triangulated: {worldPos} vs. Real: {_bottle.transform.position} | Confidences: [{string.Join(",", detections.Select(d => d.score.ToString("F2")))}]");
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
        foreach (var renderTexture in _renderTextures)
        {
            if (renderTexture != null) renderTexture.Release();
        }
    }

}
