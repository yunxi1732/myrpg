using UnityEngine;

// This class is the future hot-update entry point. The DLL loader must call
// MenuRuntimeEntry.Register after Assembly.Load and before enabling menu actions.
public static class MenuRuntimeEntry
{
#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#endif
    public static void Register()
    {
        GameplayControlLock.ResetForPlay();
        Player.ResetActivePlayers();
        SaveManagerRuntimeRegistry.Register(owner => new SaveManagerRuntime(owner), SaveManagerRuntime.ShouldSuppressInstance);
        AudioManager.IsSourceAudible = AudioSourceAudibility.IsAudible;
        MainMenuRuntimeRegistry.Register(menu => menu.gameObject.AddComponent<MenuRuntimeController>());
        Debug.Log("[HotUpdate] DLL update probe v2 registered");
    }
}

public sealed class MenuRuntimeController : MonoBehaviour, IMainMenuRuntime
{
    private MenuRoomFlow room;
    private MenuSinglePlayerFlow singlePlayer;

    public void Initialize(UI_MainMenu menu)
    {
        room = new MenuRoomFlow(menu);
        room.Initialize();
        room.ShowEntryMessage();
        singlePlayer = new MenuSinglePlayerFlow(menu, room);
        singlePlayer.Initialize();
    }

    private void Update() => room?.Tick();

    private void OnEnable() => room?.Resume();

    private void OnDisable() => room?.Suspend();

    private void OnDestroy()
    {
        room?.Shutdown();
        room = null;
        singlePlayer = null;
    }

    public void ContinueGame() => singlePlayer?.ContinueGame();
    public void NewGame() => singlePlayer?.NewGame();
    public void ExitGame() => singlePlayer?.ExitGame();
    public void OpenRoomPanel() => room?.OpenRoomPanel();
    public void CloseRoomPanel() => room?.CloseRoomPanel();
    public void ShowSceneProgress(string message, bool loading) => room?.ShowSceneProgress(message, loading);
    public void PreserveRoomInput() => room?.PreserveRoomInput();
    public void ShowNetworkSceneView() => room?.ShowNetworkSceneView();
    public void RevealPreparedScene() => room?.RevealPreparedScene();
}
