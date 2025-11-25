using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityML.Auth
{
    public class BackendApiService : MonoBehaviour
    {
        public static BackendApiService Instance { get; private set; }

        [Header("Configuration")]
        // Replace with your actual backend IP (e.g., http://192.168.1.50:8000)
        // Use "http://localhost:8000" ONLY if running Unity Editor on the same machine
        public string baseUrl = "http://localhost:8000"; 

        public string AccessToken { get; private set; }
        public bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); // Persist between scenes
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public IEnumerator Login(string username, string password, Action<bool, string> callback)
        {
            // OAuth2PasswordRequestForm expects form fields
            WWWForm form = new WWWForm();
            form.AddField("username", username);
            form.AddField("password", password);

            string url = $"{baseUrl}/auth/token";

            using (UnityWebRequest www = UnityWebRequest.Post(url, form))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<TokenResponse>(www.downloadHandler.text);
                    AccessToken = response.access_token;
                    Debug.Log($"[BackendApi] Login Success. Token: {AccessToken.Substring(0, 10)}...");
                    callback?.Invoke(true, "Login Successful");
                }
                else
                {
                    string errorMsg = ParseError(www);
                    Debug.LogError($"[BackendApi] Login Failed: {errorMsg}");
                    callback?.Invoke(false, errorMsg);
                }
            }
        }

        public IEnumerator Register(string username, string password, Action<bool, string> callback)
        {
            // Your register endpoint also expects Form Data based on 'OAuth2PasswordRequestForm' dependency
            WWWForm form = new WWWForm();
            form.AddField("username", username);
            form.AddField("password", password);

            string url = $"{baseUrl}/auth/register";

            using (UnityWebRequest www = UnityWebRequest.Post(url, form))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log("[BackendApi] Registration Success.");
                    callback?.Invoke(true, "Registration Successful. Please Login.");
                }
                else
                {
                    string errorMsg = ParseError(www);
                    Debug.LogError($"[BackendApi] Registration Failed: {errorMsg}");
                    callback?.Invoke(false, errorMsg);
                }
            }
        }

        // Helper to send Authorized requests in the next scene
        public UnityWebRequest CreateAuthenticatedRequest(string endpoint, string method)
        {
            string url = $"{baseUrl}{endpoint}";
            var www = new UnityWebRequest(url, method);
            www.downloadHandler = new DownloadHandlerBuffer();
            
            if (IsLoggedIn)
            {
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
            }
            
            return www;
        }

        private string ParseError(UnityWebRequest www)
        {
            try
            {
                // Try to parse the JSON error message from FastAPI
                var errorData = JsonUtility.FromJson<ErrorResponse>(www.downloadHandler.text);
                if (errorData != null && !string.IsNullOrEmpty(errorData.detail))
                    return errorData.detail;
            }
            catch { }

            // Fallback to generic network error
            return www.error;
        }
    }
}