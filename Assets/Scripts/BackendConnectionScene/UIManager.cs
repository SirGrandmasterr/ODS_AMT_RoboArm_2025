using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace UnityML.Auth
{
    public class AuthUIManager : MonoBehaviour
    {
        [Header("Scene Config")]
        public string trainingSceneName = "RobotArm";

        [Header("UI References")]
        public TMP_InputField usernameInput;
        public TMP_InputField passwordInput;
        public Button actionButton;     // The main button (Login or Register)
        public Button switchModeButton; // Switch between Login/Register
        public TextMeshProUGUI statusText;
        public TextMeshProUGUI headerText;
        public TextMeshProUGUI switchModeText;

        private bool isLoginMode = true;

        private void Start()
        {
            UpdateUIState();
            
            // Bind Buttons
            actionButton.onClick.AddListener(OnActionClicked);
            switchModeButton.onClick.AddListener(ToggleMode);
            
            statusText.text = "";
        }

        private void ToggleMode()
        {
            isLoginMode = !isLoginMode;
            statusText.text = "";
            UpdateUIState();
        }

        private void UpdateUIState()
        {
            if (isLoginMode)
            {
                headerText.text = "Login";
                actionButton.GetComponentInChildren<TextMeshProUGUI>().text = "Login";
                switchModeText.text = "New user? Create Account";
            }
            else
            {
                headerText.text = "Register";
                actionButton.GetComponentInChildren<TextMeshProUGUI>().text = "Create Account";
                switchModeText.text = "Already have an account? Login";
            }
        }

        private void OnActionClicked()
        {
            string user = usernameInput.text;
            string pass = passwordInput.text;

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                statusText.color = Color.red;
                statusText.text = "Username and Password required.";
                return;
            }

            SetInteractivity(false);
            statusText.color = Color.yellow;
            statusText.text = "Connecting...";

            if (isLoginMode)
            {
                StartCoroutine(BackendApiService.Instance.Login(user, pass, HandleLoginResponse));
            }
            else
            {
                StartCoroutine(BackendApiService.Instance.Register(user, pass, HandleRegisterResponse));
            }
        }

        private void HandleLoginResponse(bool success, string message)
        {
            SetInteractivity(true);
            if (success)
            {
                statusText.color = Color.green;
                statusText.text = "Success! Loading...";
                // Transition to the training scene
                SceneManager.LoadScene(trainingSceneName);
            }
            else
            {
                statusText.color = Color.red;
                statusText.text = message;
            }
        }

        private void HandleRegisterResponse(bool success, string message)
        {
            SetInteractivity(true);
            if (success)
            {
                statusText.color = Color.green;
                statusText.text = message;
                // Automatically switch to login mode after successful registration
                isLoginMode = true;
                UpdateUIState();
            }
            else
            {
                statusText.color = Color.red;
                statusText.text = message;
            }
        }

        private void SetInteractivity(bool interactable)
        {
            usernameInput.interactable = interactable;
            passwordInput.interactable = interactable;
            actionButton.interactable = interactable;
            switchModeButton.interactable = interactable;
        }
    }
}