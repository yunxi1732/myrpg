using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rpg.Networking
{
    /// <summary>One room/scene lifetime for players and future skills, projectiles and drops. Owner 0 is room-owned.</summary>
    public sealed class RoomNetworkObjects : IDisposable
    {
        public sealed class Registration : IDisposable
        {
            private readonly RoomNetworkObjects owner;
            internal readonly long Key;
            internal Registration(RoomNetworkObjects owner, long key) { this.owner = owner; Key = key; }
            public void Dispose() => owner.Release(Key, true);
        }

        private sealed class Entry
        {
            public GameObject Object;
            public int PlayerId;
            public Action Detach;
            public Registration Registration;
            public RoomNetworkObject Link;
            public int NativeId;
        }

        private readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();
        private readonly Dictionary<int, long> objectKeys = new Dictionary<int, long>();
        private long nextKey;
        private readonly HashSet<int> departedPlayers = new HashSet<int>();
        private readonly int sceneHandle;
        private Action beforeRoomCleanup;
        private Action<int> beforePlayerCleanup;
        public string RoomSessionId { get; }
        public bool IsDisposed { get; private set; }
        public int Count => entries.Count;
        public Exception LastCleanupError { get; private set; }
        internal bool AllowsOwner(int playerId) => !IsDisposed && !departedPlayers.Contains(playerId);

        public RoomNetworkObjects(string room, Scene scene, Action beforeRoomCleanup = null, Action<int> beforePlayerCleanup = null)
        {
            if (!Guid.TryParseExact(room, "N", out _) || !scene.IsValid() || !scene.isLoaded)
                throw new ArgumentException("A current room and loaded scene are required.");
            RoomSessionId = room; sceneHandle = scene.handle;
            this.beforeRoomCleanup = beforeRoomCleanup; this.beforePlayerCleanup = beforePlayerCleanup;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        // The caller supplies A-confirmed source ownership; late registrations from departed players are rejected.
        // Detach removes this object's entity dictionary entry, events and other managed references.
        public Registration Register(string room, int playerId, GameObject value, Action detach = null)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(RoomNetworkObjects));
            if (room != RoomSessionId || playerId < 0 || departedPlayers.Contains(playerId) || value == null || value.scene.handle != sceneHandle)
                throw new InvalidOperationException("Object ownership does not belong to this live room/scene.");
            int id = value.GetInstanceID();
            if (objectKeys.TryGetValue(id, out long existingKey) && entries.TryGetValue(existingKey, out Entry existing))
            {
                if (existing.Object != null)
                {
                    if (existing.PlayerId != playerId || existing.Detach != detach) throw new InvalidOperationException("Object ownership cannot be reassigned.");
                    return existing.Registration;
                }
                Release(existingKey, false);
            }
            var link = value.GetComponent<RoomNetworkObject>();
            if (link == null) link = value.AddComponent<RoomNetworkObject>();
            if (link.IsAssigned || link.IsRetired) throw new InvalidOperationException("Create a fresh object for a new room lifetime.");
            // A room container may contain multiple players; a player-owned parent may only contain its own objects.
            for (Transform parent = value.transform.parent; parent != null; parent = parent.parent)
            {
                var parentOwner = parent.GetComponent<RoomNetworkObject>();
                if (parentOwner != null &&
                    (!parentOwner.IsAssigned || parentOwner.IsRetired || parentOwner.RoomSessionId != room ||
                    parentOwner.OwnerPlayerId != 0 && parentOwner.OwnerPlayerId != playerId))
                    throw new InvalidOperationException("A child object must inherit its player's cleanup ownership.");
            }
            long key = ++nextKey;
            var registration = new Registration(this, key);
            entries.Add(key, new Entry { Object = value, NativeId = id, PlayerId = playerId, Detach = detach, Registration = registration, Link = link });
            objectKeys.Add(id, key);
            link.Assign(this, key, playerId);
            return registration;
        }

        public Registration RegisterOwned(RoomNetworkObject source, GameObject value, Action detach = null)
        {
            if (source == null || !source.IsAssigned || source.IsRetired || !source.BelongsTo(this))
                throw new InvalidOperationException("A live source from this room must provide derived-object ownership.");
            return Register(source.RoomSessionId, source.OwnerPlayerId, value, detach);
        }

        public bool CleanupPlayer(string room, int playerId)
        {
            if (IsDisposed || room != RoomSessionId || playerId <= 0 || !departedPlayers.Add(playerId)) return false;
            Run(() => beforePlayerCleanup?.Invoke(playerId));
            var removed = new List<long>();
            foreach (var pair in entries) if (pair.Value.PlayerId == playerId) removed.Add(pair.Key);
            foreach (long key in removed) Release(key, true);
            return true;
        }

        internal void Release(long key, bool destroy)
        {
            if (!entries.TryGetValue(key, out Entry entry)) return;
            entries.Remove(key); // Invalidate before callbacks, OnDisable and OnDestroy can re-enter.
            objectKeys.Remove(entry.NativeId);
            if (entry.Link != null) entry.Link.Retire();
            Action detach = entry.Detach; entry.Detach = null;
            Run(() => detach?.Invoke());
            if (!destroy || entry.Object == null) return;
            Run(() =>
            {
                foreach (MonoBehaviour behaviour in entry.Object.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour != null) { behaviour.CancelInvoke(); behaviour.StopAllCoroutines(); }
            });
            Run(() => entry.Object.SetActive(false));
            Run(() =>
            {
                if (entry.Object == null) return;
                if (Application.isPlaying) UnityEngine.Object.Destroy(entry.Object);
                else UnityEngine.Object.DestroyImmediate(entry.Object);
            });
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true; // No callback may register new objects while the room is closing.
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Action closing = beforeRoomCleanup;
            beforeRoomCleanup = null; beforePlayerCleanup = null;
            Run(() => closing?.Invoke());
            foreach (long key in new List<long>(entries.Keys)) Release(key, true);
            entries.Clear(); objectKeys.Clear(); departedPlayers.Clear();
        }

        private void OnSceneUnloaded(Scene scene) { if (scene.handle == sceneHandle) Dispose(); }
        private void Run(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception error) { LastCleanupError = error; Debug.LogException(error); }
        }
    }
}
