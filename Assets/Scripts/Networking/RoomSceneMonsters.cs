using System;
using System.Collections.Generic;
using System.IO;
using Rpg.Networking.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rpg.Networking
{
    /// <summary>Existing scene monsters and their A-owned item drops.</summary>
    public sealed class RoomSceneMonsters : IDisposable
    {
        public sealed class SceneEntry
        {
            public string Key, Type;
            public Enemy Enemy;
            public bool Active;
            public bool Dead;
            public ulong RemovalTick;
            public Vector2 Position;
            public Collider2D[] Colliders;
        }
        private readonly SortedDictionary<string, SceneEntry> sceneEntries = new SortedDictionary<string, SceneEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, SceneEntry> byId = new Dictionary<int, SceneEntry>();
        private readonly Dictionary<(int, int), List<KeyValuePair<int, SceneEntry>>> spatialCells =
            new Dictionary<(int, int), List<KeyValuePair<int, SceneEntry>>>();
        private readonly HashSet<int> spatialSeen = new HashSet<int>();
        private readonly List<KeyValuePair<int, SceneEntry>> spatialCandidates = new List<KeyValuePair<int, SceneEntry>>();
        private ulong spatialTick;
        private int spatialCount = -1;
        private const float SpatialCellSize = 25f;
        private readonly List<KeyValuePair<int, SceneEntry>> deathEntries = new List<KeyValuePair<int, SceneEntry>>();
        private readonly List<MonsterSpawnDto> snapshotSpawns = new List<MonsterSpawnDto>();
        private MonsterStatesDto stateSnapshot;
        private MonsterInitializationDto initializationSnapshot;
        private readonly MonsterStateDto initializationState = new MonsterStateDto();
        private readonly MonsterAttackDto emptyInitializationAttack = new MonsterAttackDto();
        private readonly Dictionary<int, MonsterSpawnDto> dynamicSpawns = new Dictionary<int, MonsterSpawnDto>();
        private readonly HashSet<int> stalledPositionLogged = new HashSet<int>();
        private readonly Dictionary<int, long> shownSpellActions = new Dictionary<int, long>();
        private readonly Dictionary<int, ItemObject> drops = new Dictionary<int, ItemObject>();
        private readonly Dictionary<int, RoomNetworkObjects.Registration> dropRegistrations = new Dictionary<int, RoomNetworkObjects.Registration>();
        private readonly List<RoomNetworkObjects.Registration> registrations = new List<RoomNetworkObjects.Registration>();
        private readonly RoomNetworkObjects objects;
        private RoomPresentationContext context;
        private Func<int> allocateMonsterId;
        private RoomPlayerEntities authorityPlayers;
        private RoomCombatDamage damageService;
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
            allocateMonsterId = allocateId;
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
            entry.Colliders = enemy.GetComponentsInChildren<Collider2D>(true);
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
            Vector2 center = origin.transform.position;
            CollectSpatialCandidates(center, 25f);
            foreach (var pair in spatialCandidates)
            {
                var entry = pair.Value;
                var enemy = entry.Enemy;
                if (enemy == origin || !IsValidPrimaryAttackTarget(enemy)) continue;
                bool inRange = false;
                foreach (var collider in entry.Colliders)
                    if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy &&
                        ((Vector2)collider.ClosestPoint(center) - center).sqrMagnitude <= 25 * 25)
                    { inRange = true; break; }
                if (!inRange) continue;
                float distance = ((Vector2)enemy.transform.position - center).sqrMagnitude;
                int id = pair.Key;
                if (distance < nearest || distance == nearest && id < selectedId) { selected = enemy; nearest = distance; selectedId = id; }
            }
            return selected;
        }

        public void FillBounceTargets(Vector2 center, List<Transform> targets)
        {
            targets.Clear();
            if (disposed || context == null || context.IsGuest) return;
            CollectSpatialCandidates(center, 10f);
            spatialCandidates.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var pair in spatialCandidates)
            {
                foreach (var collider in pair.Value.Colliders)
                    if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy &&
                        (Physics2D.DefaultRaycastLayers & (1 << collider.gameObject.layer)) != 0 &&
                        (Physics2D.queriesHitTriggers || !collider.isTrigger) && collider.GetComponent<Enemy>() != null &&
                        ((Vector2)collider.ClosestPoint(center) - center).sqrMagnitude <= 100f)
                        targets.Add(collider.transform);
            }
        }

        private void CollectSpatialCandidates(Vector2 center, float radius)
        {
            RebuildSpatialCells();
            spatialCandidates.Clear();
            spatialSeen.Clear();
            int minX = Mathf.FloorToInt((center.x - radius) / SpatialCellSize);
            int maxX = Mathf.FloorToInt((center.x + radius) / SpatialCellSize);
            int minY = Mathf.FloorToInt((center.y - radius) / SpatialCellSize);
            int maxY = Mathf.FloorToInt((center.y + radius) / SpatialCellSize);
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    if (spatialCells.TryGetValue((x, y), out var bucket))
                        foreach (var pair in bucket)
                            if (spatialSeen.Add(pair.Key)) spatialCandidates.Add(pair);
        }

        private void RebuildSpatialCells()
        {
            ulong tick = GameSimulationClock.AuthorityTick;
            if (spatialTick == tick && spatialCount == byId.Count) return;
            foreach (var bucket in spatialCells.Values) bucket.Clear();
            foreach (var pair in byId)
            {
                var entry = pair.Value;
                if (!entry.Active || entry.Enemy == null || !entry.Enemy.gameObject.activeInHierarchy) continue;
                foreach (var collider in entry.Colliders)
                {
                    if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                    Bounds bounds = collider.bounds;
                    int minX = Mathf.FloorToInt(bounds.min.x / SpatialCellSize);
                    int maxX = Mathf.FloorToInt(bounds.max.x / SpatialCellSize);
                    int minY = Mathf.FloorToInt(bounds.min.y / SpatialCellSize);
                    int maxY = Mathf.FloorToInt(bounds.max.y / SpatialCellSize);
                    for (int x = minX; x <= maxX; x++)
                        for (int y = minY; y <= maxY; y++)
                        {
                            var cell = (x, y);
                            if (!spatialCells.TryGetValue(cell, out var bucket))
                                spatialCells.Add(cell, bucket = new List<KeyValuePair<int, SceneEntry>>());
                            bucket.Add(pair);
                        }
                }
            }
            spatialTick = tick;
            spatialCount = byId.Count;
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
            var snapshot = initializationSnapshot ?? (initializationSnapshot = new MonsterInitializationDto());
            if (snapshot.monsters == null || snapshot.monsters.Length != sceneEntries.Count)
                snapshot.monsters = new InitialMonsterDto[sceneEntries.Count];
            int index = 0;
            foreach (var pair in sceneEntries)
            {
                SceneEntry entry = pair.Value;
                var stats = entry.Enemy.GetComponent<EnemyStats>();
                int id = ids[pair.Key];
                if (!context.TryGetEntity(id, out var healthView))
                    throw new InvalidOperationException("Registered monster presentation was lost.");
                healthView.ApplyVersionedHealth(context.RoomSessionId, tick, stats.NetworkHealthVersion, stats.currentHealth, stats.GetMaxHealthValue());
                healthView.MarkReady();
                var monster = snapshot.monsters[index] ?? (snapshot.monsters[index] = new InitialMonsterDto());
                var state = CaptureState(entry, id, true, initializationState);
                monster.entityId = id;
                monster.sceneKey = pair.Key;
                monster.monsterType = entry.Type;
                monster.position = state.position;
                monster.facingDir = state.facingDir;
                monster.targetEntityId = state.targetEntityId;
                monster.active = state.active;
                monster.dead = state.dead;
                monster.aiState = state.aiState;
                monster.moving = state.moving;
                monster.attack = state.aiState == MonsterAiState.Attack ? entry.Enemy.NetworkAttack : emptyInitializationAttack;
                monster.health = stats.currentHealth;
                monster.maxHealth = stats.GetMaxHealthValue();
                monster.healthVersion = stats.NetworkHealthVersion;
                index++;
            }
            snapshot.roomSessionId = context.RoomSessionId;
            snapshot.loadId = loadId;
            snapshot.authorityTick = tick;
            snapshot.stateSeq = ++nextSeq;
            return snapshot;
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
                    Bind(entry, state.entityId, state.position, state.facingDir);
                    ids.Add(state.sceneKey, state.entityId); byId.Add(state.entityId, entry);
                }
                if (!context.TryGetEntity(state.entityId, out var presentation))
                    throw new InvalidDataException("Registered monster entity was lost: " + state.entityId);
                if (presentation.ApplyPosition(value.roomSessionId, value.authorityTick, value.stateSeq,
                        state.position, true, state.facingDir,
                        state.moving ? MovementAnimationState.Move : MovementAnimationState.Idle,
                        monsterAttack: state.aiState == MonsterAiState.Attack ? state.attack : null,
                        monsterTargetEntityId: state.targetEntityId))
                {
                    entry.Active = state.active;
                    if (!state.active) entry.Enemy.gameObject.SetActive(false);
                    entry.Enemy.GetComponent<RemotePlayerView>()?.SetMonsterAiState(state.aiState);
                }
                presentation.ApplyVersionedHealth(value.roomSessionId, value.authorityTick, state.healthVersion, state.health, state.maxHealth);
                if (state.dead) ShowDeath(entry);
                presentation.MarkReady();
            }
            Reveal(); appliedSeq = value.stateSeq; appliedTick = value.authorityTick;
            return true;
        }

        public void EnableAuthorityMovement(RoomPlayerEntities players)
        {
            if (disposed || context == null || context.IsGuest) return;
            authorityPlayers = players;
            foreach (var entry in sceneEntries.Values)
            {
                if (!entry.Active || entry.Enemy == null) continue;
                entry.Enemy.ConfigureNetworkMovement(() => players.Entities.Values);
                entry.Enemy.GetComponent<NetworkEntityIdentity>().ReleaseAuthoritativeMovement();
                entry.Enemy.enabled = true;
            }
        }

        public void BindDamageService(RoomCombatDamage damage)
        {
            if (disposed || context == null || context.IsGuest || damage == null) return;
            damageService = damage;
            foreach (var entry in byId.Values)
                AttachDamage(entry.Enemy);
        }

        private void AttachDamage(Enemy enemy)
        {
            if (enemy == null || damageService == null) return;
            if (enemy.NetworkHits == null)
                enemy.NetworkHits = new MonsterAttackHitDetection(enemy, objects.AllocatePrimaryAttackInstanceId, damageService.IsValidPlayerTarget);
            enemy.NetworkHits.HitConfirmed -= damageService.OnMonsterHit;
            enemy.NetworkHits.HitConfirmed += damageService.OnMonsterHit;
            if (enemy is Enemy_Boss boss)
            {
                boss.NetworkSpellHit -= damageService.OnBossSpellHit;
                boss.NetworkSpellHit += damageService.OnBossSpellHit;
            }
        }

        public void HoldForRoomRetry()
        {
            foreach (var entry in byId.Values)
            {
                entry.Enemy.enabled = false;
                if (entry.Enemy.rb != null) entry.Enemy.rb.linearVelocity = Vector2.zero;
                entry.Enemy.NetworkHits?.Reset();
            }
        }

        public void ForgetPlayerTarget(int entityId)
        {
            foreach (var entry in byId.Values)
                entry.Enemy.ForgetNetworkPlayer(entityId);
        }

        private static MonsterStateDto CaptureState(SceneEntry entry, int id, bool initial, MonsterStateDto state = null)
        {
            Enemy enemy = entry.Enemy;
            bool defeated = entry.Dead || enemy.GetComponent<EnemyStats>().currentHealth == 0;
            MonsterAiState aiState = !defeated ? enemy.NetworkAiState : MonsterAiState.Idle;
            int targetId = 0;
            if ((aiState == MonsterAiState.Chase || aiState == MonsterAiState.Attack) && enemy.NetworkTarget != null)
                targetId = enemy.NetworkTarget.GetComponent<NetworkEntityIdentity>()?.EntityId ?? 0;
            Vector2 position = initial ? (Vector2)enemy.transform.position : enemy.rb != null ? enemy.rb.position : entry.Position;
            state ??= new MonsterStateDto();
            state.entityId = id;
            state.position = position;
            state.facingDir = enemy.facingDir;
            state.targetEntityId = targetId;
            state.aiState = aiState;
            state.moving = !defeated && enemy.anim != null && enemy.anim.GetBool("Move");
            state.counterWindow = !defeated && enemy.CounterWindowOpen;
            state.dead = entry.Dead;
            state.active = entry.Active && enemy.gameObject.activeInHierarchy;
            return state;
        }

        public void AdvanceDeaths(ulong tick, AuthorityResultSequence sequence, Action<MonsterDeathDto> publish)
        {
            if (disposed || context == null || context.IsGuest) return;
            deathEntries.Clear();
            foreach (var pair in byId) deathEntries.Add(pair);
            foreach (var pair in deathEntries)
            {
                var entry = pair.Value;
                if (!entry.Dead && entry.Active)
                {
                    var stats = entry.Enemy.GetComponent<EnemyStats>();
                    if (stats.IsRuntimeInitialized && stats.currentHealth == 0)
                    {
                        entry.RemovalTick = checked(tick + (ulong)Mathf.CeilToInt(5f / GameSimulationClock.FixedDeltaTime));
                        entry.Enemy.NetworkHits?.Reset(); entry.Enemy.Die();
                        ShowDeath(entry);
                        if (entry.Enemy is Enemy_slime slime)
                            slime.SpawnNetworkChildren(child => RegisterDynamicSlime(child, pair.Key, slime.facingDir));
                        var result = new MonsterDeathDto { roomSessionId = context.RoomSessionId, authorityTick = tick,
                            resultId = sequence.Allocate(), entityId = pair.Key, healthVersion = stats.NetworkHealthVersion,
                            maxHealth = stats.GetMaxHealthValue() };
                        publish?.Invoke(result);
                    }
                }
                if (entry.Dead && entry.Active && tick >= entry.RemovalTick)
                { entry.Active = false; entry.Enemy.gameObject.SetActive(false); }
            }
            deathEntries.Clear();
        }

        private void RegisterDynamicSlime(Enemy_slime child, int sourceId, int facing)
        {
            if (child == null || allocateMonsterId == null || authorityPlayers == null) return;
            int id = allocateMonsterId();
            var entry = new SceneEntry { Key = "slime:" + id, Type = child.GetType().FullName,
                Enemy = child, Active = true, Position = child.transform.position };
            Bind(entry, id, entry.Position, child.facingDir);
            byId.Add(id, entry);
            var stats = child.GetComponent<EnemyStats>();
            dynamicSpawns.Add(id, new MonsterSpawnDto { entityId = id, sourceEntityId = sourceId,
                position = new[] { entry.Position.x, entry.Position.y }, facingDir = child.facingDir,
                health = stats.currentHealth, maxHealth = stats.GetMaxHealthValue(), healthVersion = stats.NetworkHealthVersion });
            child.ConfigureNetworkMovement(() => authorityPlayers.Entities.Values);
            AttachDamage(child);
            child.GetComponent<NetworkEntityIdentity>().ReleaseAuthoritativeMovement();
            child.enabled = true;
            child.gameObject.SetActive(true);
            child.SetupSlime(facing);
        }

        public int MonsterCurrency(int entityId) => byId[entityId].Enemy.GetComponent<EnemyStats>().soulsDropAmount.GetValue();

        public bool ApplyDeath(MonsterDeathDto result, AuthorityResultReceipts receipts)
        {
            ProtocolCodec.ValidateMessage(result, true);
            if (disposed || context == null || !context.IsGuest || result.roomSessionId != context.RoomSessionId ||
                !byId.TryGetValue(result.entityId, out var entry)) return false;
            if (!context.TryGetEntity(result.entityId, out var presentation) || presentation.PlayerId != 0) return false;
            presentation.ApplyVersionedHealth(result.roomSessionId, result.authorityTick, result.healthVersion, 0, result.maxHealth);
            var stats = entry.Enemy.GetComponent<EnemyStats>();
            if (stats.currentHealth != 0 || !receipts.TryAccept(result)) return false;
            ShowDeath(entry);
            return true;
        }

        public void GenerateDrops(int monsterEntityId, ulong tick, Func<int> allocateDropId,
            AuthorityResultSequence sequence, Action<DropSpawnDto> publish)
        {
            if (disposed || context == null || context.IsGuest || !byId.TryGetValue(monsterEntityId, out var entry) || !entry.Dead)
                throw new InvalidOperationException("Only a confirmed A monster death can roll drops.");
            var source = entry.Enemy.GetComponent<ItemDrop>();
            source.SelectDrops((item, velocity) =>
            {
                Vector2 position = source.transform.position;
                var result = new DropSpawnDto { roomSessionId = context.RoomSessionId, authorityTick = tick,
                    resultId = sequence.Allocate(), dropId = allocateDropId(), sourceMonsterEntityId = monsterEntityId,
                    configurationId = item.name, quantity = 1, position = new[] { position.x, position.y },
                    velocity = new[] { velocity.x, velocity.y } };
                ProtocolCodec.ValidateMessage(result, true);
                SpawnDrop(result, source, item, false);
                publish?.Invoke(result);
            });
        }

        public bool ApplyDropSpawn(DropSpawnDto result, AuthorityResultReceipts receipts)
        {
            ProtocolCodec.ValidateMessage(result, true);
            if (disposed || context == null || !context.IsGuest || result.roomSessionId != context.RoomSessionId ||
                !byId.TryGetValue(result.sourceMonsterEntityId, out var entry) || !entry.Dead || drops.ContainsKey(result.dropId)) return false;
            var source = entry.Enemy.GetComponent<ItemDrop>();
            ItemData item = null;
            foreach (ItemData candidate in source.PossibleDrops)
                if (candidate.name == result.configurationId) { item = candidate; break; }
            if (item == null) throw new InvalidDataException("Unknown room drop configuration: " + result.configurationId);
            if (!receipts.TryAccept(result)) return false;
            SpawnDrop(result, source, item, true);
            return true;
        }

        private void SpawnDrop(DropSpawnDto result, ItemDrop source, ItemData item, bool display)
        {
            var position = new Vector3(result.position[0], result.position[1], source.transform.position.z);
            GameObject value = UnityEngine.Object.Instantiate(source.DropPrefab, position, Quaternion.identity);
            var itemObject = value.GetComponent<ItemObject>();
            itemObject.SetUpItem(item, new Vector2(result.velocity[0], result.velocity[1]));
            drops.Add(result.dropId, itemObject);
            var registration = objects.Register(context.RoomSessionId, 0, value, () => drops.Remove(result.dropId));
            dropRegistrations.Add(result.dropId, registration);
            registrations.Add(registration);
            Debug.Log($"[RoomDrop] {(display ? "B" : "A")} dropId={result.dropId} monster={result.sourceMonsterEntityId} item={result.configurationId} count={result.quantity} position={result.position[0]},{result.position[1]} velocity={result.velocity[0]},{result.velocity[1]} resultId={result.resultId} tick={result.authorityTick}");
        }

        public bool TryGetDropId(ItemObject item, out int dropId)
        {
            foreach (var pair in drops)
                if (pair.Value == item) { dropId = pair.Key; return true; }
            dropId = 0;
            return false;
        }

        public bool TryGetDropItem(int dropId, out ItemData item)
        {
            if (drops.TryGetValue(dropId, out var value)) { item = value.ItemData; return true; }
            item = null;
            return false;
        }

        public bool RemoveDrop(int dropId)
        {
            if (!dropRegistrations.TryGetValue(dropId, out var registration)) return false;
            dropRegistrations.Remove(dropId);
            registrations.Remove(registration);
            registration.Dispose();
            return true;
        }

        private static void ShowDeath(SceneEntry entry)
        {
            if (entry.Dead) return;
            entry.Dead = true;
            var enemy = entry.Enemy;
            enemy.GetComponent<EnemyStats>().MarkNetworkDead();
            var display = enemy.GetComponent<RemotePlayerView>();
            if (display != null && display.MonsterStunned)
            {
                enemy.GetComponentInChildren<EntityFX>(true).EndStunBlink();
                display.MonsterStunned = false;
            }
            var feedback = enemy.GetComponent<NetworkHitFeedback>();
            if (feedback != null) feedback.enabled = false;
            enemy.NetworkHits?.Reset();
            enemy.cd.enabled = false;
            enemy.anim.speed = 0;
            var healthBar = enemy.GetComponentInChildren<UI_HealthBar>(true);
            if (healthBar != null) healthBar.gameObject.SetActive(false);
            if (enemy is Enemy_slime)
            {
                var sprite = enemy.GetComponentInChildren<SpriteRenderer>(true);
                if (sprite != null) sprite.enabled = false;
            }
        }

        public MonsterStatesDto CaptureStates(long loadId, ulong tick)
        {
            if (disposed || context == null || context.IsGuest) throw new InvalidOperationException("Only A publishes monster movement.");
            var snapshot = stateSnapshot ?? (stateSnapshot = new MonsterStatesDto());
            int attackCount = 0, spellCount = 0;
            foreach (var pair in byId)
            {
                if (pair.Value.Enemy.NetworkAiState == MonsterAiState.Attack && !pair.Value.Dead &&
                    pair.Value.Enemy.GetComponent<EnemyStats>().currentHealth != 0) attackCount++;
                if (pair.Value.Enemy is Enemy_Boss boss && boss.NetworkSpell != null &&
                    tick >= boss.NetworkSpell.startTick && tick - boss.NetworkSpell.startTick < 100) spellCount++;
            }
            if (snapshot.monsters == null || snapshot.monsters.Length != byId.Count) snapshot.monsters = new MonsterStateDto[byId.Count];
            if (snapshot.attacks == null || snapshot.attacks.Length != attackCount) snapshot.attacks = new MonsterAttackStateDto[attackCount];
            if (snapshot.spells == null || snapshot.spells.Length != spellCount) snapshot.spells = new MonsterSpellStateDto[spellCount];
            if (snapshot.spawns == null || snapshot.spawns.Length != dynamicSpawns.Count) snapshot.spawns = new MonsterSpawnDto[dynamicSpawns.Count];
            int monsterIndex = 0, attackIndex = 0, spellIndex = 0;
            foreach (var pair in byId)
            {
                var state = snapshot.monsters[monsterIndex] ?? (snapshot.monsters[monsterIndex] = new MonsterStateDto());
                CaptureState(pair.Value, pair.Key, false, state);
                monsterIndex++;
                if (state.aiState == MonsterAiState.Attack)
                {
                    var attack = snapshot.attacks[attackIndex] ?? (snapshot.attacks[attackIndex] = new MonsterAttackStateDto());
                    attack.entityId = state.entityId;
                    attack.attack = pair.Value.Enemy.NetworkAttack;
                    attackIndex++;
                }
                if (pair.Value.Enemy is Enemy_Boss boss && boss.NetworkSpell != null &&
                    tick >= boss.NetworkSpell.startTick && tick - boss.NetworkSpell.startTick < 100)
                {
                    var spell = snapshot.spells[spellIndex] ?? (snapshot.spells[spellIndex] = new MonsterSpellStateDto());
                    spell.entityId = state.entityId;
                    spell.spell = boss.NetworkSpell;
                    spellIndex++;
                }
            }
            snapshotSpawns.Clear();
            snapshotSpawns.AddRange(dynamicSpawns.Values);
            snapshotSpawns.Sort((a, b) => a.entityId.CompareTo(b.entityId));
            snapshotSpawns.CopyTo(snapshot.spawns);
            snapshot.roomSessionId = context.RoomSessionId;
            snapshot.loadId = loadId;
            snapshot.authorityTick = tick;
            snapshot.stateSeq = ++nextSeq;
            return snapshot;
        }

        public void ApplyStates(MonsterStatesDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (disposed || context == null || !context.IsGuest || value.roomSessionId != context.RoomSessionId) return;
            foreach (var spawn in value.spawns)
            {
                if (byId.ContainsKey(spawn.entityId)) continue;
                if (!byId.TryGetValue(spawn.sourceEntityId, out var source) || !(source.Enemy is Enemy_slime parent) ||
                    parent.NetworkChildPrefab == null) throw new InvalidDataException("Unknown slime spawn source: " + spawn.sourceEntityId);
                var child = UnityEngine.Object.Instantiate(parent.NetworkChildPrefab,
                    new Vector3(spawn.position[0], spawn.position[1]), Quaternion.identity).GetComponent<Enemy_slime>();
                var entry = new SceneEntry { Key = "slime:" + spawn.entityId, Type = child.GetType().FullName,
                    Enemy = child, Active = true, Position = child.transform.position };
                Bind(entry, spawn.entityId, entry.Position, spawn.facingDir);
                byId.Add(spawn.entityId, entry);
                dynamicSpawns.Add(spawn.entityId, spawn);
                if (context.TryGetEntity(spawn.entityId, out var childView))
                { childView.ApplyVersionedHealth(value.roomSessionId, value.authorityTick,
                    spawn.healthVersion, spawn.health, spawn.maxHealth); childView.MarkReady(); }
            }
            var attacks = new Dictionary<int, MonsterAttackDto>();
            foreach (var action in value.attacks) attacks.Add(action.entityId, action.attack);
            foreach (var state in value.monsters)
                if (!byId.ContainsKey(state.entityId)) throw new InvalidDataException("Unknown monster entity ID: " + state.entityId);
            foreach (var state in value.monsters)
            {
                var entry = byId[state.entityId];
                if (state.dead) ShowDeath(entry);
                if (!context.TryGetEntity(state.entityId, out var presentation))
                    throw new InvalidDataException("Registered monster entity was lost: " + state.entityId);
                if (!presentation.ApplyPosition(value.roomSessionId, value.authorityTick, value.stateSeq,
                    state.position, !entry.Enemy.gameObject.activeSelf, state.facingDir,
                    state.moving ? MovementAnimationState.Move : MovementAnimationState.Idle,
                    monsterAttack: attacks.TryGetValue(state.entityId, out var attack) ? attack : null,
                    monsterTargetEntityId: state.targetEntityId))
                {
                    if (stalledPositionLogged.Add(state.entityId))
                        Debug.LogWarning($"[RoomMonsterSync] Position rejected entity={state.entityId} tick={value.authorityTick} seq={value.stateSeq} attack={(attack == null ? 0 : attack.actionId)}");
                    if (!state.active) entry.Enemy.gameObject.SetActive(false);
                    continue;
                }
                stalledPositionLogged.Remove(state.entityId);
                var display = entry.Enemy.GetComponent<RemotePlayerView>();
                display?.SetMonsterAiState(state.aiState);
                bool stunned = !entry.Dead && state.aiState == MonsterAiState.Stunned;
                if (stunned != display.MonsterStunned)
                {
                    var fx = entry.Enemy.GetComponentInChildren<EntityFX>(true);
                    if (stunned) fx.BeginStunBlink();
                    else fx.EndStunBlink();
                    display.MonsterStunned = stunned;
                }
                if (!entry.Dead) entry.Enemy.ShowCounterWindow(state.counterWindow);
                entry.Enemy.gameObject.SetActive(state.active);
            }
            foreach (var action in value.spells)
            {
                if (!byId.TryGetValue(action.entityId, out var entry) || !(entry.Enemy is Enemy_Boss boss) || entry.Dead) continue;
                if (shownSpellActions.TryGetValue(action.entityId, out long shown) && shown >= action.spell.actionId) continue;
                shownSpellActions[action.entityId] = action.spell.actionId;
                boss.SpawnNetworkSpellVisual(new Vector2(action.spell.position[0], action.spell.position[1]));
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var entry in byId.Values)
                { entry.Enemy.NetworkHits?.Reset(); entry.Enemy.NetworkHits = null; }
            foreach (var registration in registrations) registration.Dispose();
            registrations.Clear(); ids.Clear(); byId.Clear(); deathEntries.Clear(); snapshotSpawns.Clear(); spatialCells.Clear(); spatialSeen.Clear(); spatialCandidates.Clear(); stateSnapshot = null; initializationSnapshot = null; dynamicSpawns.Clear(); shownSpellActions.Clear(); drops.Clear(); dropRegistrations.Clear(); context = null;
        }
    }
}
