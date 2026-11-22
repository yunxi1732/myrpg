using System.Collections;
using System.IO;
using UnityEngine;

// This component is serialized in a prefab used by the built-in MainMenu.
// Game-specific save behavior is supplied after the runtime assembly is loaded.
public class SaveManager : MonoBehaviour
{
    public static SaveManager instance;

    [SerializeField] private string fileName;
    [SerializeField] private bool encrypeData;
    public GameData gameData;
    private ISaveManagerRuntime runtime;

    public bool HasLoaded { get; private set; }
    public bool LoadedFromSave { get; private set; }
    public string FileName => fileName;
    public bool EncryptData => encrypeData;

    private void Awake()
    {
        if (SaveManagerRuntimeRegistry.ShouldSuppressInstance(this))
        {
            HasLoaded = true;
            enabled = false;
            return;
        }
        if (instance != null && instance != this) Destroy(instance.gameObject);
        instance = this;
    }

    private IEnumerator Start()
    {
        while (!SaveManagerRuntimeRegistry.IsRegistered) yield return null;
        EnsureRuntime().Initialize();
    }

    private ISaveManagerRuntime EnsureRuntime()
    {
        if (runtime == null && SaveManagerRuntimeRegistry.IsRegistered)
            runtime = SaveManagerRuntimeRegistry.Create(this);
        return runtime;
    }

    public void SetLoadedState(bool loadedFromSave)
    {
        LoadedFromSave = loadedFromSave;
        HasLoaded = true;
    }

    [ContextMenu("Delete saved file")]
    public void DeleteSavedData()
    {
        ISaveManagerRuntime service = EnsureRuntime();
        if (service != null) { service.DeleteSavedData(); return; }
        // Keep the inspector command useful when no runtime has been registered in Edit Mode.
        if (!Application.isPlaying)
        {
            string path = Path.Combine(Application.persistentDataPath, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
    }
    public void NewGame() => EnsureRuntime()?.NewGame();
    public void LoadGame() => EnsureRuntime()?.LoadGame();
    public void SaveGame() => EnsureRuntime()?.SaveGame();
    public bool HaveSavedData() => EnsureRuntime()?.HaveSavedData() ?? false;

    private void OnApplicationQuit() => SaveGame();
    private void OnDestroy()
    {
        if (ReferenceEquals(instance, this)) instance = null;
    }
}
