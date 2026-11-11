using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour, ISaveManager
{
    public static GameManager instance;

    private Transform player;
    [SerializeField] private Checkpoint[] checkpoints;
    [SerializeField] private string closestCheckpointId;

    [Header("Lost currency")]
    [SerializeField] private GameObject lostCurrencyPrefab;
    public int lostCurrencyAmount;
    [SerializeField] private float lostCurrencyX;
    [SerializeField] private float lostCurrencyY;

    private void Awake()
    {
        if (instance != null && instance != this)
            Destroy(instance.gameObject);
        instance = this;
    }

    private void OnDestroy()
    {
        foreach (object owner in networkPauseOwners) GameplayControlLock.Release(owner);
        networkPauseOwners.Clear();
        pauseOwners.Clear();
        menuPaused = false;
        if (ReferenceEquals(instance, this))
        {
            instance = null;
            Time.timeScale = 1;
        }
    }

    private void Start()
    {
        checkpoints = FindObjectsByType<Checkpoint>(FindObjectsSortMode.InstanceID);
        player = PlayerManager.instance.player.transform;
    }
    public void RestartScene()
    {
        if (Rpg.Networking.RoomSceneCoordinator.ProtectSaves) return;
        SaveManager.instance.SaveGame();
        Scene scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.name);
    }

    public void LoadData(GameData _data)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanPersist(this)) return;
        StartCoroutine(LoadWithDelay(_data));
    }

    private void LoadCheckpoints(GameData _data)
    {
        foreach (KeyValuePair<string, bool> pair in _data.checkpoints)
        {
            foreach (Checkpoint c in checkpoints)
            {
                if (c.id == pair.Key && pair.Value == true)
                    c.ActiveCheckpoint();
            }
        }
    }

    private void LoadLostCurrency(GameData _data)
    {
        lostCurrencyAmount = _data.lostCurrencyAmount;
        lostCurrencyX = _data.lostCurrencyX;
        lostCurrencyY = _data.lostCurrencyY;
        if (lostCurrencyAmount > 0)
        {
            GameObject newLostCurrency = Instantiate(lostCurrencyPrefab, new Vector3(lostCurrencyX, lostCurrencyY), Quaternion.identity);
            newLostCurrency.GetComponent<LostCurrencyController>().currency = lostCurrencyAmount;
        }

        lostCurrencyAmount = 0;
    }

    private IEnumerator LoadWithDelay(GameData _data)
    {
        yield return GameSimulationClock.Wait(0.1f);
        if (!Rpg.Networking.EntityRuntimePermissions.CanPersist(this)) yield break;
        LoadCheckpoints(_data);
        LoadClosestCheckpoint(_data);
        LoadLostCurrency(_data);
    }

    public void SaveData(ref GameData _data)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanPersist(this)) return;
        _data.lostCurrencyAmount = lostCurrencyAmount;
        _data.lostCurrencyX = player.position.x;
        _data.lostCurrencyY = player.position.y;

        if (FindClosestCheckpoint() != null)
            _data.closestCheckpointId = FindClosestCheckpoint().id;
        _data.checkpoints.Clear();

        foreach (Checkpoint checkpoint in checkpoints)
        {
            _data.checkpoints.Add(checkpoint.id, checkpoint.activationStatus);
        }
    }

    private Checkpoint FindClosestCheckpoint()
    {
        float closestDistance = Mathf.Infinity;
        Checkpoint closestCheckpoint = null;
        foreach (var checkpoint in checkpoints)
        {
            float distanceToCheckpoint = Vector2.Distance(player.position, checkpoint.transform.position);
            if (distanceToCheckpoint < closestDistance && checkpoint.activationStatus == true)
            {
                closestDistance = distanceToCheckpoint;
                closestCheckpoint = checkpoint;
            }
        }
        return closestCheckpoint;
    }
    private void LoadClosestCheckpoint(GameData _data)
    {
        if (_data.closestCheckpointId == null)
            return;

        closestCheckpointId = _data.closestCheckpointId;
        foreach (Checkpoint c in checkpoints)
        {
            if (closestCheckpointId == c.id)
            {
                player.position = c.transform.position;
            }
        }
    }

    private bool menuPaused;
    private readonly HashSet<object> pauseOwners = new HashSet<object>();
    private readonly HashSet<object> networkPauseOwners = new HashSet<object>();
    private readonly object legacyNetworkPauseOwner = new object();
    // The legacy bool request only; owner-based menu/dialogue requests remain independent.
    public bool IsMenuPaused => menuPaused;

    // Compatibility entry point. Menus and dialogue now use owner-based pause requests.
    public void PauseGame(bool _pause)
    {
        if (GameSimulationClock.IsNetwork)
        {
            SetPauseOwner(legacyNetworkPauseOwner, _pause);
            return;
        }
        menuPaused = _pause;
        ApplyPause();
    }

    public void SetPauseOwner(object owner, bool paused)
    {
        if (ReferenceEquals(owner, null)) return;
        if (GameSimulationClock.IsNetwork)
        {
            pauseOwners.Remove(owner);
            if (paused) { networkPauseOwners.Add(owner); GameplayControlLock.Acquire(owner); }
            else { networkPauseOwners.Remove(owner); GameplayControlLock.Release(owner); }
            ApplyPause();
            return;
        }
        if (networkPauseOwners.Remove(owner)) GameplayControlLock.Release(owner);
        if (paused) pauseOwners.Add(owner);
        else pauseOwners.Remove(owner);
        ApplyPause();
    }

    private void ApplyPause()
    {
        // Cleanup from an old scene must not overwrite the replacement manager's pause state.
        if (ReferenceEquals(instance, this))
            Time.timeScale = GameSimulationClock.IsNetwork ? 1 : menuPaused || pauseOwners.Count > 0 ? 0 : 1;
    }

    public void RefreshPauseState() => ApplyPause();
}
