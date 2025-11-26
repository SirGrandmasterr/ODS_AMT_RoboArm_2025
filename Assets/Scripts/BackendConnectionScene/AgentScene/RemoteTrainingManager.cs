using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using System.IO;

public class RemoteTrainingManager : MonoBehaviour
{
    [SerializeField] private string baseUrl = "http://localhost:8000";
    [SerializeField] private string configFileName = "training_config.yaml"; // Assumed to be in StreamingAssets or root
    
    // If true, we are currently waiting for the server to be ready
    private bool isInitializing = false;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T) && !isInitializing)
        {
            StartRemoteTraining();
        }
    }

    public void StartRemoteTraining()
    {
        if (AuthManager.Instance == null)
        {
            Debug.LogError("AuthManager not found! Please login first.");
            return;
        }

        isInitializing = true;
        StartCoroutine(StartTrainingFlow());
    }

    private IEnumerator StartTrainingFlow()
    {
        Debug.Log("Starting Remote Training Flow...");

        // 1. Load Config File
        string configPath = Path.Combine(Application.dataPath, configFileName);
        if (!File.Exists(configPath))
        {
            // Try StreamingAssets if not in root Assets
            configPath = Path.Combine(Application.streamingAssetsPath, configFileName);
        }

        if (!File.Exists(configPath))
        {
            Debug.LogError($"Config file not found at {configPath}");
            isInitializing = false;
            yield break;
        }

        string configContent = File.ReadAllText(configPath);

        // 2. Upload Config and Start Session
        string runId = "";
        
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", System.Text.Encoding.UTF8.GetBytes(configContent), configFileName, "text/yaml");

        using (UnityWebRequest www = UnityWebRequest.Post($"{baseUrl}/train/start", form))
        {
            string token = GetAccessToken();
            if (!string.IsNullOrEmpty(token))
            {
                www.SetRequestHeader("Cookie", $"access_token={token}"); // Try cookie first
                // Also try Authorization header if backend supports it directly, but our backend uses cookies.
                // However, UnityWebRequest cookie handling can be tricky. 
                // Let's assume AuthManager might expose the token for manual header usage if needed.
                // For now, we'll try to rely on the cookie if AuthManager set it, 
                // BUT AuthManager stores the token string. We should manually add the header if the backend expects Bearer,
                // OR manually add the Cookie header.
                // Our backend `oauth2_scheme` expects "Authorization: Bearer <token>" OR cookie "access_token=<token>"
                // The `APIKeyCookie` in `auth.py` looks for `access_token` cookie.
            }
             // Manual cookie construction
            www.SetRequestHeader("Cookie", $"access_token={token}");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to start training: {www.error} - {www.downloadHandler.text}");
                isInitializing = false;
                yield break;
            }

            var response = JsonConvert.DeserializeObject<StartTrainingResponse>(www.downloadHandler.text);
            runId = response.run_id;
            Debug.Log($"Training Session Started. Run ID: {runId}");
        }

        // 3. Poll for Status
        string host = "";
        int port = 0;
        bool ready = false;

        while (!ready)
        {
            yield return new WaitForSeconds(2f); // Poll every 2 seconds

            using (UnityWebRequest www = UnityWebRequest.Get($"{baseUrl}/train/{runId}/status"))
            {
                string token = GetAccessToken();
                www.SetRequestHeader("Cookie", $"access_token={token}");

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Failed to get status: {www.error}");
                    isInitializing = false;
                    yield break;
                }

                var statusResponse = JsonConvert.DeserializeObject<TrainingStatusResponse>(www.downloadHandler.text);
                Debug.Log($"Status: {statusResponse.status}");

                if (statusResponse.status == "running")
                {
                    host = statusResponse.host_ip;
                    port = statusResponse.unity_port;
                    ready = true;
                }
                else if (statusResponse.status == "failed" || statusResponse.status == "stopped")
                {
                    Debug.LogError("Training session failed or stopped.");
                    isInitializing = false;
                    yield break;
                }
            }
        }

        // 4. Set Environment Variables
        Debug.Log($"Connecting to {host}:{port}...");
        System.Environment.SetEnvironmentVariable("MLAGENTS_HOST", host);
        System.Environment.SetEnvironmentVariable("MLAGENTS_PORT", port.ToString());

        // 5. Reload Scene to Trigger ML-Agents Initialization
        Debug.Log("Reloading Scene to initialize ML-Agents...");
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private string GetAccessToken()
    {
        // Reflection or public property access to AuthManager's token
        // Since AuthManager.accessToken is private in the previous code, we might need to make it public 
        // OR assume AuthManager has a method. 
        // I will assume I can access it via a new property or method I'll add to AuthManager, 
        // OR just use reflection for now if I can't change AuthManager easily (but I can).
        // Let's assume I will update AuthManager to expose the token.
        return AuthManager.Instance != null ? AuthManager.Instance.AccessToken : "";
    }

    [System.Serializable]
    private class StartTrainingResponse
    {
        public string run_id;
        public string status;
    }

    [System.Serializable]
    private class TrainingStatusResponse
    {
        public string run_id;
        public string status;
        public string host_ip;
        public int unity_port;
        public int tb_port;
    }
}
