using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class LoginUIManager : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject registerPanel;
    [SerializeField] private GameObject scenarioPanel;

    [Header("Login Inputs")]
    [SerializeField] private TMP_InputField loginUsernameInput;
    [SerializeField] private TMP_InputField loginPasswordInput;
    [SerializeField] private Button loginButton;
    [SerializeField] private Button goToRegisterButton;
    [SerializeField] private TextMeshProUGUI loginStatusText;

    [Header("Register Inputs")]
    [SerializeField] private TMP_InputField registerUsernameInput;
    [SerializeField] private TMP_InputField registerPasswordInput;
    [SerializeField] private TMP_InputField registerConfirmPasswordInput;
    [SerializeField] private Button registerButton;
    [SerializeField] private Button goToLoginButton;
    [SerializeField] private TextMeshProUGUI registerStatusText;

    private void Start()
    {
        // Initial State
        ShowLogin();

        // Bind Buttons
        loginButton.onClick.AddListener(OnLoginClicked);
        goToRegisterButton.onClick.AddListener(ShowRegister);
        
        registerButton.onClick.AddListener(OnRegisterClicked);
        goToLoginButton.onClick.AddListener(ShowLogin);

        // Bind Auth Events
        if (AuthManager.Instance != null)
        {
            AuthManager.Instance.OnLoginSuccess += HandleLoginSuccess;
            AuthManager.Instance.OnLoginFailed += HandleLoginFailed;
            AuthManager.Instance.OnRegisterSuccess += HandleRegisterSuccess;
            AuthManager.Instance.OnRegisterFailed += HandleRegisterFailed;
        }
    }

    private void OnDestroy()
    {
        if (AuthManager.Instance != null)
        {
            AuthManager.Instance.OnLoginSuccess -= HandleLoginSuccess;
            AuthManager.Instance.OnLoginFailed -= HandleLoginFailed;
            AuthManager.Instance.OnRegisterSuccess -= HandleRegisterSuccess;
            AuthManager.Instance.OnRegisterFailed -= HandleRegisterFailed;
        }
    }

    // --- UI Actions ---

    private void OnLoginClicked()
    {
        string username = loginUsernameInput.text;
        string password = loginPasswordInput.text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            loginStatusText.text = "Please enter username and password";
            return;
        }

        loginStatusText.text = "Logging in...";
        AuthManager.Instance.Login(username, password);
    }

    private void OnRegisterClicked()
    {
        string username = registerUsernameInput.text;
        string password = registerPasswordInput.text;
        string confirm = registerConfirmPasswordInput.text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            registerStatusText.text = "Please fill all fields";
            return;
        }

        if (password != confirm)
        {
            registerStatusText.text = "Passwords do not match";
            return;
        }

        registerStatusText.text = "Registering...";
        AuthManager.Instance.Register(username, password);
    }

    // --- Event Handlers ---

    private void HandleLoginSuccess(string token)
    {
        loginStatusText.text = "Success!";
        ShowScenarioSelection();
    }

    private void HandleLoginFailed(string error)
    {
        loginStatusText.text = $"Error: {error}";
    }

    private void HandleRegisterSuccess(string msg)
    {
        registerStatusText.text = "Success! Logging in...";
    }

    private void HandleRegisterFailed(string error)
    {
        registerStatusText.text = $"Error: {error}";
    }

    // --- Panel Switching ---

    private void ShowLogin()
    {
        loginPanel.SetActive(true);
        registerPanel.SetActive(false);
        scenarioPanel.SetActive(false);
        loginStatusText.text = "";
    }

    private void ShowRegister()
    {
        loginPanel.SetActive(false);
        registerPanel.SetActive(true);
        scenarioPanel.SetActive(false);
        registerStatusText.text = "";
    }

    private void ShowScenarioSelection()
    {
        loginPanel.SetActive(false);
        registerPanel.SetActive(false);
        scenarioPanel.SetActive(true);
    }
}
