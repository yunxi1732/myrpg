using System;
using System.Collections.Generic;
using System.Linq;
using Rpg.Networking;
using UnityEngine;

// Save rules and file operations live in the game assembly, not the built-in prefab component.
public sealed class SaveManagerRuntime : ISaveManagerRuntime
{
    private readonly SaveManager owner;
    private readonly FileDataHandler dataHandler;
    private List<ISaveManager> saveManagers;
    private bool initialized;
    private bool networkSceneInstance;
    private bool BlockNetworkSave => networkSceneInstance || RoomSceneCoordinator.ProtectSaves || GameSimulationClock.IsNetwork;

    public SaveManagerRuntime(SaveManager owner)
    {
        this.owner = owner;
        dataHandler = new FileDataHandler(Application.persistentDataPath, owner.FileName, owner.EncryptData);
    }

    public static bool ShouldSuppressInstance(SaveManager owner) =>
        RoomSceneCoordinator.ProtectSaves && owner.gameObject.scene.name == Rpg.Networking.Protocol.RoomScene.Name;

    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        networkSceneInstance = ShouldSuppressInstance(owner);
        if (BlockNetworkSave) { owner.SetLoadedState(false); return; }
        saveManagers = FindAllSaveManagers();
        LoadGame();
    }

    public void DeleteSavedData()
    {
        if (BlockNetworkSave) return;
        dataHandler.Delete();
    }

    public void NewGame() => owner.gameData = new GameData();

    public void LoadGame()
    {
        if (BlockNetworkSave) { owner.SetLoadedState(false); return; }
        owner.gameData = dataHandler.Load();
        bool loadedFromSave = owner.gameData != null;
        if (!loadedFromSave)
        {
            Debug.Log("No saved data found!");
            NewGame();
        }
        if (saveManagers != null)
            foreach (ISaveManager saveManager in saveManagers)
                if (saveManager is MonoBehaviour component && EntityRuntimePermissions.CanPersist(component))
                    saveManager.LoadData(owner.gameData);
        owner.SetLoadedState(loadedFromSave);
    }

    public void SaveGame()
    {
        if (BlockNetworkSave || saveManagers == null || owner.gameData == null) return;
        try
        {
            foreach (ISaveManager saveManager in saveManagers)
                if (saveManager is MonoBehaviour component && EntityRuntimePermissions.CanPersist(component))
                    saveManager.SaveData(ref owner.gameData);
            dataHandler.Save(owner.gameData);
        }
        catch (Exception error)
        {
            Debug.LogError("[Save] Save skipped; the last valid file was kept. " + error);
        }
    }

    public bool HaveSavedData() => dataHandler.Load() != null;

    private static List<ISaveManager> FindAllSaveManagers() =>
        UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
            .Where(EntityRuntimePermissions.CanPersist).OfType<ISaveManager>().ToList();
}
