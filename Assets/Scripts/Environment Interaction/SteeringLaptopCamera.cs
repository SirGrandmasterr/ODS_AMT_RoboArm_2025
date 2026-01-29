using UnityEngine;
using System.Collections;
using UnityFactorySceneHDRP;

public class CameraManager : MonoBehaviour
{
    [Header("Camera Control")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float transitionSpeed = 5.0f;
    [SerializeField] private KeyCode exitKey = KeyCode.Escape;

    [Header("Player Components")]
    [SerializeField] private MonoBehaviour playerMovementScript; 
    [SerializeField] private MonoBehaviour mouseLookScript;

    [Header("Simulation Integration")]
    [Tooltip("Drag the Robot Agent here to switch it to manual mode when focusing.")]
    [SerializeField] private ArmAgentSorting_Curriculum robotAgent;

    // Private state
    private Transform originalCameraParent;
    private Vector3 originalCameraLocalPos;
    private Quaternion originalCameraLocalRot;

    private Transform currentFocusTarget;
    private bool isFocusing = false;

    public bool IsFocusing() => isFocusing;

    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        originalCameraParent = mainCamera.transform.parent;
        originalCameraLocalPos = mainCamera.transform.localPosition;
        originalCameraLocalRot = mainCamera.transform.localRotation;

        if (playerMovementScript == null) playerMovementScript = GetComponent<CameraMove>(); 
        if (mouseLookScript == null) mouseLookScript = GetComponent<CameraMove>();
    }

    void Update()
    {
        if (isFocusing)
        {
            if (Input.GetKeyDown(exitKey) || Input.GetKeyDown(KeyCode.E))
            {
                ExitFocus();
            }
        }
    }

    public void MoveToFocus(Transform newTarget)
    {
        if (isFocusing) return;

        isFocusing = true;
        currentFocusTarget = newTarget;

        EnablePlayerControls(false);

        // --- ENABLE MANUAL DEBUG MODE ON ROBOT ---
        if (robotAgent != null)
        {
            robotAgent.SetManualDebugMode(true);
        }
        // -----------------------------------------

        mainCamera.transform.SetParent(null, true);
        StopAllCoroutines();
        StartCoroutine(SmoothTransition(currentFocusTarget.position, currentFocusTarget.rotation));
    }

    public void ExitFocus()
    {
        if (!isFocusing) return;

        isFocusing = false;

        // --- DISABLE MANUAL DEBUG MODE ON ROBOT ---
        if (robotAgent != null)
        {
            robotAgent.SetManualDebugMode(false);
        }
        // ------------------------------------------
        
        mainCamera.transform.SetParent(originalCameraParent, true);
        StopAllCoroutines();
        StartCoroutine(SmoothTransition(originalCameraLocalPos, originalCameraLocalRot, true));
    }

    private void EnablePlayerControls(bool enable)
    {
        if (playerMovementScript != null) playerMovementScript.enabled = enable;
        if (mouseLookScript != null) mouseLookScript.enabled = enable;

        Cursor.lockState = enable ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !enable;
    }

    private IEnumerator SmoothTransition(Vector3 targetPos, Quaternion targetRot, bool isReturning = false)
    {
        while (true)
        {
            if (isReturning)
            {
                mainCamera.transform.localPosition = Vector3.Lerp(mainCamera.transform.localPosition, targetPos, Time.deltaTime * transitionSpeed);
                mainCamera.transform.localRotation = Quaternion.Lerp(mainCamera.transform.localRotation, targetRot, Time.deltaTime * transitionSpeed);

                if (Vector3.Distance(mainCamera.transform.localPosition, targetPos) < 0.01f)
                {
                    mainCamera.transform.localPosition = targetPos;
                    mainCamera.transform.localRotation = targetRot;
                    EnablePlayerControls(true);
                    yield break;
                }
            }
            else
            {
                mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, targetPos, Time.deltaTime * transitionSpeed);
                mainCamera.transform.rotation = Quaternion.Lerp(mainCamera.transform.rotation, targetRot, Time.deltaTime * transitionSpeed);

                if (Vector3.Distance(mainCamera.transform.position, targetPos) < 0.01f)
                {
                    mainCamera.transform.position = targetPos;
                    mainCamera.transform.rotation = targetRot;
                    yield break;
                }
            }
            yield return null;
        }
    }
}