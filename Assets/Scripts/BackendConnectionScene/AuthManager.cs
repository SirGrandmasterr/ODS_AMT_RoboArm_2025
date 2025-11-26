using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

public class AuthManager : MonoBehaviour
{
    public static AuthManager Instance;

    [SerializeField] private string baseUrl = "http://localhost:8000";

    public event Action<string> OnLoginSuccess;
    public event Action<string> OnLoginFailed;
    public event Action<string> OnRegisterSuccess;
    public event Action<string> OnRegisterFailed;

    public string AccessToken { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void Login(string username, string password)
    {
        StartCoroutine(LoginCoroutine(username, password));
    }

    public void Register(string username, string password)
    {
        StartCoroutine(RegisterCoroutine(username, password));
    }

    private IEnumerator LoginCoroutine(string username, string password)
    {
        WWWForm form = new WWWForm();
        form.AddField("username", username);
        form.AddField("password", password);

        using (UnityWebRequest www = UnityWebRequest.Post($"{baseUrl}/auth/token", form))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                var response = JsonConvert.DeserializeObject<TokenResponse>(www.downloadHandler.text);
                AccessToken = response.access_token;
                Debug.Log($"Login Successful. Token: {AccessToken}");
                OnLoginSuccess?.Invoke(AccessToken);
            }
            else
            {
                Debug.LogError($"Login Failed: {www.error} - {www.downloadHandler.text}");
                OnLoginFailed?.Invoke(www.error);
            }
        }
    }

    private IEnumerator RegisterCoroutine(string username, string password)
    {
        WWWForm form = new WWWForm();
        form.AddField("username", username);
        form.AddField("password", password);

        using (UnityWebRequest www = UnityWebRequest.Post($"{baseUrl}/auth/register", form))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Registration Successful");
                OnRegisterSuccess?.Invoke("Registration Successful");
                // Auto login or ask user to login
                Login(username, password);
            }
            else
            {
                Debug.LogError($"Registration Failed: {www.error} - {www.downloadHandler.text}");
                OnRegisterFailed?.Invoke(www.error);
            }
        }
    }

    [System.Serializable]
    private class TokenResponse
    {
        public string access_token;
        public string token_type;
    }
}
