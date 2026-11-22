using System.Collections;
using Rpg.HotUpdate;
using UnityEngine;
using UnityEngine.UI;

// Serialized in the built-in MainMenu scene. A later-loaded DLL owns the behavior.
public class UI_MainMenu : MonoBehaviour
{
    [SerializeField] private string sceneName = "MainScene";
    [SerializeField] private GameObject continueButton;
    [SerializeField] private RoomPanel roomPanel;
    [SerializeField] private Button onlineButton;
    [SerializeField] private bool logRoomMessageMetrics = true;
    [SerializeField, Min(0)] private int roomTestSendDelayMs;
    [SerializeField, Min(0)] private int roomTestSendJitterMs;
    [SerializeField, Range(0, 100)] private int roomTestSendLossPercent;
    [SerializeField] private GameObject[] mainMenuButtons = new GameObject[0];

    private ResourceUpdateController resourceUpdate;
    private Animator fadeAnimator;
    private IMainMenuRuntime runtime;

    public string SceneName => sceneName;
    public GameObject ContinueButton => continueButton;
    public RoomPanel RoomPanel => roomPanel;
    public Button OnlineButton => onlineButton;
    public bool LogRoomMessageMetrics => logRoomMessageMetrics;
    public int RoomTestSendDelayMs => roomTestSendDelayMs;
    public int RoomTestSendJitterMs => roomTestSendJitterMs;
    public int RoomTestSendLossPercent => roomTestSendLossPercent;
    public GameObject[] MainMenuButtons => mainMenuButtons;
    public bool ResourcesReady => resourceUpdate != null && resourceUpdate.Ready;

    private void Awake()
    {
        HotUpdateScenes.ReleaseCurrent();
        foreach (Animator animator in GetComponentsInChildren<Animator>(true))
            if (animator.gameObject.name == "DarkScreen") { fadeAnimator = animator; break; }
        if (fadeAnimator == null) Debug.LogError("[MainMenu] DarkScreen Animator is missing.", this);
        resourceUpdate = GetComponent<ResourceUpdateController>();
        if (resourceUpdate == null) resourceUpdate = gameObject.AddComponent<ResourceUpdateController>();
        if (onlineButton != null) onlineButton.interactable = false;
    }

    private IEnumerator Start()
    {
        // The HybridCLR loader will register its factory after loading the DLL.
        while (!ResourcesReady || !MainMenuRuntimeRegistry.IsRegistered) yield return null;
        runtime = MainMenuRuntimeRegistry.Create(this);
        runtime.Initialize(this);
    }

    public void FadeOut()
    {
        if (fadeAnimator != null) fadeAnimator.SetTrigger("fadeOut");
    }

    public void FadeIn()
    {
        if (fadeAnimator != null) fadeAnimator.SetTrigger("fadeIn");
    }

    // Existing UnityEvent bindings in MainMenu.unity remain valid.
    public void ContinueGame() => runtime?.ContinueGame();
    public void NewGame() => runtime?.NewGame();
    public void ExitGame() => runtime?.ExitGame();

    // RoomSceneCoordinator uses this view without owning the built-in component.
    public void OpenRoomPanel() => runtime?.OpenRoomPanel();
    public void CloseRoomPanel() => runtime?.CloseRoomPanel();
    public void ShowSceneProgress(string message, bool loading) => runtime?.ShowSceneProgress(message, loading);
    public void PreserveRoomInput() => runtime?.PreserveRoomInput();
    public void ShowNetworkSceneView() => runtime?.ShowNetworkSceneView();
    public void RevealPreparedScene() => runtime?.RevealPreparedScene();
}

