public interface ISaveManagerRuntime
{
    void Initialize();
    void DeleteSavedData();
    void NewGame();
    void LoadGame();
    void SaveGame();
    bool HaveSavedData();
}
