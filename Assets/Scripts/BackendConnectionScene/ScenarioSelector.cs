using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ScenarioSelector : MonoBehaviour
{
    [Header("Scenario Buttons")]
    [SerializeField] private Button scenario1Button;
    [SerializeField] private Button scenario2Button;
    [SerializeField] private Button scenario3Button;

    [Header("Scene Names")]
    [SerializeField] private string scenario1SceneName = "Scenario1";
    [SerializeField] private string scenario2SceneName = "Scenario2";
    [SerializeField] private string scenario3SceneName = "Scenario3";

    private void Start()
    {
        scenario1Button.onClick.AddListener(() => LoadScenario(scenario1SceneName));
        scenario2Button.onClick.AddListener(() => LoadScenario(scenario2SceneName));
        scenario3Button.onClick.AddListener(() => LoadScenario(scenario3SceneName));
    }

    private void LoadScenario(string sceneName)
    {
        Debug.Log($"Loading Scenario: {sceneName}");
        // Check if scene exists in build settings before loading to avoid crash/error
        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogWarning($"Scene '{sceneName}' not found in Build Settings. Just logging selection for now.");
        }
    }
}
