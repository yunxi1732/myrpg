using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Networking
{
    /// <summary>Presentation data only, not a wire DTO. Fed by future authoritative protocol handlers.</summary>
    public sealed class RoomEntityPresentation
    {
        public string RoomSessionId { get; }
        public int EntityId { get; }
        public int PlayerId { get; }
        public Transform View { get; }
        public bool IsValid { get; private set; } = true;
        public bool HasHealth { get; private set; }
        public bool HasHud { get; private set; }
        public bool HasPosition { get; private set; }
        public bool IsReady { get; private set; }
        public int Health { get; private set; }
        public int MaxHealth { get; private set; }
        public int Currency { get; private set; }
        public Sprite FlaskIcon { get; private set; }
        public float FlaskFill { get; private set; }
        private readonly float[] skillFills = new float[5];
        private ulong healthTick, hudTick;
        private long healthSeq, hudSeq;
        private ulong positionTick;
        private long positionSeq;
        public event Action Changed;

        // Position has its own per-entity stream. No arrival time or local wall clock participates.
        public bool ApplyPosition(string room, ulong authorityTick, long stateSeq, Vector2 position, bool reset = false)
        {
            if (!IsValid || View == null || room != RoomSessionId || authorityTick == 0 || stateSeq <= positionSeq ||
                HasPosition && authorityTick < positionTick ||
                float.IsNaN(position.x) || float.IsInfinity(position.x) || float.IsNaN(position.y) || float.IsInfinity(position.y)) return false;
            var display = View.GetComponent<RemotePlayerView>();
            if (display != null)
            {
                if (!display.ApplyState(authorityTick, stateSeq, position, reset || !HasPosition)) return false;
            }
            else View.position = new Vector3(position.x, position.y, View.position.z);
            positionTick = authorityTick; positionSeq = stateSeq; HasPosition = true;
            return true;
        }

        internal RoomEntityPresentation(string room, int entity, int player, Transform view)
        { RoomSessionId = room; EntityId = entity; PlayerId = player; View = view; }

        public bool ApplyHealth(string room, ulong authorityTick, long stateSeq, int health, int maximum)
        {
            if (!IsValid || room != RoomSessionId || maximum <= 0 || health < 0 || health > maximum ||
                stateSeq <= healthSeq || HasHealth && authorityTick < healthTick) return false;
            healthTick = authorityTick; healthSeq = stateSeq;
            Health = health; MaxHealth = maximum; HasHealth = true;
            NotifyChanged(); return true;
        }

        public bool ApplyHud(string room, ulong authorityTick, long stateSeq, int currency, Sprite flask, float flaskFill, float[] cooldownFills)
        {
            if (!IsValid || PlayerId == 0 || room != RoomSessionId || currency < 0 || stateSeq <= hudSeq ||
                HasHud && authorityTick < hudTick || !ValidFill(flaskFill) || cooldownFills == null || cooldownFills.Length != 5) return false;
            foreach (float fill in cooldownFills) if (!ValidFill(fill)) return false;
            hudTick = authorityTick; hudSeq = stateSeq;
            Currency = currency; FlaskIcon = flask; FlaskFill = flaskFill;
            Array.Copy(cooldownFills, skillFills, 5); HasHud = true;
            NotifyChanged(); return true;
        }

        public float SkillFill(int index) => skillFills[index];
        public void MarkReady()
        {
            if (!IsValid || IsReady) return;
            IsReady = true; NotifyChanged();
        }
        private static bool ValidFill(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1;
        private void NotifyChanged()
        {
            if (Changed == null) return;
            foreach (Action callback in Changed.GetInvocationList())
                try { callback(); }
                catch (Exception error) { Debug.LogException(error); }
        }
        internal void Invalidate()
        {
            IsValid = false;
            try { NotifyChanged(); }
            finally
            {
                Changed = null;
                if (View != null)
                    foreach (UI_HealthBar bar in View.GetComponentsInChildren<UI_HealthBar>(true)) if (bar != null) bar.BindNetwork(null);
            }
        }
    }

    /// <summary>Scene/session-owned identity registry. Register an inactive entity before activation; no spawning is done here.</summary>
    public sealed class RoomPresentationContext : IDisposable
    {
        private readonly Dictionary<int, RoomEntityPresentation> entities = new Dictionary<int, RoomEntityPresentation>();
        private readonly Dictionary<int, int> playerEntities = new Dictionary<int, int>();
        private bool disposed;
        public string RoomSessionId { get; }
        public int LocalPlayerId { get; }
        public bool IsGuest { get; }

        public RoomPresentationContext(string room, int localPlayer, bool isGuest, RoomPlayerIdentity host, RoomPlayerIdentity guest)
        {
            if (string.IsNullOrEmpty(room) || host == null || guest == null || host.RoomSessionId != room || guest.RoomSessionId != room ||
                host.PlayerId <= 0 || guest.PlayerId <= 0 || host.EntityId <= 0 || guest.EntityId <= 0 ||
                host.PlayerId == guest.PlayerId || host.EntityId == guest.EntityId ||
                localPlayer != (isGuest ? guest.PlayerId : host.PlayerId)) throw new ArgumentException("Use admitted room identities.");
            RoomSessionId = room; LocalPlayerId = localPlayer; IsGuest = isGuest;
            playerEntities.Add(host.PlayerId, host.EntityId); playerEntities.Add(guest.PlayerId, guest.EntityId);
        }

        // A keeps its local presentation when a newly admitted B replaces the old guest.
        public void UpdatePlayers(RoomPlayerIdentity host, RoomPlayerIdentity guest)
        {
            if (disposed) throw new ObjectDisposedException(nameof(RoomPresentationContext));
            var validated = new RoomPresentationContext(RoomSessionId, LocalPlayerId, IsGuest, host, guest);
            if (!playerEntities.TryGetValue(LocalPlayerId, out int localEntity) || validated.playerEntities[LocalPlayerId] != localEntity)
                throw new ArgumentException("Local identity changed within the presentation session.");
            foreach (var pair in playerEntities)
                if (!validated.playerEntities.TryGetValue(pair.Key, out int entityId) || entityId != pair.Value) RemoveEntity(pair.Value);
            playerEntities.Clear();
            playerEntities.Add(host.PlayerId, host.EntityId); playerEntities.Add(guest.PlayerId, guest.EntityId);
            validated.Dispose();
        }

        public RoomEntityPresentation RegisterEntity(int entityId, int playerId, Transform view)
        {
            if (disposed) throw new ObjectDisposedException(nameof(RoomPresentationContext));
            if (entityId <= 0 || playerId < 0 || view == null || playerId == 0 && playerEntities.ContainsValue(entityId) || playerId > 0 &&
                (!playerEntities.TryGetValue(playerId, out int expected) || expected != entityId))
                throw new ArgumentException("Entity does not match admitted player identity.");
            if (entities.TryGetValue(entityId, out var existing))
            {
                if (existing.View != view || existing.PlayerId != playerId) throw new InvalidOperationException("Entity ID already registered.");
                return existing;
            }
            var entity = new RoomEntityPresentation(RoomSessionId, entityId, playerId, view);
            var identity = view.GetComponent<NetworkEntityIdentity>();
            if (identity == null) identity = view.gameObject.AddComponent<NetworkEntityIdentity>();
            identity.Bind(this, entity);
            entities.Add(entityId, entity);
            foreach (UI_HealthBar bar in view.GetComponentsInChildren<UI_HealthBar>(true)) bar.BindNetwork(entity);
            return entity;
        }

        public RoomEntityPresentation LocalEntity => !disposed && playerEntities.TryGetValue(LocalPlayerId, out int id) &&
            entities.TryGetValue(id, out var entity) && entity.IsValid && entity.IsReady && entity.View != null ? entity : null;

        public bool TryGetEntity(int id, out RoomEntityPresentation entity)
        {
            entity = null;
            return !disposed && entities.TryGetValue(id, out entity) && entity.IsValid && entity.View != null;
        }

        public void RemoveEntity(int id)
        {
            if (!entities.TryGetValue(id, out var entity)) return;
            entities.Remove(id); entity.Invalidate();
        }

        public void RemovePlayer(int playerId)
        {
            if (!playerEntities.TryGetValue(playerId, out int entityId)) return;
            playerEntities.Remove(playerId);
            RemoveEntity(entityId);
        }

        public void RemoveRemotePlayers()
        {
            var removed = new List<int>();
            foreach (var pair in playerEntities) if (pair.Key != LocalPlayerId) removed.Add(pair.Key);
            foreach (int playerId in removed) RemovePlayer(playerId);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var removed = new List<RoomEntityPresentation>(entities.Values);
            entities.Clear(); playerEntities.Clear();
            foreach (var entity in removed) entity.Invalidate();
        }
    }
}
