using UnityEngine;
using System.Collections;
using UnityFactorySceneHDRP; // Needed for smooth transitions (Coroutines)

public class CameraManager : MonoBehaviour
{
    [Header("Camera Control")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float transitionSpeed = 5.0f;
    [SerializeField] private KeyCode exitKey = KeyCode.Escape; // Key to exit focus

    // This is the script you use for player movement and mouse look
    // You MUST assign this in the inspector or find it in Start()
    [Header("Player Components")]
    [SerializeField] private MonoBehaviour playerMovementScript; 
    [SerializeField] private MonoBehaviour mouseLookScript; // Assign your mouse look script (if separate)

    // Private state
    private Transform originalCameraParent;
    private Vector3 originalCameraLocalPos;
    private Quaternion originalCameraLocalRot;

    private Transform currentFocusTarget;
    private bool isFocusing = false;

    // Public method to check state
    public bool IsFocusing()
    {
        return isFocusing;
    }

    void Start()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        // Store the camera's original state (relative to the player)
        originalCameraParent = mainCamera.transform.parent;
        originalCameraLocalPos = mainCamera.transform.localPosition;
        originalCameraLocalRot = mainCamera.transform.localRotation;

        // --- Find Player Scripts if not assigned ---
        // This is a common setup for Unity's Starter Assets
        if (playerMovementScript == null)
        {
            // Replace "FirstPersonController" with your script's name
            playerMovementScript = GetComponent<CameraMove>(); 
        }
        if (mouseLookScript == null)
        {
             // Starter assets uses the same script, but you might have a separate one
            mouseLookScript = GetComponent<CameraMove>();
        }
        // --- End Find Scripts ---
    }

    void Update()
    {
        if (isFocusing)
        {
            // Check for exit key
            if (Input.GetKeyDown(exitKey) || Input.GetKeyDown(KeyCode.E))
            {
                ExitFocus();
            }
        }
    }

    // --- Public Methods to Control Camera ---

    public void MoveToFocus(Transform newTarget)
    {
        if (isFocusing) return; // Already focusing

        isFocusing = true;
        currentFocusTarget = newTarget;

        // Disable player controls
        EnablePlayerControls(false);

        // Unparent the camera so it can move freely
        mainCamera.transform.SetParent(null, true);

        // Start the smooth transition
        StopAllCoroutines();
        StartCoroutine(SmoothTransition(currentFocusTarget.position, currentFocusTarget.rotation));
    }

    public void ExitFocus()
    {
        if (!isFocusing) return; // Not currently focusing

        isFocusing = false;
        
        // Re-parent the camera and move it back to its original spot
        mainCamera.transform.SetParent(originalCameraParent, true);
        
        // Start the smooth transition back
        StopAllCoroutines();
        StartCoroutine(SmoothTransition(originalCameraLocalPos, originalCameraLocalRot, true));
    }

    // --- Helper Methods ---

    private void EnablePlayerControls(bool enable)
    {
        if (playerMovementScript != null)
        {
            playerMovementScript.enabled = enable;
        }
        
        if (mouseLookScript != null)
        {
            mouseLookScript.enabled = enable;
        }

        // Show/hide cursor
        Cursor.lockState = enable ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !enable;
    }

    // This Coroutine handles the smooth camera movement
    private IEnumerator SmoothTransition(Vector3 targetPos, Quaternion targetRot, bool isReturning = false)
    {
        while (true)
        {
            // Determine if we are using world space (focusing) or local space (returning)
            if (isReturning)
            {
                // Move towards local position/rotation
                mainCamera.transform.localPosition = Vector3.Lerp(mainCamera.transform.localPosition, targetPos, Time.deltaTime * transitionSpeed);
                mainCamera.transform.localRotation = Quaternion.Lerp(mainCamera.transform.localRotation, targetRot, Time.deltaTime * transitionSpeed);

                // Check if we're close enough to snap
                if (Vector3.Distance(mainCamera.transform.localPosition, targetPos) < 0.01f)
                {
                    mainCamera.transform.localPosition = targetPos;
                    mainCamera.transform.localRotation = targetRot;
                    EnablePlayerControls(true); // Re-enable controls *after* returning
                    yield break; // Exit the coroutine
                }
            }
            else
            {
                // Move towards world position/rotation
                mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, targetPos, Time.deltaTime * transitionSpeed);
                mainCamera.transform.rotation = Quaternion.Lerp(mainCamera.transform.rotation, targetRot, Time.deltaTime * transitionSpeed);

                // Check if we're close enough to snap
                if (Vector3.Distance(mainCamera.transform.position, targetPos) < 0.01f)
                {
                    mainCamera.transform.position = targetPos;
                    mainCamera.transform.rotation = targetRot;
                    yield break; // Exit the coroutine
                }
            }
            
            yield return null; // Wait for the next frame
        }
    }
}
