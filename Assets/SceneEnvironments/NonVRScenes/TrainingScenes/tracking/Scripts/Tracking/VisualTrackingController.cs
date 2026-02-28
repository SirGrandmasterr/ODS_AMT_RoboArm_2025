using System.Collections.Generic;
using System.Linq;
using Unity.InferenceEngine;
using Unity.InferenceEngine.Tokenization;
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
    private bool _isProcessing = false;

    private bool _isInitialized = false;

    private void Start()
    {
        try
        {
            LoadModel();
            SetupRenderTextures();

            foreach (var debugView in _debugViews)
            {
                if (debugView != null)
                {
                    debugView.gameObject.SetActive(visualize);
                }
            }

            startTime = Time.time;
            _isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Tracking] Initialization failed: {e.Message}\n{e.StackTrace}");
            enabled = false;
        }
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
            if (_cameras[i] != null)
            {
                _cameras[i].targetTexture = _renderTextures[i];
            }
            else
            {
                Debug.LogWarning($"[Tracking] Camera at index {i} is null.");
            }
        }
    }

    private void Update()
    {
        if (!_isInitialized) return;

        if (visualize)
        {
            float offset = 0.05f;
            Debug.DrawLine(LastDetectedBottlePosition + Vector3.up * offset, LastDetectedBottlePosition - Vector3.up * offset, Color.red);
            Debug.DrawLine(LastDetectedBottlePosition + Vector3.right * offset, LastDetectedBottlePosition - Vector3.right * offset, Color.red);
            Debug.DrawLine(LastDetectedBottlePosition + Vector3.forward * offset, LastDetectedBottlePosition - Vector3.forward * offset, Color.red);
        }

        if (!_isProcessing)
        {
            if (useStaticImage) ProcessStaticImage();
            else ProcessCameraImagesAsync();

            count++;

            if (count % 10 == 0)
            {
                float elapsed = Time.time - startTime;
                // This is now "Application FPS" essentially, as Update is not blocked
                Debug.Log($"[Tracking] Update Rate: {count / elapsed:F2} Hz");
                count = 0;
                startTime = Time.time;
            }
        }



    }

    private void ProcessStaticImage()
    {
        Texture texture = testImage;
        float[] result = ProcessCameraImage(texture);
        VisualTrackingDebugger.LogMaxConfidence(result, 39); // Log max confidence for bottle class
    }

    private async void ProcessCameraImagesAsync()
    {
        _isProcessing = true;

        List<Detection> detections = new List<Detection>();

        try
        {
            string output = "";
            for (int i = 0; i < _cameras.Count; i++)
            {
                if (i >= _renderTextures.Count) continue;
                
                Camera cam = _cameras[i];
                if (cam == null) continue;

                RenderTexture renderTexture = _renderTextures[i];

                // 1. Run Inference (Async Readback)
                float[] result = await ProcessCameraImageAsync(renderTexture);

                output += $"{i}: {result == null}, ";
                // VisualTrackingDebugger.LogMaxConfidence(result, 39); // Log max confidence for bottle class

                Detection detection;

                if (result != null)
                {
                    detection = await System.Threading.Tasks.Task.Run(() => BottleDetector.GetBottlePosition(result));
                    if (detection == null) detection = new Detection() { isValid = false };
                }
                else
                {
                    detection = new Detection() { isValid = false };
                }

                if (detection.box.width + detection.box.height > 150f) detection.isValid = false;
                if (detection.score < 0.0005f) detection.isValid = false;

                output += $"score: {detection.score:F4}, box: {detection.box.width:F3}x{detection.box.height:F3}, valid: {detection.isValid}\n";

                detection.renderTexture = renderTexture;
                detection.camera = cam;
                detection.cameraIndex = i;

                detections.Add(detection);
            }


            Debug.Log($"[Tracking] Output: \n{output}");
            List<Detection> validDetections = detections.Where(d => d.isValid).ToList();

            // 3. Triangulate (Main Thread)
            Vector3 worldPos = VisualTrackingTriangulator.GetWorldPosition(validDetections, LastDetectedBottlePosition.y, visualize);

            // Only update position if valid
            if (worldPos != Vector3.zero)
            {
                LastDetectedBottlePosition = worldPos;
            }
            else
            {
                // Optional: Debug log for no triangulation
                // Debug.Log($"[Tracking] No valid triangulation. Available detections was {detections.Count}");
            }

            // 4. Visualization (Main Thread)
            if (visualize)
            {
                foreach (var detection in detections)
                {
                    if (detection.isValid)
                    {
                        Debug.DrawLine(detection.camera.transform.position, worldPos, Color.green);

                        // Draw render texture with box
                        VisualTrackingDebugger.UpdateDebugViewWithBox(_debugViews[detection.cameraIndex], detection.renderTexture, detection.box, Color.green, MODEL_SIZE);
                    }
                    else
                    {
                        // Draw render texture without box
                        VisualTrackingDebugger.UpdateDebugViewWithBox(_debugViews[detection.cameraIndex], detection.renderTexture, Rect.zero, Color.green, MODEL_SIZE);
                    }

                }

                if (detections.Count > 0)
                    Debug.Log($"[Tracking] Triangulated: {worldPos} vs. Real: {_bottle.transform.position} | Confidences: [{string.Join(",", detections.Select(d => d.score.ToString("F3")))}] | Validity: [{string.Join(",", detections.Select(d => $"({d.box.width.ToString("F2")}x{d.box.height.ToString("F2")})"))}]");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Tracking] Error in async process: {e}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    // Kept for static image testing (sync)
    private float[] ProcessCameraImage(Texture texture)
    {
        if (texture == null) return null;

        // Get input image as tensor
        using Tensor<float> inputTensor = new Tensor<float>(new TensorShape(1, 3, 640, 640));
        TextureConverter.ToTensor(texture, inputTensor);

        _worker.Schedule(inputTensor);
        using Tensor<float> outputTensor = _worker.PeekOutput() as Tensor<float>;
        return outputTensor.DownloadToArray();
    }

    private async System.Threading.Tasks.Task<float[]> ProcessCameraImageAsync(Texture texture)
    {
        if (texture == null) return null;

        // Get input image as tensor (Must be on Main Thread)
        using Tensor<float> inputTensor = new Tensor<float>(new TensorShape(1, 3, 640, 640));
        TextureConverter.ToTensor(texture, inputTensor);

        if (normalizeInputTo255)
        {
            // Note: Copying and modifying tensor data is expensive and should possibly be done in a shader or compute shader if FPS is critical.
            // For now, doing it via DownloadToArray is slow, but simpler to preserve logic. 
            // Ideally we'd remove this path if not needed.
            // However, for Async refactor, we can't easily make Tensor manipulation async on GPU without custom compute.
            // We will stick to the provided path but warn about performance if this branch is taken.

            // Wait, if we download to array here, we block.
            // Ideally TextureConverter handles normalization if we set parameters? 
            // Sentis TextureConverter usually has parameters for this.
            // But to keep it 1:1 with user code:

            float[] data = inputTensor.DownloadToArray(); // Blocking!
            for (int i = 0; i < data.Length; i++)
                data[i] *= 255.0f;

            using Tensor<float> scaledTensor = new Tensor<float>(new TensorShape(1, 3, 640, 640), data);
            _worker.Schedule(scaledTensor);
        }
        else
        {
            _worker.Schedule(inputTensor);
        }

        // Get Output (Async)
        using Tensor<float> outputTensor = _worker.PeekOutput() as Tensor<float>;

        // This is the key non-blocking call
        // Note: Make sure your Sentis version supports ReadbackAndCloneAsync or similar. 
        // Unity.InferenceEngine usually has it. If not, we might need a different approach.
        // Given 'using Unity.InferenceEngine', it maps to newer Sentis/Barracuda versions.
        var readback = await outputTensor.ReadbackAndCloneAsync();

        // Depending on version, result might be a Tensor or we might need to dispose it.
        // ReadbackAndCloneAsync returns a new Tensor on CPU.

        float[] result = readback.DownloadToArray();
        readback.Dispose();

        return result;
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
