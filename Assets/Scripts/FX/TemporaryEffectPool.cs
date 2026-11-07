using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene-owned pools for temporary effects. Only pop up text, hit effects and afterimages use the scene pool.</summary>
public sealed class TemporaryEffectPool : MonoBehaviour
{
    private readonly Dictionary<GameObject, PrefabPool> pools = new Dictionary<GameObject, PrefabPool>();

    private static readonly Dictionary<int, TemporaryEffectPool> scenePools = new Dictionary<int, TemporaryEffectPool>();

    public static TemporaryEffectPool ForScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("Cannot create an effect pool for an unloaded scene.");
        if (scenePools.TryGetValue(scene.handle, out var existing) && existing != null && !existing.IsDisposed)
            return existing;
        var root = new GameObject("Temporary Effect Pool");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        var pool = root.AddComponent<TemporaryEffectPool>();
        pool.BindScene();
        scenePools[scene.handle] = pool;
        root.SetActive(true);
        return pool;
    }

    public bool IsDisposed { get; private set; }
    private bool subscribed;
    private Scene ownerScene;

    private void Awake() { BindScene(); }
    private void BindScene()
    {
        if (subscribed || IsDisposed) return;
        ownerScene = gameObject.scene;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        subscribed = true;
    }
    private void OnSceneUnloaded(Scene scene)
    {
        if (scene == ownerScene) Dispose();
    }
    private void EnsureAlive()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(TemporaryEffectPool));
        BindScene();
    }
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        if (subscribed) SceneManager.sceneUnloaded -= OnSceneUnloaded;
        subscribed = false;
        if (scenePools.TryGetValue(ownerScene.handle, out var registered) && ReferenceEquals(registered, this))
            scenePools.Remove(ownerScene.handle);
        var snapshot = new List<PrefabPool>(pools.Values);
        pools.Clear();
        foreach (var pool in snapshot) pool.Dispose();
    }

    public sealed class PrefabPool
    {
        public bool IsDisposed { get; private set; }
        public GameObject Prefab { get; private set; }
        private readonly List<PooledTemporaryEffect> instances = new List<PooledTemporaryEffect>();
        private readonly List<PooledTemporaryEffect> idle = new List<PooledTemporaryEffect>();
        public int IdleCapacity { get; private set; } = 32;
        public int IdleCount { get { idle.RemoveAll(item => item == null); return idle.Count; } }
        private readonly TemporaryEffectPool manager;
        internal PrefabPool(GameObject prefab, TemporaryEffectPool manager) { Prefab = prefab; this.manager = manager; }
        internal bool ReturnToOwner(PooledTemporaryEffect instance)
        {
            return !IsDisposed && manager != null && manager.Return(instance);
        }

        internal void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            var snapshot = instances.ToArray();
            instances.Clear();
            idle.Clear();
            foreach (var instance in snapshot)
            {
                if (instance == null) continue;
                instance.StopPlayback();
                instance.DetachPool();
                instance.gameObject.SetActive(false);
                if (instance != null) UnityEngine.Object.Destroy(instance.gameObject);
            }
        }

        internal void SetIdleCapacity(int capacity)
        {
            IdleCapacity = capacity;
            idle.RemoveAll(item => item == null);
            while (idle.Count > capacity)
            {
                var excess = idle[idle.Count - 1];
                idle.RemoveAt(idle.Count - 1);
                instances.Remove(excess);
                UnityEngine.Object.Destroy(excess.gameObject);
            }
        }

        internal bool Return(PooledTemporaryEffect instance, Transform parent)
        {
            if (IsDisposed || instance == null || instance.OwnerPool != this || !instance.IsInUse
                || instance.IsReturning || !instances.Contains(instance)) return false;
            instance.IsReturning = true;
            try
            {
                instance.StopPlayback();
                if (IsDisposed || instance == null) return false;
                instance.gameObject.SetActive(false);
                if (IsDisposed || instance == null) return false;
                instance.transform.SetParent(parent, false);
                if (IsDisposed || instance == null) return false;
                instance.MarkIdle();
                idle.RemoveAll(item => item == null);
                if (idle.Count < IdleCapacity) idle.Add(instance);
                else
                {
                    instances.Remove(instance);
                    UnityEngine.Object.Destroy(instance.gameObject);
                }
                return true;
            }
            finally { if (instance != null) instance.IsReturning = false; }
        }
        internal PooledTemporaryEffect Take(Transform parent, Vector3 position, Quaternion rotation)
        {
            instances.RemoveAll(item => item == null);
            PooledTemporaryEffect instance = null;
            while (idle.Count > 0 && instance == null)
            {
                instance = idle[idle.Count - 1];
                idle.RemoveAt(idle.Count - 1);
            }
            if (instance == null)
            {
                // Instantiate inactive so OnEnable sees ownership and usage already assigned.
                GameObject staging = new GameObject("Temporary effect staging");
                staging.SetActive(false);
                staging.transform.SetParent(parent, false);
                try
                {
                    GameObject clone = UnityEngine.Object.Instantiate(Prefab, staging.transform);
                    clone.SetActive(false);
                    instance = clone.GetComponent<PooledTemporaryEffect>();
                    if (instance == null) instance = clone.AddComponent<PooledTemporaryEffect>();
                    instance.Initialize(this);
                    instances.Add(instance);
                    clone.transform.SetParent(parent, false);
                }
                finally { UnityEngine.Object.Destroy(staging); }
            }
            instance.MarkInUse();
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.gameObject.SetActive(true);
            return instance;
        }
    }

    public PooledTemporaryEffect Take(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        EnsureAlive();
        if (prefab == null) throw new ArgumentNullException(nameof(prefab));
        if (!pools.TryGetValue(prefab, out PrefabPool pool))
        {
            pool = new PrefabPool(prefab, this);
            pools.Add(prefab, pool);
        }
        return pool.Take(transform, position, rotation);
    }

    /// <summary>Limits retained idle instances only. Active effects are never capped or destroyed.</summary>
    public void SetIdleCapacity(GameObject prefab, int capacity)
    {
        EnsureAlive();
        if (prefab == null) throw new ArgumentNullException(nameof(prefab));
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (!pools.TryGetValue(prefab, out PrefabPool pool))
        {
            pool = new PrefabPool(prefab, this);
            pools.Add(prefab, pool);
        }
        pool.SetIdleCapacity(capacity);
    }

    public bool Return(PooledTemporaryEffect instance)
    {
        if (IsDisposed || this == null || instance == null || instance.OwnerPool == null) return false;
        var owner = instance.OwnerPool;
        if (owner.Prefab == null || !pools.TryGetValue(owner.Prefab, out PrefabPool registered)
            || registered != owner) return false;
        return owner.Return(instance, transform);
    }
    // Instances remain scene children; no persistent singleton or global effect cleanup.
    private void OnDestroy() { Dispose(); }
}
