using UnityEngine;
using TMPro; // Make sure to include this namespace for TextMeshPro

public class UIManager : MonoBehaviour
{
    // A 'static instance' to make this a Singleton.
    // This allows any script to access it easily.
    public static UIManager Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("The parent panel for the notification")]
    public GameObject notificationPanel;

    [Tooltip("The TextMeshPro text element that displays the message")]
    public TMP_Text notificationText;

    void Awake()
    {
        // --- Singleton Pattern Setup ---
        // This ensures there is only ever one UIManager
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            // DontDestroyOnLoad(gameObject); // Optional: if you want UI to persist between scenes
        }
        // --- End Singleton Setup ---

        // Start with the panel hidden
        if (notificationPanel != null)
        {
            notificationPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Shows the notification panel with a specific message.
    /// </summary>
    /// <param name="message">The text you want to display.</param>
    public void ShowNotification(string message)
    {
        if (notificationPanel != null && notificationText != null)
        {
            notificationText.text = message;
            notificationPanel.SetActive(true);
        }
        else
        {
            Debug.LogWarning("Notification UI references are not set in UIManager.");
        }
    }

    /// <summary>
    /// Hides the notification panel.
    /// </summary>
    public void HideNotification()
    {
        if (notificationPanel != null)
        {
            notificationPanel.SetActive(false);
        }
    }
}