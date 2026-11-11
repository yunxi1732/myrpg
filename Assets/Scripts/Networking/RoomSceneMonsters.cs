using System;
using System.Collections.Generic;
using System.IO;
using Rpg.Networking.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rpg.Networking
{
    /// <summary>Existing scene monsters only. A initializes target health and runs AI/actions; legacy stats updates/damage/drops remain held.</summary>
    public sealed class RoomSceneMonsters : IDisposable
    {
        public sealed class SceneEntry
        {
            public string Key, Type;
            public Enemy Enemy;
            public bool Active;
            public Vector2 Position;
        }
        private readonly SortedDictionary<string, SceneEntry> sceneEntries = new SortedDictionary<string, SceneEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, SceneEntry> byId = new Dictionary<int, SceneEntry>();
        private readonly List<RoomNetworkObjects.Registration> registrations = new List<RoomNetworkObjects.Registration>();
        private readonly RoomNetworkObjects objects;
        private RoomPresentationContext context;
        private long nextSeq, appliedSeq;
        private ulong appliedTick;
        private bool disposed;
        public int Count => ids.Count;

        public static SceneEntry[] Discover(Scene scene)
        {
            var entries = new List<SceneEntry>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Enemy enemy in root.GetComponentsInChildren<Enemy>(true))
                    entries.Add(new SceneEntry { Key = SceneKey(enemy.transform), Type = enemy.GetType().FullName,
                        Enemy = enemy, Active = enemy.gameObject.activeInHierarchy, Position = enemy.transform.position });
            return entries.ToArray();
        }

        private static string SceneKey(Transform value)
        {
            int ordinal = 0;
            if (value.parent == null)
            {
                foreach (GameObject root in value.gameObject.scene.GetRootGameObjects())
                { if (root.transform == value) break; if (root.name == value.name) ordinal++; }
            }
            else foreach (Transform sibling in value.parent)
            { if (sibling == value) break; if (sibling.name == value.name) ordinal++; }
            string part = Uri.EscapeDataString(value.name) + "[" + ordinal + "]";
            return value.parent == null ? part : SceneKey(value.parent) + "/" + part;
        }

        public RoomSceneMonsters(SceneEntry[] entries, RoomNetworkObjects objects)
        {
            this.objects = objects;
            foreach (SceneEntry entry in entries)
            {
                if (sceneEntries.ContainsKey(entry.Key)) throw new InvalidDataException("Ambiguous scene monster key: " + entry.Key);
                sceneEntries.Add(entry.Key, entry);
            }
        }

        public void BindAuthority(RoomPresentationContext owner, Func<int> allocateId)
        {
            if (disposed || owner == null || owner.IsGuest) throw new InvalidOperationException("Only A allocates monster IDs.");
            if (context != null) { if (!ReferenceEquals(context, owner)) throw new InvalidOperationException("Old monster room context."); return; }
            context = owner;
            foreach (var pair in sceneEntries)
            {
                int id = allocateId();
                Bind(pair.Value, id, pair.Value.Position, pair.Value.Enemy.facingDir);
                ids.Add(pair.Key, id);
                byId.Add(id, pair.Value);
            }
            Reveal();
        }

        private void Bind(SceneEntry entry, int id, Vector2 position, int facing)
        {
            Enemy enemy = entry.Enemy;
            enemy.gameObject.SetActive(false);
            // Keep legacy update/event/drop paths dormant. A initializes combat data for the room hit/result pipeline.
            enemy.enabled = false;
            foreach (CharaterStats stats in enemy.GetComponentsInChildren<CharaterStats>(true)) stats.enabled = false;
            foreach (ItemDrop drop in enemy.GetComponentsInChildren<ItemDrop>(true)) drop.enabled = false;
            context.RegisterEntity(id, 0, enemy.transform);
            registrations.Add(objects.Register(context.RoomSessionId, 0, enemy.gameObject, () => context?.RemoveEntity(id)));
            if (!context.IsGuest) enemy.GetComponent<EnemyStats>()?.InitializeAuthorityCombatStats();
            enemy.GetComponent<NetworkEntityIdentity>().HoldMovement();
            enemy.transform.position = new Vector3(position.x, position.y, enemy.transform.position.z);
            enemy.FlipController(facing);
            foreach (Animator animator in enemy.GetComponentsInChildren<Animator>(true)) animator.fireEvents = false;
        }

        public bool IsValidPrimaryAttackTarget(Enemy enemy)
        {
            if (disposed || objects.IsDisposed || context == null || context.IsGuest || enemy == null || !enemy.isActiveAndEnabled) return false;
            var identity = enemy.GetComponent<NetworkEntityIdentity>();
            var stats = enemy.GetComponent<EnemyStats>();
            return identity != null && identity.IsAuthoritative && identity.RoomSessionId == context.RoomSessionId && identity.PlayerId == 0 &&
                byId.TryGetValue(identity.EntityId, out var entry) && entry.Enemy == enemy && entry.Active &&
                stats != null && stats.IsRuntimeInitialized && !stats.isDead && stats.currentHealth > 0;
        }

        public Enemy FindShockTarget(Enemy origin)
        {
            Enemy selected = null; float nearest = float.PositiveInfinity; int selectedId = int.MaxValue;
            if (origin == null || !IsValidPrimaryAttackTarget(origin)) return null;
            foreach (var entry in byId.Values)
            {
                var enemy = entry.Enemy;
                if (enemy == origin || !IsValidPrimaryAttackTarget(enemy)) continue;
                bool inRange = false;
                foreach (var collider in enemy.GetComponentsInChildren<Collider2D>())
                    if (collider.enabled && ((Vector2)collider.ClosestPoint(origin.transform.position) - (Vector2)origin.transform.position).sqrMagnitude <= 25 * 25)
                    { inRange = true; break; }
                if (!inRange) continue;
                float distance = ((Vector2)enemy.transform.position - (Vector2)origin.transform.position).sqrMagnitude;
                int id = enemy.GetComponent<NetworkEntityIdentity>().EntityId;
                if (distance < nearest || distance == nearest && id < selectedId) { selected = enemy; nearest = distance; selectedId = id; }
            }
            return selected;
        }

        private void Reveal()
        {
            var paths = new HashSet<Transform>();
            foreach (var entry in sceneEntries.Values)
                if (entry.Active && entry.Enemy != null)
                    for (Transform current = entry.Enemy.transform; current != null; current = current.parent) paths.Add(current);
            foreach (var entry in sceneEntries.Values)
            {
                if (!entry.Active || entry.Enemy == null) continue;
                var chain = new List<Transform>();
                for (Transform current = entry.Enemy.transform; current != null; current = current.parent) chain.Add(current);
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    Transform current = chain[i];
                    if (current == entry.Enemy.transform) { current.gameObject.SetActive(true); continue; }
                    if (current.gameObject.activeSelf) continue;
                    foreach (Transform child in current) if (!paths.Contains(child)) child.gameObject.SetActive(false);
                    foreach (MonoBehaviour behaviour in current.GetComponents<MonoBehaviour>()) behaviour.enabled = false;
                    current.gameObject.SetActive(true);
                }
            }
        }

        public MonsterInitializationDto Capture(long loadId, ulong tick)
        {
            if (disposed || context == null || context.IsGuest) throw new InvalidOperationException("Bind A's monsters first.");
            var list = new List<InitialMonsterDto>();
            foreach (var pair in sceneEntries)
            {
                SceneEntry entry = pair.Value;
                var stats = entry.Enemy.GetComponent<EnemyStats>();
                if (!context.TryGetEntity(ids[pair.Key], out var healthView))
                    throw new InvalidOperationException("Registered monster presentation was lost.");
                healthView.ApplyVersionedHealth(context.RoomSessionId, tick, stats.NetworkHealthVersion, stats.currentHealth, stats.GetMaxHealthValue());
                healthView.MarkReady();
                var state = CaptureState(entry, ids[pair.Key], true);
                list.Add(new InitialMonsterDto { entityId = ids[pair.Key], sceneKey = pair.Key, monsterType = entry.Type,
                    position = state.position, facingDir = state.facingDir, targetEntityId = state.targetEntityId,
                    active = state.active, aiState = state.aiState, moving = state.moving, attack = state.attack, health = stats.currentHealth,
                    maxHealth = stats.GetMaxHealthValue(), healthVersion = stats.NetworkHealthVersion });
            }
            return new MonsterInitializationDto { roomSessionId = context.RoomSessionId, loadId = loadId,
                authorityTick = tick, stateSeq = ++nextSeq, monsters = list.ToArray() };
        }

        public bool Apply(RoomPresentationContext owner, MonsterInitializationDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (disposed || owner == null || !owner.IsGuest || value.roomSessionId != owner.RoomSessionId ||
                value.stateSeq <= appliedSeq || value.authorityTick < appliedTick) return false;
            if (value.monsters.Length != sceneEntries.Count) throw new InvalidDataException("Scene monster count differs from A.");
            // Validate the entire mapping before binding any scene object.
            foreach (var state in value.monsters)
                if (!sceneEntries.TryGetValue(state.sceneKey, out var entry) || entry.Type != state.monsterType || entry.Enemy == null ||
                    ids.TryGetValue(state.sceneKey, out int existing) && existing != state.entityId ||
                    !ids.ContainsKey(state.sceneKey) && owner.TryGetEntity(state.entityId, out _))
                    throw new InvalidDataException("Scene monster identity mismatch: " + state.sceneKey);
            if (context != null && !ReferenceEquals(context, owner)) throw new InvalidOperationException("Old monster room context.");
            context = owner;
            foreach (var state in value.monsters)
            {
                var entry = sceneEntries[state.sceneKey];
                if (!ids.ContainsKey(state.sceneKey))
                {
                    Bind(entry, state.entityId, new Vector2(state.position[0], state.position[1]), state.facingDir);
                    ids.Add(state.sceneKey, state.entityId); byId.Add(state.entityId, entry);
                }
                if (!context.TryGetEntity(state.entityId, out var presentation))
                    throw new InvalidDataException("Registered monster entity was lost: " + state.entityId);
                if (presentation.ApplyPosition(value.roomSessionId, value.authorityTick, value.stateSeq,
                        new Vector2(state.position[0], state.position[1]), true, state.facingDir,
                        state.moving ? MovementAnimationState.Move : MovementAnimationState.Idle,
                        monsterAttack: state.aiState == MonsterAiState.Attack ? state.attack : null,
                        monsterTargetEntityId: state.targetEntityId))
                {
                    entry.Active = state.active;
                    if (!state.active) entry.Enemy.gameObject.SetActive(false);
                }
                presentation.ApplyVersionedHealth(value.roomSessionId, value.authorityTick, state.healthVersion, state.health, state.maxHealth);
                presentation.MarkReady();
            }
            Reveal(); appliedSeq = value.stateSeq; appliedTick = value.authorityTick;
            return true;
        }

        public void EnableAuthorityMovement(RoomPlayerEntities players)
        {
            if (disposed || context == null || context.IsGuest) return;
            foreach (var entry in sceneEntries.Values)
            {
                if (!entry.Active || entry.Enemy == null || !(entry.Enemy is EnemySkeleton)) continue;
                entry.Enemy.ConfigureNetworkMovement(() => players.Entities.Values);
                entry.Enemy.GetComponent<NetworkEntityIdentity>().ReleaseAuthoritativeMovement();
                entry.Enemy.enabled = true;
            }
        }

        public void BindDamageService(RoomCombatDamage damage)
        {
            if (disposed || context == null || context.IsGuest || damage == null) return;
            foreach (var entry in byId.Values)
                if (entry.Enemy is EnemySkeleton skeleton)
                {
                    if (skeleton.NetworkHits == null)
                        skeleton.NetworkHits = new MonsterAttackHitDetection(skeleton, objects.AllocatePrimaryAttackInstanceId, damage.IsValidPlayerTarget);
                    skeleton.NetworkHits.HitConfirmed -= damage.OnMonsterHit;
                    skeleton.NetworkHits.HitConfirmed += damage.OnMonsterHit;
                }
        }

        private static MonsterStateDto CaptureState(SceneEntry entry, int id, bool initial)
        {
            Enemy enemy = entry.Enemy;
            var skeleton = enemy as EnemySkeleton;
            MonsterAiState aiState = skeleton != null ? skeleton.NetworkAiState : MonsterAiState.Idle;
            int targetId = 0;
            if (skeleton != null && (aiState == MonsterAiState.Chase || aiState == MonsterAiState.Attack) && skeleton.battleState.Target != null)
                targetId = skeleton.battleState.Target.GetComponent<NetworkEntityIdentity>().EntityId;
            Vector2 position = initial ? (Vector2)enemy.transform.position : enemy.rb != null ? enemy.rb.position : entry.Position;
            return new MonsterStateDto { entityId = id, position = new[] { position.x, position.y }, facingDir = enemy.facingDir,
                targetEntityId = targetId, aiState = aiState, moving = enemy.anim != null && enemy.anim.GetBool("Move"),
                counterWindow = enemy.CounterWindowOpen,
                attack = aiState == MonsterAiState.Attack ? skeleton.NetworkAttack : new MonsterAttackDto(),
                active = entry.Active && enemy.gameObject.activeInHierarchy };
        }

        public MonsterStatesDto CaptureStates(long loadId, ulong tick)
        {
            if (disposed || context == null || context.IsGuest) throw new InvalidOperationException("Only A publishes monster movement.");
            var list = new List<MonsterStateDto>();
            foreach (var pair in sceneEntries) list.Add(CaptureState(pair.Value, ids[pair.Key], false));
            return new MonsterStatesDto { roomSessionId = context.RoomSessionId, loadId = loadId, authorityTick = tick,
                stateSeq = ++nextSeq, monsters = list.ToArray() };
        }

        public void ApplyStates(MonsterStatesDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (disposed || context == null || !context.IsGuest || value.roomSessionId != context.RoomSessionId) return;
            if (value.monsters.Length != byId.Count) throw new InvalidDataException("Monster state count differs from the room manifest.");
            foreach (var state in value.monsters)
                if (!byId.ContainsKey(state.entityId)) throw new InvalidDataException("Unknown monster entity ID: " + state.entityId);
            foreach (var state in value.monsters)
            {
                var entry = byId[state.entityId];
                if (!context.TryGetEntity(state.entityId, out var presentation))
                    throw new InvalidDataException("Registered monster entity was lost: " + state.entityId);
                if (!presentation.ApplyPosition(value.roomSessionId, value.authorityTick, value.stateSeq,
                    new Vector2(state.position[0], state.position[1]), !entry.Enemy.gameObject.activeSelf, state.facingDir,
                    state.moving ? MovementAnimationState.Move : MovementAnimationState.Idle,
                    monsterAttack: state.aiState == MonsterAiState.Attack ? state.attack : null,
                    monsterTargetEntityId: state.targetEntityId)) continue;
                entry.Enemy.GetComponent<RemotePlayerView>().MonsterStunned = state.aiState == MonsterAiState.Stunned;
                entry.Enemy.ShowCounterWindow(state.counterWindow);
                entry.Enemy.gameObject.SetActive(state.active);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var entry in byId.Values)
                if (entry.Enemy is EnemySkeleton skeleton) { skeleton.NetworkHits?.Reset(); skeleton.NetworkHits = null; }
            foreach (var registration in registrations) registration.Dispose();
            registrations.Clear(); ids.Clear(); byId.Clear(); context = null;
        }
    }
}
