using System;
using System.Collections.Generic;
using System.IO;
using Rpg.Networking.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rpg.Networking
{
    /// <summary>Scene-owned entityId dictionary. Clones only the RPG Player body; never duplicates singleton managers.</summary>
    public sealed class RoomPlayerEntities : IDisposable
    {
        private readonly Dictionary<int, Player> entities = new Dictionary<int, Player>();
        private readonly HashSet<int> confirmedDeaths = new HashSet<int>();
        private readonly Dictionary<int, PlayerDeathCause> deathCauses = new Dictionary<int, PlayerDeathCause>();
        private readonly Dictionary<int, PlayerDeathCause> pendingDeathCauses = new Dictionary<int, PlayerDeathCause>();
        private readonly Dictionary<int, RoomNetworkObjects.Registration> registrations = new Dictionary<int, RoomNetworkObjects.Registration>();
        private readonly RoomNetworkObjects networkObjects;
        private readonly RoomNetworkObjects.Registration rootRegistration;
        private readonly Player template;
        private readonly float teleportDistance;
        private readonly int inventoryCapacity = 24;
        private RoomMaterialDto[] startingMaterials = Array.Empty<RoomMaterialDto>();
        private RoomCraftCatalog craftCatalog;
        private RoomSkillTreeCatalog skillTreeCatalog;
        private RoomFlaskCatalog flaskCatalog;
        private readonly List<UI_SkillTreeSlot> skillTreeSlots = new List<UI_SkillTreeSlot>();
        private readonly int startingCurrency;
        private readonly int dodgeBonus;
        private readonly Inventory roomInventorySource;
        private readonly HashSet<ItemData_Equipment> roomRecipes = new HashSet<ItemData_Equipment>();
        private readonly GameObject root;
        private readonly Transform playersRoot, staging;
        private RoomPresentationContext context;
        private RoomPlayerIdentity hostIdentity, guestIdentity;
        private long nextStateSeq, appliedStateSeq;
        private ulong appliedStateTick;
        private long initializationLoadId;
        private bool disposed;
        public Transform HostSpawn { get; }
        public Transform GuestSpawn { get; }
        public IReadOnlyDictionary<int, Player> Entities => entities;
        public bool AllStarted
        {
            get
            {
                if (disposed || entities.Count != 2) return false;
                foreach (Player player in entities.Values)
                    if (player == null || !player.gameObject.activeInHierarchy || !player.IsRuntimeInitialized ||
                        player.stats == null || !player.stats.IsRuntimeInitialized) return false;
                return true;
            }
        }

        public RoomPlayerEntities(Scene scene, Player playerTemplate, Vector2 hostOffset, Vector2 guestOffset, RoomNetworkObjects networkObjects, float teleportDistance = 5f)
        {
            if (!scene.isLoaded || playerTemplate == null || playerTemplate.gameObject.scene != scene)
                throw new ArgumentException("Use the prepared MainScene RPG player template.");
            this.networkObjects = networkObjects ?? throw new ArgumentNullException(nameof(networkObjects));
            template = playerTemplate;
            this.teleportDistance = Mathf.Max(0.01f, teleportDistance);
            ValidateTemplate(template);
            foreach (var sceneRoot in scene.GetRootGameObjects())
            {
                var inventory = sceneRoot.GetComponentInChildren<Inventory>(true);
                if (inventory != null) { inventoryCapacity = inventory.RoomViewCapacity; roomInventorySource = inventory; break; }
            }
            foreach (var sceneRoot in scene.GetRootGameObjects())
            {
                skillTreeSlots.AddRange(sceneRoot.GetComponentsInChildren<UI_SkillTreeSlot>(true));
                var manager = sceneRoot.GetComponentInChildren<PlayerManager>(true);
                if (manager != null) startingCurrency = Mathf.Max(0, manager.currency);
                var dodge = sceneRoot.GetComponentInChildren<DodgeSkill>(true);
                if (dodge != null) dodgeBonus = dodge.EvasionAmount;
                foreach (var list in sceneRoot.GetComponentsInChildren<UI_CraftList>(true))
                    foreach (var product in list.CraftEquipment) if (product != null) roomRecipes.Add(product);
            }
            root = new GameObject("NetworkPlayers");
            SceneManager.MoveGameObjectToScene(root, scene);
            HostSpawn = Child("Spawn_A", root.transform);
            GuestSpawn = Child("Spawn_B", root.transform);
            HostSpawn.position = template.transform.position + (Vector3)hostOffset;
            GuestSpawn.position = template.transform.position + (Vector3)guestOffset;
            playersRoot = Child("Players", root.transform);
            staging = Child("Inactive staging", root.transform);
            staging.gameObject.SetActive(false);
            try { rootRegistration = networkObjects.Register(networkObjects.RoomSessionId, 0, root); }
            catch { DestroyObject(root); throw; }
        }

        public static Player FindSceneTemplate(Scene scene)
        {
            Player found = null;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                foreach (Player player in sceneRoot.GetComponentsInChildren<Player>(true))
                {
                    if (player.GetComponent<NetworkEntityIdentity>() != null) continue;
                    if (found != null) throw new InvalidOperationException("MainScene must have one unbound RPG player template.");
                    found = player;
                }
            if (found == null) throw new InvalidOperationException("MainScene RPG player template was not found.");
            return found;
        }

        public void EnsurePlayers(RoomPresentationContext owner, RoomPlayerIdentity host, RoomPlayerIdentity guest, PlayerInitializationDto baseline = null)
        {
            if (disposed) throw new ObjectDisposedException(nameof(RoomPlayerEntities));
            if (networkObjects.IsDisposed) throw new ObjectDisposedException(nameof(RoomNetworkObjects));
            if (owner == null || host == null || guest == null || host.RoomSessionId != owner.RoomSessionId || guest.RoomSessionId != owner.RoomSessionId)
                throw new ArgumentException("Use the current admitted room identities.");
            if (context != null && !ReferenceEquals(context, owner)) throw new InvalidOperationException("Dispose old room entities before replacing their context.");
            if (owner.IsGuest)
            {
                ProtocolCodec.ValidateMessage(baseline, true);
                var a = ProtocolCodec.FindPlayer(baseline, host.PlayerId, host.EntityId);
                var b = ProtocolCodec.FindPlayer(baseline, guest.PlayerId, guest.EntityId);
                if (baseline.roomSessionId != owner.RoomSessionId || a == null || b == null)
                    throw new InvalidDataException("Initial entities do not match admitted identities.");
                HostSpawn.position = Position(a); GuestSpawn.position = Position(b);
                initializationLoadId = baseline.loadId;
            }
            context = owner; hostIdentity = host; guestIdentity = guest;
            if (!owner.IsGuest && craftCatalog == null)
            {
                craftCatalog = new RoomCraftCatalog(roomRecipes);
                skillTreeCatalog = new RoomSkillTreeCatalog(skillTreeSlots);
                var flasks = new HashSet<ItemData_Equipment>(roomRecipes);
                if (roomInventorySource != null && roomInventorySource.itemDataBase != null)
                    foreach (var item in roomInventorySource.itemDataBase) if (item is ItemData_Equipment equipment) flasks.Add(equipment);
                flaskCatalog = new RoomFlaskCatalog(flasks);
                startingMaterials = RoomCraftCatalog.StartingMaterials(roomInventorySource != null ? roomInventorySource.startingItems : null);
            }
            EnsurePlayer(host, HostSpawn.position, "A");
            EnsurePlayer(guest, GuestSpawn.position, "B");
        }

        private void EnsurePlayer(RoomPlayerIdentity identity, Vector3 position, string slot)
        {
            if (entities.TryGetValue(identity.EntityId, out Player existing))
            {
                if (existing == null || existing.GetComponent<NetworkEntityIdentity>().PlayerId != identity.PlayerId)
                    throw new InvalidOperationException("An initialized entity ID cannot change ownership.");
                return;
            }
            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, staging);
            Player player = null;
            try
            {
                clone.SetActive(false);
                clone.name = $"Player_{slot} (player={identity.PlayerId}, entity={identity.EntityId})";
                clone.transform.SetParent(playersRoot, false);
                clone.transform.position = position;
                player = clone.GetComponent<Player>();
                player.Combat.ConfigureRoomDodgeBonus(dodgeBonus);
                player.enabled = true;
                clone.GetComponent<PlayerStats>().enabled = true;
                context.RegisterEntity(identity.EntityId, identity.PlayerId, clone.transform);
                if (context.IsGuest) clone.GetComponent<RemotePlayerView>().TeleportDistance = teleportDistance;
                clone.GetComponent<NetworkEntityIdentity>().HoldMovement();
                entities.Add(identity.EntityId, player);
                registrations.Add(identity.EntityId, networkObjects.Register(context.RoomSessionId, identity.PlayerId, clone, () =>
                {
                    try
                    {
                        player.Combat.ReleaseRoomEquipment();
                        player.Combat.PrimaryAttack?.InterruptCurrent();
                        context?.RemoveEntity(identity.EntityId);
                    }
                    finally
                    {
                        entities.Remove(identity.EntityId);
                        confirmedDeaths.Remove(identity.EntityId);
                        deathCauses.Remove(identity.EntityId);
                        pendingDeathCauses.Remove(identity.EntityId);
                        registrations.Remove(identity.EntityId);
                    }
                }));
                // Both identity and cleanup ownership must be live before authority-only configuration.
                if (!context.IsGuest)
                {
                    RoomCombatConfiguration.InitializeAuthority(player, slot == "A", inventoryCapacity, startingMaterials);
                    player.Combat.InitializeRoomSkillTree(skillTreeCatalog, startingCurrency);
                    player.Combat.BindPrimaryAttack(networkObjects);
                }
                clone.SetActive(true); // Identity, role, spawn and input hold are set before Awake/Start.
            }
            catch
            {
                if (player != null) player.Combat.ReleaseRoomEquipment();
                if (registrations.TryGetValue(identity.EntityId, out var registration)) registration.Dispose();
                else
                {
                    context.RemoveEntity(identity.EntityId);
                    entities.Remove(identity.EntityId);
                    DestroyObject(clone);
                }
                throw;
            }
        }

        public PlayerInitializationDto CaptureInitialState(long loadId, ulong authorityTick)
        {
            if (context == null || context.IsGuest || !AllStarted) throw new InvalidOperationException("A must finish both actor initializations first.");
            initializationLoadId = loadId;
            var value = new PlayerInitializationDto
            {
                roomSessionId = context.RoomSessionId, loadId = loadId, authorityTick = authorityTick, stateSeq = ++nextStateSeq,
                players = new[] { Capture(hostIdentity), Capture(guestIdentity) }
            };
            ProtocolCodec.ValidateMessage(value, true);
            return value;
        }

        private InitialPlayerDto Capture(RoomPlayerIdentity identity)
        {
            Player player = entities[identity.EntityId];
            int maximum = player.stats.GetMaxHealthValue();
            return new InitialPlayerDto
            {
                playerId = identity.PlayerId, entityId = identity.EntityId,
                position = new[] { player.transform.position.x, player.transform.position.y },
                facingDir = player.facingDir,
                movementState = player.MovementAnimation, yVelocity = player.rb.linearVelocity.y,
                health = player.stats.currentHealth, maxHealth = maximum, healthVersion = player.stats.NetworkHealthVersion, currency = player.Combat.RoomCurrency,
                deathCause = DeathCauseFor(identity.EntityId, player),
                combat = player.Combat.CaptureRoomConfiguration(), attack = CaptureAttack(player), flask = player.Combat.CaptureRoomFlaskState()
            };
        }

        public void ApplyInitialState(PlayerInitializationDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (!AllStarted || value.roomSessionId != context.RoomSessionId || value.loadId != initializationLoadId ||
                ProtocolCodec.FindPlayer(value, hostIdentity.PlayerId, hostIdentity.EntityId) == null ||
                ProtocolCodec.FindPlayer(value, guestIdentity.PlayerId, guestIdentity.EntityId) == null)
                throw new InvalidOperationException("Both assigned scene entities must finish initialization before presentation binding.");
            if (value.stateSeq <= appliedStateSeq || value.authorityTick < appliedStateTick) return;
            foreach (var state in value.players)
            {
                Player player = entities[state.entityId];
                if (!context.TryGetEntity(state.entityId, out var presentation)) throw new InvalidOperationException("Player registration was lost.");
                if (context.IsGuest)
                {
                    player.Combat.ApplyConfirmedRoomConfiguration(state.combat, value.roomSessionId, value.authorityTick, value.stateSeq);
                    presentation.ApplyPosition(value.roomSessionId, value.authorityTick, value.stateSeq, Position(state), true, state.facingDir,
                        state.movementState, state.yVelocity, playerAttack: state.attack);
                }
                if (!presentation.ApplyVersionedHealth(value.roomSessionId, value.authorityTick, state.healthVersion, state.health, state.maxHealth) ||
                    !presentation.ApplyHud(value.roomSessionId, value.authorityTick, value.stateSeq, state.currency, null, 0, RoomCombatConfiguration.HudFills(state.combat)))
                    throw new InvalidDataException("Initial state is older than the player's current presentation state.");
                presentation.ApplySkillTreePresentation(state.combat.skillTree, value.authorityTick);
                presentation.ApplyFlaskState(value.roomSessionId, value.authorityTick, state.flask);
                if (context.IsGuest && state.health == 0)
                {
                    player.stats.MarkNetworkDead();
                    deathCauses[state.entityId] = state.deathCause;
                    confirmedDeaths.Add(state.entityId);
                }
            }
            appliedStateSeq = value.stateSeq;
            appliedStateTick = value.authorityTick;
            foreach (var state in value.players)
                if (context.TryGetEntity(state.entityId, out var presentation)) presentation.MarkReady();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var registration in new List<RoomNetworkObjects.Registration>(registrations.Values)) registration.Dispose();
            rootRegistration.Dispose();
            registrations.Clear(); entities.Clear(); confirmedDeaths.Clear(); deathCauses.Clear(); pendingDeathCauses.Clear();
            context = null;
        }

        public void EnableAuthoritativeMovement()
        {
            if (disposed || context == null || context.IsGuest || appliedStateSeq == 0) return;
            foreach (Player player in entities.Values)
                player.GetComponent<NetworkEntityIdentity>().ReleaseAuthoritativeMovement();
        }

        public void AdvanceDeaths(ulong tick, AuthorityResultSequence sequence, Action<PlayerDeathDto> publish)
        {
            if (disposed || context == null || context.IsGuest || appliedStateSeq == 0) return;
            foreach (var pair in entities)
            {
                Player player = pair.Value;
                if (confirmedDeaths.Contains(pair.Key) || !player.stats.IsRuntimeInitialized || player.stats.currentHealth != 0) continue;
                PlayerDeathCause cause = DeathCauseFor(pair.Key, player);
                confirmedDeaths.Add(pair.Key);
                deathCauses[pair.Key] = cause;
                pendingDeathCauses.Remove(pair.Key);
                player.HoldNetworkAtZeroHealth();
                player.stats.MarkNetworkDead();
                player.Die();
                player.GetComponent<NetworkEntityIdentity>().HoldMovement();
                var result = new PlayerDeathDto { roomSessionId = context.RoomSessionId, authorityTick = tick,
                    resultId = sequence.Allocate(), playerId = player.Combat.PlayerId, entityId = pair.Key,
                    healthVersion = player.stats.NetworkHealthVersion, maxHealth = player.stats.GetMaxHealthValue(), cause = cause };
                publish?.Invoke(result);
            }
        }

        public bool ApplyDeath(PlayerDeathDto result, AuthorityResultReceipts receipts)
        {
            ProtocolCodec.ValidateMessage(result, true);
            if (disposed || context == null || !context.IsGuest || result.roomSessionId != context.RoomSessionId ||
                !entities.TryGetValue(result.entityId, out var player) || player.Combat.PlayerId != result.playerId ||
                !context.TryGetEntity(result.entityId, out var presentation) || presentation.PlayerId != result.playerId) return false;
            presentation.ApplyVersionedHealth(result.roomSessionId, result.authorityTick, result.healthVersion, 0, result.maxHealth);
            if (player.stats.currentHealth != 0 || !receipts.TryAccept(result)) return false;
            player.stats.MarkNetworkDead();
            confirmedDeaths.Add(result.entityId);
            deathCauses[result.entityId] = result.cause;
            return true;
        }

        public bool TryKillFromDeadZone(Player player)
        {
            if (disposed || context == null || context.IsGuest || appliedStateSeq == 0 || player == null ||
                !player.isActiveAndEnabled || !player.IsAuthoritative || player.stats.currentHealth <= 0) return false;
            var identity = player.GetComponent<NetworkEntityIdentity>();
            if (identity == null || identity.RoomSessionId != context.RoomSessionId || identity.PlayerId <= 0 ||
                !entities.TryGetValue(identity.EntityId, out var registered) || registered != player ||
                !context.TryGetEntity(identity.EntityId, out var presentation) || presentation.View != player.transform) return false;
            ulong tick = GameSimulationClock.AuthorityTick;
            long version = player.stats.ApplyAuthorityNetworkHealth(0);
            pendingDeathCauses[identity.EntityId] = PlayerDeathCause.DeadZone;
            presentation.ApplyVersionedHealth(context.RoomSessionId, tick, version, 0, player.stats.GetMaxHealthValue());
            player.HoldNetworkAtZeroHealth();
            return true;
        }

        private PlayerDeathCause DeathCauseFor(int entityId, Player player)
        {
            if (player.stats.currentHealth > 0) return PlayerDeathCause.None;
            if (deathCauses.TryGetValue(entityId, out var confirmed)) return confirmed;
            return pendingDeathCauses.TryGetValue(entityId, out var pending) ? pending : PlayerDeathCause.Combat;
        }

        public PlayerPositionsDto CapturePositions(long loadId)
        {
            if (disposed || context == null || context.IsGuest || !AllStarted || appliedStateSeq == 0)
                throw new InvalidOperationException("Only initialized A authority can capture player positions.");
            ulong tick = GameSimulationClock.AuthorityTick; // Read once for the entire snapshot.
            return new PlayerPositionsDto
            {
                roomSessionId = context.RoomSessionId, loadId = loadId, authorityTick = tick, stateSeq = ++nextStateSeq,
                players = new[] { CapturePosition(hostIdentity), CapturePosition(guestIdentity) }
            };
        }

        private PlayerPositionDto CapturePosition(RoomPlayerIdentity identity)
        {
            Player player = entities[identity.EntityId];
            Vector2 position = player.rb.position;
            var movement = player.MovementAnimation;
            return new PlayerPositionDto
            {
                entityId = identity.EntityId, position = new[] { position.x, position.y }, facingDir = player.facingDir,
                movementState = movement, yVelocity = player.rb.linearVelocity.y, attack = CaptureAttack(player), flask = player.Combat.CaptureRoomFlaskState(),
                deathCause = DeathCauseFor(identity.EntityId, player),
                deathHealthVersion = player.stats.currentHealth == 0 ? player.stats.NetworkHealthVersion : 0,
                deathMaxHealth = player.stats.currentHealth == 0 ? player.stats.GetMaxHealthValue() : 0,
                skillFills = RoomCombatConfiguration.HudFills(player.Combat.Skills),
                parrying = movement == MovementAnimationState.Parry || movement == MovementAnimationState.ParrySuccess,
                parrySucceeded = movement == MovementAnimationState.ParrySuccess, invincible = player.stats.isInvincible
            };
        }

        private static PlayerAttackDto CaptureAttack(Player player)
        {
            var attack = player.Combat.PrimaryAttack.Current;
            if (attack == null || attack.IsClosed || player.stateMachine.currentState != player.primaryAttackState ||
                GameSimulationClock.AuthorityTick >= attack.EndTick) return null;
            return new PlayerAttackDto { attackInstanceId = attack.AttackInstanceId, sourceEntityId = attack.EntityId,
                comboIndex = attack.ComboIndex, facingDir = attack.FacingDir, startTick = attack.StartTick,
                durationTicks = checked((int)(attack.EndTick - attack.StartTick)) };
        }

        public void ApplyPositions(PlayerPositionsDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (disposed || context == null || !context.IsGuest || !AllStarted || appliedStateSeq == 0 ||
                value.roomSessionId != context.RoomSessionId ||
                ProtocolCodec.FindPosition(value, hostIdentity.EntityId) == null ||
                ProtocolCodec.FindPosition(value, guestIdentity.EntityId) == null) return;
            foreach (var state in value.players)
            {
                if (!entities.TryGetValue(state.entityId, out Player player) || player == null || player.IsAuthoritative ||
                    !context.TryGetEntity(state.entityId, out var presentation)) continue;
                // Each entity's position stream decides freshness independently of health/HUD and other entities.
                bool applied = presentation.ApplyPosition(value.roomSessionId, value.authorityTick, value.stateSeq,
                    new Vector2(state.position[0], state.position[1]), false, state.facingDir, state.movementState, state.yVelocity, playerAttack: state.attack);
                if (applied)
                {
                    var display = player.GetComponent<RemotePlayerView>();
                    display.ParryActive = state.parrying;
                    display.ParrySucceeded = state.parrySucceeded;
                    player.stats.ApplyConfirmedInvincibility(state.invincible);
                    if (state.deathCause != PlayerDeathCause.None)
                    {
                        if (presentation.ApplyVersionedHealth(value.roomSessionId, value.authorityTick,
                                state.deathHealthVersion, 0, state.deathMaxHealth) && player.stats.currentHealth == 0)
                        {
                            player.stats.MarkNetworkDead();
                            confirmedDeaths.Add(state.entityId);
                            deathCauses[state.entityId] = state.deathCause;
                        }
                    }
                }
                presentation.ApplyFlaskState(value.roomSessionId, value.authorityTick, state.flask);
                presentation.ApplySkillFills(value.roomSessionId, value.authorityTick, value.stateSeq, state.skillFills);
            }
        }

        public bool ApplyGuestMovementInput(RoomPlayerIdentity source, MovementInputDto input)
        {
            if (!TryGetGuestMovementPlayer(source, out Player player) ||
                player.GetComponent<NetworkEntityIdentity>().MovementHeld) return false;
            bool applied = player.InputData.TryApplyNetworkInput(input);
            if (player.NetworkInputLocked) player.InputData.Clear();
            return applied;
        }

        public void ApplyGuestInputLock(RoomPlayerIdentity source, bool locked)
        {
            if (disposed || context == null || context.IsGuest || source == null || source.RoomSessionId != context.RoomSessionId ||
                source.PlayerId != guestIdentity.PlayerId || source.EntityId != guestIdentity.EntityId ||
                !entities.TryGetValue(source.EntityId, out Player player) || player == null || !player.IsAuthoritative || player.IsLocalInputOwner) return;
            player.SetNetworkInputLocked(locked);
        }

        // A local input and B's connection-bound input share this entry. DTO never chooses an actor.
        public void BindPrimaryAttackTargets(RoomSceneMonsters monsters)
        {
            if (disposed || context == null || context.IsGuest) return;
            foreach (var player in entities.Values) player.Combat.BindPrimaryAttackTargets(monsters);
        }

        public void BindDamageService(RoomCombatDamage damage)
        {
            if (disposed || context == null || context.IsGuest || damage == null) return;
            foreach (var player in entities.Values)
            {
                player.counterAttackState.RoomDamage = damage;
                player.Combat.PrimaryAttackHits.HitConfirmed -= damage.OnPrimaryHit;
                player.Combat.PrimaryAttackHits.HitConfirmed += damage.OnPrimaryHit;
            }
        }

        public bool RequestPrimaryAttack(RoomPlayerIdentity source, PrimaryAttackRequestDto request)
        {
            if (!TryGetAuthorityCombatPlayer(source, out Player player) || request == null ||
                player.IsLocalInputOwner != ReferenceEquals(source, hostIdentity)) return false;
            var runtime = player.Combat.PrimaryAttack;
            bool accepted = runtime.TryQueue(request.requestSeq, GameSimulationClock.AuthorityTick, player.CheckNetworkPrimaryAttack());
            if (!accepted) Debug.Log($"[PrimaryAttack] REJECT player={source.PlayerId} requestSeq={request.requestSeq} phase=request reason={runtime.LastRejection}");
            return accepted;
        }

        public EquipmentChangeResultDto RequestEquipmentChange(RoomPlayerIdentity source, EquipmentChangeRequestDto request, AuthorityResultSequence results)
        {
            if (!TryGetAuthorityCombatPlayer(source, out Player player)) return null;
            return player.Combat.ChangeRoomEquipment(request, GameSimulationClock.AuthorityTick, results);
        }
        public CraftResultDto RequestCraft(RoomPlayerIdentity source, CraftRequestDto request, AuthorityResultSequence results)
        {
            if (!TryGetAuthorityCombatPlayer(source, out Player player)) return null;
            return player.Combat.CraftRoomItem(request, craftCatalog, GameSimulationClock.AuthorityTick, results);
        }
        public SkillUnlockResultDto RequestSkillUnlock(RoomPlayerIdentity source, SkillUnlockRequestDto request, AuthorityResultSequence results)
        {
            if (!TryGetAuthorityCombatPlayer(source, out Player player)) return null;
            return player.Combat.UnlockRoomSkill(request, skillTreeCatalog, GameSimulationClock.AuthorityTick, results);
        }
        public FlaskUseResultDto RequestFlaskUse(RoomPlayerIdentity source, FlaskUseRequestDto request, AuthorityResultSequence results)
        {
            if (!TryGetAuthorityCombatPlayer(source, out Player player) || !player.isActiveAndEnabled ||
                player.GetComponent<NetworkEntityIdentity>().MovementHeld) return null;
            return player.Combat.UseRoomFlask(request, flaskCatalog, GameSimulationClock.AuthorityTick, GameSimulationClock.FixedDeltaTime, results);
        }
        private bool TryGetAuthorityCombatPlayer(RoomPlayerIdentity source, out Player player)
        {
            player = null;
            return !disposed && !networkObjects.IsDisposed && context != null && !context.IsGuest && appliedStateSeq != 0 &&
                source != null && source.RoomSessionId == context.RoomSessionId &&
                (ReferenceEquals(source, hostIdentity) || ReferenceEquals(source, guestIdentity)) &&
                entities.TryGetValue(source.EntityId, out player) && player.IsAuthoritative &&
                player.Combat.PlayerId == source.PlayerId && player.Combat.EntityId == source.EntityId;
        }
        public void RefreshAuthorityFlaskHud()
        {
            if (disposed || context == null || context.IsGuest || appliedStateSeq == 0 || GameSimulationClock.AuthorityTick == 0) return;
            foreach (var pair in entities)
                if (pair.Value != null && context.TryGetEntity(pair.Key, out var view))
                {
                    view.ApplyFlaskState(context.RoomSessionId, GameSimulationClock.AuthorityTick, pair.Value.Combat.CaptureRoomFlaskState());
                    view.ApplySkillFills(context.RoomSessionId, GameSimulationClock.AuthorityTick, ++nextStateSeq,
                        RoomCombatConfiguration.HudFills(pair.Value.Combat.Skills));
                }
        }

        public bool RequestGuestDash(RoomPlayerIdentity source, DashRequestDto request)
        {
            if (!TryGetGuestMovementPlayer(source, out Player player)) return false;
            return player.TryQueueNetworkDash(request.actionSeq, request.currentAxis);
        }

        public bool RequestGuestJump(RoomPlayerIdentity source, JumpRequestDto request)
        {
            if (!TryGetGuestMovementPlayer(source, out Player player)) return false;
            return player.TryQueueNetworkJump(request.actionSeq);
        }

        private bool TryGetGuestMovementPlayer(RoomPlayerIdentity source, out Player player)
        {
            player = null;
            return !disposed && context != null && !context.IsGuest && appliedStateSeq != 0 && source != null &&
                source.RoomSessionId == context.RoomSessionId && source.PlayerId == guestIdentity.PlayerId &&
                source.EntityId == guestIdentity.EntityId && entities.TryGetValue(source.EntityId, out player) &&
                player.IsAuthoritative && !player.IsLocalInputOwner && player.stats.currentHealth > 0;
        }

        private static Transform Child(string name, Transform parent)
        {
            var value = new GameObject(name).transform;
            value.SetParent(parent, false); return value;
        }
        private static Vector3 Position(InitialPlayerDto value) => new Vector3(value.position[0], value.position[1], 0);
        private static void DestroyObject(GameObject value)
        {
            value.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        private static void ValidateTemplate(Player value)
        {
            if (value.GetComponent<PlayerStats>() == null || value.GetComponent<Rigidbody2D>() == null || value.GetComponentInChildren<Animator>(true) == null)
                throw new InvalidOperationException("RPG Player template requires PlayerStats, Rigidbody2D and Animator.");
            foreach (MonoBehaviour component in value.GetComponentsInChildren<MonoBehaviour>(true))
                if (component is PlayerManager || component is SkillManager || component is Inventory || component is SaveManager ||
                    component is GameManager || component is AudioManager || component is UI || component is UI_MainMenu)
                    throw new InvalidOperationException("The Player template must contain the character body only, without singleton managers.");
            if (value.GetComponentInChildren<Camera>(true) != null || value.GetComponentInChildren<AudioListener>(true) != null)
                throw new InvalidOperationException("Use the scene camera; do not clone cameras with room players.");
        }
    }
}
