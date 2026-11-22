public interface IMainMenuRuntime
{
    void Initialize(UI_MainMenu menu);
    void ContinueGame();
    void NewGame();
    void ExitGame();
    void OpenRoomPanel();
    void CloseRoomPanel();
    void ShowSceneProgress(string message, bool loading);
    void PreserveRoomInput();
    void ShowNetworkSceneView();
    void RevealPreparedScene();
}
