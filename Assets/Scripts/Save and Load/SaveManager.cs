using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using JetBrains.Annotations;
using Rpg.Networking;

public class SaveManager : MonoBehaviour
{
    public static SaveManager instance;

    [SerializeField] private string fileName;
    [SerializeField] private bool encrypeData;
    public GameData gameData;
    private List<ISaveManager> saveManagers;
    private FileDataHandler dataHandler;
    public bool HasLoaded { get; private set; }
    public bool LoadedFromSave { get; private set; }
    private bool networkSceneInstance;
    private bool BlockNetworkSave => networkSceneInstance || RoomSceneCoordinator.ProtectSaves || GameSimulationClock.IsNetwork;

    [ContextMenu("Delete saved file")]
    public void DeleteSavedData()
    {
        if (BlockNetworkSave) return;
        dataHandler = new FileDataHandler(Application.persistentDataPath, fileName, encrypeData);
        dataHandler.Delete();
    }

    private void Start()
    {
        if (BlockNetworkSave) { HasLoaded = true; return; }
        dataHandler = new FileDataHandler(Application.persistentDataPath, fileName, encrypeData);
        saveManagers = FindAllSaveManagers();

        LoadGame();
    }

    private void Awake()
    {
        networkSceneInstance = RoomSceneCoordinator.ProtectSaves && gameObject.scene.name == Rpg.Networking.Protocol.RoomScene.Name;
        if (networkSceneInstance) { enabled = false; return; }
        if (instance != null)
            Destroy(instance.gameObject);
        else
            instance = this;
    }

    public void NewGame()
    {
        gameData = new GameData();
    }

    public void LoadGame()
    {
        if (BlockNetworkSave) { HasLoaded = true; return; }
        gameData = dataHandler.Load();
        LoadedFromSave = gameData != null;

        if (this.gameData == null)
        {
            Debug.Log("No saved data found!");
            NewGame();
        }

        foreach (ISaveManager saveManager in saveManagers)
        {
            if (saveManager is MonoBehaviour owner && EntityRuntimePermissions.CanPersist(owner)) saveManager.LoadData(gameData);
        }
        HasLoaded = true;
    }

    public void SaveGame()
    {
        if (BlockNetworkSave) return;
        foreach (ISaveManager saveManager in saveManagers)
        {
            if (saveManager is MonoBehaviour owner && EntityRuntimePermissions.CanPersist(owner)) saveManager.SaveData(ref gameData);
        }
        dataHandler.Save(gameData);
    }

    private void OnApplicationQuit()
    {
        SaveGame();
    }

    private List<ISaveManager> FindAllSaveManagers()
    {
        IEnumerable<ISaveManager> saveManagers = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
            .Where(EntityRuntimePermissions.CanPersist).OfType<ISaveManager>();

        return new List<ISaveManager>(saveManagers);
    }

    public bool HaveSavedData()
    {
        if (dataHandler.Load() != null)
            return true;

        return false;
    }
}
