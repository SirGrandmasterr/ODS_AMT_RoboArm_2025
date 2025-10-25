using UnityEngine;

[RequireComponent(typeof(Collider))] // Ensures this object always has a collider
public class NotificationTrigger : MonoBehaviour
{
    [Header("Notification Settings")]
    [Tooltip("The message to display when the player enters this trigger.")]
    [TextArea(3, 5)] // Makes the string box bigger in the Inspector
    public string notificationMessage = "You have entered the area.";

    [Tooltip("The tag of the object that can activate this trigger (e.g., 'Player').")]
    public string playerTag = "Player";

    private void Start()
    {
        // Make sure the collider is set to 'Is Trigger'
        Collider col = GetComponent<Collider>();
        if (!col.isTrigger)
        {
            Debug.LogWarning("Collider on " + gameObject.name + " is not set to 'Is Trigger'. Forcing it now.", this);
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Check if the object that entered has the correct tag
        if (other.CompareTag(playerTag))
        {
            // Call the UIManager to show the notification
            UIManager.Instance.ShowNotification(notificationMessage);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // Check if the object that exited has the correct tag
        if (other.CompareTag(playerTag))
        {
            // Call the UIManager to hide the notification
            UIManager.Instance.HideNotification();
        }
    }
}