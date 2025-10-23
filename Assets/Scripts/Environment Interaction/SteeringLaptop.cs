using UnityEngine;
using TMPro; // Add this if you are using TextMeshPro

public class InteractableObject : MonoBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private Transform focusTarget; // The empty GameObject for the camera

    [Header("UI Prompt")]
    [SerializeField] private GameObject interactPromptUI; // The "E" UI object
    // If you want the prompt to follow the object on-screen:
    [SerializeField] private Vector3 promptOffset = new Vector3(0, 150, 0);

    // Private state
    private bool playerInRange = false;
    private CameraManager cameraManager; // Reference to the player's camera manager

    void Start()
    {
        // Find the CameraManager on the player. 
        // This assumes your player is tagged "Player".
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player)
        {
            cameraManager = player.GetComponent<CameraManager>();
        }

        // Ensure UI is hidden at start
        if (interactPromptUI)
        {
            interactPromptUI.SetActive(false);
        }
    }

    // This runs when the player enters the trigger collider
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            if (interactPromptUI)
            {
                interactPromptUI.SetActive(true);
            }
        }
    }

    // This runs when the player leaves the trigger collider
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            if (interactPromptUI)
            {
                interactPromptUI.SetActive(false);
            }
            
            // If the player walks away while focused, exit focus
            if (cameraManager != null && cameraManager.IsFocusing())
            {
                cameraManager.ExitFocus();
            }
        }
    }

    void Update()
    {
        // If the player is in range
        if (playerInRange)
        {
            // --- Optional: Make UI Follow Object ---
            // This makes the UI element hover over the object in screen space.
            if (interactPromptUI)
            {
                Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position);
                interactPromptUI.transform.position = screenPos + promptOffset;
            }
            // --- End Optional ---

            // Check if the player presses the interact key
            if (Input.GetKeyDown(interactKey))
            {
                if (cameraManager != null && !cameraManager.IsFocusing())
                {
                    // Tell the CameraManager to start focusing
                    cameraManager.MoveToFocus(focusTarget);
                    
                    // Hide the "E" prompt while we are focused
                    interactPromptUI.SetActive(false);
                }
            }
        }
    }
}
