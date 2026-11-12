using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Rpg.Networking.Protocol
{
    /// <summary>Single DTO registry and JSON gateway. Invoke on the Unity main thread.</summary>
    public static class ProtocolCodec
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        private sealed class Entry
        {
            public readonly ProtocolId Id;
            public readonly Type Type;
            public readonly bool AllowedBeforeHandshake;
            public readonly Action<IArchitecture, object> Dispatch;
            public Entry(ProtocolId id, Type type, bool allowedBeforeHandshake, Action<IArchitecture, object> dispatch)
            {
                Id = id;
                Type = type;
                AllowedBeforeHandshake = allowedBeforeHandshake;
                Dispatch = dispatch;
            }
        }

        // Extend this sole mapping with ProtocolId + DTO. Gameplay entries default to handshake-only.
        private static readonly Entry[] Entries =
        {
            Register<HandshakeHelloDto>(ProtocolId.HandshakeHello, true),
            Register<VerificationEchoDto>(ProtocolId.VerificationEcho, true),
            Register<HandshakeResultDto>(ProtocolId.HandshakeResult, true),
            Register<HeartbeatDto>(ProtocolId.Heartbeat),
            Register<GuestLeaveDto>(ProtocolId.GuestLeave),
            Register<RoomClosedDto>(ProtocolId.RoomClosed, true),
            Register<SceneLoadDto>(ProtocolId.SceneLoad),
            Register<SceneReadyDto>(ProtocolId.SceneReady),
            Register<SceneStartDto>(ProtocolId.SceneStart),
            Register<SceneFailedDto>(ProtocolId.SceneFailed),
            Register<PlayerInitializationDto>(ProtocolId.PlayerInitialization),
            Register<MovementInputDto>(ProtocolId.MovementInput),
            Register<PlayerPositionsDto>(ProtocolId.PlayerPositions),
            Register<JumpRequestDto>(ProtocolId.JumpRequest),
            Register<DashRequestDto>(ProtocolId.DashRequest),
            Register<LocalInputLockDto>(ProtocolId.LocalInputLock),
            Register<MonsterInitializationDto>(ProtocolId.MonsterInitialization),
            Register<MonsterStatesDto>(ProtocolId.MonsterStates),
            Register<PrimaryAttackRequestDto>(ProtocolId.PrimaryAttackRequest),
            Register<DamageResultDto>(ProtocolId.DamageResult),
            Register<ElementStatesDto>(ProtocolId.ElementStates),
            Register<EquipmentChangeRequestDto>(ProtocolId.EquipmentChangeRequest),
            Register<EquipmentChangeResultDto>(ProtocolId.EquipmentChangeResult),
            Register<CraftRequestDto>(ProtocolId.CraftRequest),
            Register<CraftResultDto>(ProtocolId.CraftResult),
            Register<SkillUnlockRequestDto>(ProtocolId.SkillUnlockRequest),
            Register<SkillUnlockResultDto>(ProtocolId.SkillUnlockResult),
            Register<FlaskUseRequestDto>(ProtocolId.FlaskUseRequest),
            Register<FlaskUseResultDto>(ProtocolId.FlaskUseResult),
            Register<SwordInputDto>(ProtocolId.SwordInput),
            Register<SwordStatesDto>(ProtocolId.SwordStates),
            Register<CrystalInputDto>(ProtocolId.CrystalInput),
            Register<CrystalStatesDto>(ProtocolId.CrystalStates),
            Register<CloneStatesDto>(ProtocolId.CloneStates),
            Register<BlackholeInputDto>(ProtocolId.BlackholeInput),
            Register<BlackholeStatesDto>(ProtocolId.BlackholeStates),
            Register<ParryInputDto>(ProtocolId.ParryInput),
            Register<MonsterDeathDto>(ProtocolId.MonsterDeath),
            Register<PlayerDeathDto>(ProtocolId.PlayerDeath),
            Register<DropSpawnDto>(ProtocolId.DropSpawn),
            Register<PickupRequestDto>(ProtocolId.PickupRequest),
            Register<PickupResultDto>(ProtocolId.PickupResult),
            Register<RoomRetryReadyDto>(ProtocolId.RoomRetryReady)
        };

        private static Entry Register<T>(ProtocolId id, bool allowedBeforeHandshake = false)
        {
            return new Entry(id, typeof(T), allowedBeforeHandshake, (architecture, dto) => architecture.SendEvent<T>(dto));
        }

        // Decode, validate and route through the same registry; no second message-handler table.
        public static void DecodeAndDispatch(short protocolId, byte[] payload, IArchitecture architecture,
            bool handshakeCompleted = false)
        {
            if (architecture == null) throw new ArgumentNullException(nameof(architecture));
            object dto = Decode(protocolId, payload, handshakeCompleted);
            Find((ProtocolId)protocolId).Dispatch(architecture, dto);
        }

        // handshakeCompleted must come from local connection state, never from a received DTO.
        // This step has no connection/handshake handler; its default permits only bootstrap probes.
        public static byte[] Encode(object dto, bool handshakeCompleted = false)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            Entry entry = Find(dto.GetType());
            CheckPermission(entry, handshakeCompleted);
            Validate(dto);
            return NetPacket.Build((short)entry.Id, Utf8.GetBytes(JsonUtility.ToJson(dto)));
        }

        public static object Decode(short protocolId, byte[] payload, bool handshakeCompleted = false)
        {
            Entry entry = Find((ProtocolId)protocolId);
            CheckPermission(entry, handshakeCompleted);
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > NetPacket.MaxBodyLength - NetPacket.ProtocolIdSize)
                throw new InvalidDataException("JSON payload exceeds packet limit.");
            string json = Utf8.GetString(payload).Trim();
            if (json.Length < 2 || json[0] != '{' || json[json.Length - 1] != '}')
                throw new InvalidDataException("Protocol JSON must be an object.");
            object dto;
            try { dto = JsonUtility.FromJson(json, entry.Type); }
            catch (ArgumentException exception) { throw new InvalidDataException("Invalid protocol JSON.", exception); }
            NormalizeOptionalMetadata(dto);
            Validate(dto);
            return dto;
        }

        // Unity's inline class serialization can represent a null optional class as a default-valued object.
        // Canonicalize only completely empty metadata; partially populated/invalid actions must still be rejected.
        private static void NormalizeOptionalMetadata(object dto)
        {
            if (dto is PlayerInitializationDto initial && initial.players != null)
                foreach (var player in initial.players)
                    if (player != null && EmptyPlayerAttack(player.attack)) player.attack = null;
            if (dto is PlayerPositionsDto positions && positions.players != null)
                foreach (var player in positions.players)
                    if (player != null && EmptyPlayerAttack(player.attack)) player.attack = null;
            if (dto is DamageResultDto damage && damage.impact != null)
            {
                var impact = damage.impact;
                if (EmptyVector(impact.position) && EmptyVector(impact.knockbackVelocity) && impact.facingDir == 0 &&
                    impact.knockbackTicks == 0 && impact.reactionTicks == 0) damage.impact = null;
            }
        }
        private static bool EmptyVector(float[] value) => value == null || value.Length == 0;
        private static bool EmptyPlayerAttack(PlayerAttackDto value) => value != null && value.attackInstanceId == 0 && value.sourceEntityId == 0 &&
            value.comboIndex == 0 && value.facingDir == 0 && value.startTick == 0 && value.durationTicks == 0;

        private static Entry Find(ProtocolId id)
        {
            foreach (Entry entry in Entries) if (entry.Id == id) return entry;
            throw new InvalidDataException("Unknown protocol ID: " + (short)id);
        }

        private static Entry Find(Type type)
        {
            foreach (Entry entry in Entries) if (entry.Type == type) return entry;
            throw new InvalidDataException("Unregistered DTO: " + type.FullName);
        }

        private static void CheckPermission(Entry entry, bool handshakeCompleted)
        {
            if (!handshakeCompleted && !entry.AllowedBeforeHandshake)
                throw new InvalidDataException("Handshake required before gameplay messages.");
        }

        public static void ValidateMessage(object dto, bool handshakeCompleted = false)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            CheckPermission(Find(dto.GetType()), handshakeCompleted);
            Validate(dto);
        }

        public static InitialPlayerDto FindPlayer(PlayerInitializationDto value, int playerId, int entityId)
        {
            if (value?.players == null) return null;
            foreach (var player in value.players)
                if (player != null && player.playerId == playerId && player.entityId == entityId) return player;
            return null;
        }

        private static void Validate(object dto)
        {
            if (dto is RoomRetryReadyDto retry && ValidRetryReady(retry)) return;
            if (dto is PickupRequestDto pickupRequest && ValidPickupRequest(pickupRequest)) return;
            if (dto is PickupResultDto pickup && ValidInventoryResult(pickup) && pickup.dropId > 0 &&
                pickup.rejection >= PickupRejection.None && pickup.rejection <= PickupRejection.PlayerUnavailable &&
                pickup.accepted == (pickup.rejection == PickupRejection.None)) return;
            if (dto is DropSpawnDto drop && ValidAuthorityResult(drop) && drop.dropId > 0 &&
                drop.sourceMonsterEntityId > 0 && ValidConfigurationId(drop.configurationId) && drop.quantity == 1 &&
                drop.position != null && drop.position.Length == 2 && ValidCoordinate(drop.position[0]) && ValidCoordinate(drop.position[1]) &&
                drop.velocity != null && drop.velocity.Length == 2 && ValidCoordinate(drop.velocity[0]) && ValidCoordinate(drop.velocity[1])) return;
            if (dto is MonsterDeathDto death && ValidAuthorityResult(death) && death.entityId > 0 &&
                ValidHealth(0, death.maxHealth, death.healthVersion) &&
                (death.rewardPlayerId == 0 && death.rewardEntityId == 0 && death.rewardAmount == 0 ||
                 death.rewardPlayerId > 0 && death.rewardEntityId > 0 && death.rewardAmount >= 0 &&
                 ValidSkillTree(death.rewardState))) return;
            if (dto is PlayerDeathDto playerDeath && ValidAuthorityResult(playerDeath) && playerDeath.playerId > 0 &&
                playerDeath.entityId > 0 && ValidPlayerDeathCause(playerDeath.cause, true) &&
                ValidHealth(0, playerDeath.maxHealth, playerDeath.healthVersion)) return;
            if (dto is ParryInputDto parryInput && Guid.TryParseExact(parryInput.roomSessionId, "N", out _) && parryInput.actionSeq > 0) return;
            if (dto is BlackholeInputDto blackholeInput && Guid.TryParseExact(blackholeInput.roomSessionId, "N", out _) &&
                blackholeInput.actionSeq > 0 && blackholeInput.action >= BlackholeAction.Cast && blackholeInput.action <= BlackholeAction.Release &&
                (blackholeInput.action == BlackholeAction.Select ? blackholeInput.targetEntityId > 0 && blackholeInput.keyCode > 0 :
                    blackholeInput.targetEntityId == 0 && blackholeInput.keyCode == 0)) return;
            if (dto is BlackholeStatesDto blackholes && ValidBlackholeStates(blackholes)) return;
            if (dto is CloneStatesDto cloneStates && ValidCloneStates(cloneStates)) return;
            if (dto is CrystalInputDto crystalInput && Guid.TryParseExact(crystalInput.roomSessionId, "N", out _) && crystalInput.actionSeq > 0) return;
            if (dto is CrystalStatesDto crystalStates && ValidCrystalStates(crystalStates)) return;
            if (dto is SwordInputDto sword && Guid.TryParseExact(sword.roomSessionId, "N", out _) && sword.actionSeq > 0 &&
                sword.action >= SwordAction.Aim && sword.action <= SwordAction.Direction && ValidSwordDirection(sword.direction)) return;
            if (dto is SwordStatesDto swords && ValidSwordStates(swords)) return;
            if (dto is FlaskUseRequestDto flaskRequest && ValidFlaskRequest(flaskRequest)) return;
            if (dto is FlaskUseResultDto flask && ValidAuthorityResult(flask) && flask.playerId > 0 && flask.entityId > 0 &&
                flask.requestSeq > 0 && flask.rejection >= FlaskUseRejection.None && flask.rejection <= FlaskUseRejection.Cooldown &&
                flask.accepted == (flask.rejection == FlaskUseRejection.None) && flask.actualHealing >= 0 && flask.actualHealing <= flask.health &&
                ValidHealth(flask.health, flask.maxHealth, flask.healthVersion) && ValidFlaskState(flask.flask) && flask.flask.cooldownStartTick <= flask.authorityTick &&
                (!flask.accepted ? flask.actualHealing == 0 : flask.flask.equippedItemId.Length > 0 && flask.flask.cooldownStartTick == flask.authorityTick)) return;
            if (dto is SkillUnlockRequestDto unlock && ValidSkillUnlockRequest(unlock)) return;
            if (dto is SkillUnlockResultDto unlocked && ValidAuthorityResult(unlocked) && unlocked.playerId > 0 && unlocked.entityId > 0 &&
                unlocked.requestSeq > 0 && ValidConfigurationId(unlocked.nodeId) && ValidSkillTree(unlocked.state) &&
                unlocked.rejection >= SkillUnlockRejection.None && unlocked.rejection <= SkillUnlockRejection.PlayerUnavailable &&
                unlocked.accepted == (unlocked.rejection == SkillUnlockRejection.None) &&
                (!unlocked.accepted || Array.Exists(unlocked.state.nodes, node => node.nodeId == unlocked.nodeId && node.unlocked))) return;
            if (dto is CraftRequestDto craft && ValidCraftRequest(craft)) return;
            if (dto is CraftResultDto crafted && ValidInventoryResult(crafted) && ValidConfigurationId(crafted.recipeId) &&
                crafted.rejection >= CraftRejection.None && crafted.rejection <= CraftRejection.PlayerUnavailable &&
                crafted.accepted == (crafted.rejection == CraftRejection.None) &&
                (crafted.accepted ? ValidCraftProduct(crafted) : crafted.productId == "")) return;
            if (dto is EquipmentChangeRequestDto equipment && ValidEquipmentRequest(equipment)) return;
            if (dto is EquipmentChangeResultDto changed && ValidInventoryResult(changed) &&
                changed.rejection >= EquipmentChangeRejection.None && changed.rejection <= EquipmentChangeRejection.PlayerUnavailable &&
                changed.accepted == (changed.rejection == EquipmentChangeRejection.None)) return;
            if (dto is ElementStatesDto burns && ValidElementStates(burns)) return;
            if (dto is DamageResultDto damage && ValidAuthorityResult(damage) && damage.sourcePlayerId >= 0 &&
                damage.sourceEntityId > 0 && damage.targetEntityId > 0 && damage.sourceEntityId != damage.targetEntityId &&
                damage.attackInstanceId > 0 && damage.hitSegmentId > 0 && damage.actualDamage >= 0 && ValidDirectDamage(damage) &&
                ValidHealth(damage.finalHealth, damage.maxHealth, damage.targetStateVersion) &&
                (long)damage.actualDamage + damage.finalHealth <= damage.maxHealth &&
                (!damage.evaded || damage.actualDamage == 0 && !damage.critical && damage.impact == null) &&
                (!damage.counterAttack || damage.kind == DamageResultKind.Direct && damage.sourcePlayerId > 0 && damage.impact == null) &&
                ValidDamageImpact(damage.impact)) return;
            if (dto is PrimaryAttackRequestDto attack && attack.requestSeq > 0) return;
            if (dto is MonsterStatesDto states && ValidMonsterStates(states)) return;
            if (dto is MonsterInitializationDto monsters && ValidMonsterInitialization(monsters)) return;
            if (dto is LocalInputLockDto inputLock && inputLock.lockSeq > 0) return;
            if (dto is DashRequestDto dash && dash.actionSeq > 0 && IsValidMovementAxis(dash.currentAxis)) return;
            if (dto is JumpRequestDto jump && jump.actionSeq > 0) return;
            if (dto is PlayerPositionsDto positions && Guid.TryParseExact(positions.roomSessionId, "N", out _) &&
                positions.loadId > 0 && positions.authorityTick > 0 && positions.stateSeq > 0 &&
                positions.players != null && positions.players.Length == 2 &&
                ValidPlayerPosition(positions.players[0]) && ValidPlayerPosition(positions.players[1]) &&
                positions.players[0].flask.cooldownStartTick <= positions.authorityTick && positions.players[1].flask.cooldownStartTick <= positions.authorityTick &&
                ValidPlayerAttack(positions.players[0].attack, positions.players[0].entityId, positions.players[0].facingDir, positions.authorityTick) &&
                ValidPlayerAttack(positions.players[1].attack, positions.players[1].entityId, positions.players[1].facingDir, positions.authorityTick) &&
                positions.players[0].entityId != positions.players[1].entityId) return;
            if (dto is MovementInputDto movement && movement.inputSeq > 0 && IsValidMovementAxis(movement.currentAxis)) return;
            if (dto is HandshakeHelloDto hello && hello.schemaVersion > 0) return;
            if (dto is VerificationEchoDto echo && echo.text != null) return;
            if (dto is SceneLoadDto load && ValidScene(load.roomSessionId, load.loadId, load.sceneName)) return;
            if (dto is SceneReadyDto ready && ValidScene(ready.roomSessionId, ready.loadId, ready.sceneName)) return;
            if (dto is SceneStartDto start && ValidScene(start.roomSessionId, start.loadId, start.sceneName)) return;
            if (dto is SceneFailedDto failed && ValidScene(failed.roomSessionId, failed.loadId, failed.sceneName)) return;
            if (dto is PlayerInitializationDto initialization && Guid.TryParseExact(initialization.roomSessionId, "N", out _) &&
                initialization.loadId > 0 && initialization.authorityTick > 0 && initialization.stateSeq > 0 &&
                initialization.players != null && initialization.players.Length == 2 &&
                ValidInitialPlayer(initialization.players[0]) && ValidInitialPlayer(initialization.players[1]) &&
                initialization.players[0].flask.cooldownStartTick <= initialization.authorityTick && initialization.players[1].flask.cooldownStartTick <= initialization.authorityTick &&
                ValidPlayerAttack(initialization.players[0].attack, initialization.players[0].entityId, initialization.players[0].facingDir, initialization.authorityTick) &&
                ValidPlayerAttack(initialization.players[1].attack, initialization.players[1].entityId, initialization.players[1].facingDir, initialization.authorityTick) &&
                initialization.players[0].playerId != initialization.players[1].playerId &&
                initialization.players[0].entityId != initialization.players[1].entityId) return;
            if (dto is GuestLeaveDto leave && Guid.TryParseExact(leave.roomSessionId, "N", out _)) return;
            if (dto is RoomClosedDto closed && Guid.TryParseExact(closed.roomSessionId, "N", out _)) return;
            if (dto is HeartbeatDto heartbeat && heartbeat.heartbeatSeq > 0 &&
                Guid.TryParseExact(heartbeat.roomSessionId, "N", out _)) return;
            if (dto is HandshakeResultDto result && result.schemaVersion > 0)
            {
                if (!result.accepted && result.rejection >= HandshakeRejection.VersionMismatch &&
                    result.rejection <= HandshakeRejection.InvalidRequest) return;
                if (result.accepted && result.rejection == HandshakeRejection.None &&
                    Guid.TryParseExact(result.roomSessionId, "N", out _) && result.hostPlayerId > 0 &&
                    result.hostEntityId > 0 && result.guestPlayerId > 0 && result.guestEntityId > 0 &&
                    result.hostPlayerId != result.guestPlayerId && result.hostEntityId != result.guestEntityId) return;
            }
            throw new InvalidDataException("Missing or invalid protocol fields.");
        }

        private static bool ValidMonsterInitialization(MonsterInitializationDto value)
        {
            if (!Guid.TryParseExact(value.roomSessionId, "N", out _) || value.loadId <= 0 || value.authorityTick == 0 || value.stateSeq <= 0 ||
                value.monsters == null || value.monsters.Length > 1024) return false;
            var ids = new System.Collections.Generic.HashSet<int>();
            var keys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var monster in value.monsters)
                if (monster == null || monster.entityId <= 0 || monster.targetEntityId < 0 || !ids.Add(monster.entityId) ||
                    string.IsNullOrEmpty(monster.sceneKey) || monster.sceneKey.Length > 1024 || !keys.Add(monster.sceneKey) ||
                    string.IsNullOrEmpty(monster.monsterType) || monster.monsterType.Length > 256 ||
                    !ValidHealth(monster.health, monster.maxHealth, monster.healthVersion) || monster.dead && monster.health != 0 ||
                    monster.position == null || monster.position.Length != 2 || !ValidCoordinate(monster.position[0]) || !ValidCoordinate(monster.position[1]) ||
                    (monster.facingDir != -1 && monster.facingDir != 1) || !ValidMonsterAi(monster.aiState) ||
                    !ValidMonsterAction(monster.aiState, monster.moving, monster.facingDir, monster.attack, value.authorityTick)) return false;
            return true;
        }

        private static bool ValidMonsterAi(MonsterAiState state) => state >= MonsterAiState.Idle && state <= MonsterAiState.Stunned;
        public static bool IsValidMonsterAttack(MonsterAttackDto attack, ulong tick) => attack != null && attack.actionId > 0 &&
            attack.actionNumber == 1 && attack.startTick > 0 && attack.startTick <= tick && attack.durationTicks > 0 &&
            attack.durationTicks <= 3000 && (attack.facingDir == -1 || attack.facingDir == 1);
        private static bool ValidMonsterAction(MonsterAiState state, bool moving, int facing, MonsterAttackDto attack, ulong tick) =>
            state == MonsterAiState.Attack ? !moving && IsValidMonsterAttack(attack, tick) && attack.facingDir == facing :
            attack == null || attack.actionId == 0 && attack.actionNumber == 0 && attack.startTick == 0 && attack.durationTicks == 0 && attack.facingDir == 0;
        private static bool ValidMonsterStates(MonsterStatesDto value)
        {
            if (!Guid.TryParseExact(value.roomSessionId, "N", out _) || value.loadId <= 0 || value.authorityTick == 0 || value.stateSeq <= 0 ||
                value.monsters == null || value.monsters.Length > 1024) return false;
            var ids = new System.Collections.Generic.HashSet<int>();
            foreach (var monster in value.monsters)
                if (monster == null || monster.entityId <= 0 || monster.targetEntityId < 0 || !ids.Add(monster.entityId) || !ValidMonsterAi(monster.aiState) ||
                    monster.counterWindow && monster.aiState != MonsterAiState.Attack ||
                    monster.dead && (monster.aiState != MonsterAiState.Idle || monster.moving || monster.counterWindow || monster.targetEntityId != 0) ||
                    monster.position == null || monster.position.Length != 2 || !ValidCoordinate(monster.position[0]) || !ValidCoordinate(monster.position[1]) ||
                    (monster.facingDir != -1 && monster.facingDir != 1) ||
                    !ValidMonsterAction(monster.aiState, monster.moving, monster.facingDir, monster.attack, value.authorityTick)) return false;
            return true;
        }

        private static bool ValidScene(string room, long loadId, string scene) =>
            Guid.TryParseExact(room, "N", out _) && loadId > 0 && scene == RoomScene.Name;

        public static PlayerPositionDto FindPosition(PlayerPositionsDto value, int entityId)
        {
            if (value?.players == null) return null;
            foreach (var player in value.players)
                if (player != null && player.entityId == entityId) return player;
            return null;
        }

        private static bool ValidPlayerDeathCause(PlayerDeathCause cause, bool required) =>
            cause >= (required ? PlayerDeathCause.Combat : PlayerDeathCause.None) && cause <= PlayerDeathCause.DeadZone;

        private static bool ValidPlayerPosition(PlayerPositionDto player) => player != null && player.entityId > 0 &&
            ValidPlayerDeathCause(player.deathCause, false) &&
            (player.deathCause == PlayerDeathCause.None ? player.deathHealthVersion == 0 && player.deathMaxHealth == 0 :
                ValidHealth(0, player.deathMaxHealth, player.deathHealthVersion) && player.movementState == MovementAnimationState.Idle && player.attack == null) &&
            player.parrying == (player.movementState == MovementAnimationState.Parry || player.movementState == MovementAnimationState.ParrySuccess) &&
            player.parrySucceeded == (player.movementState == MovementAnimationState.ParrySuccess) &&
            ValidFlaskState(player.flask) &&
            ValidSkillFills(player.skillFills) &&
            player.position != null && player.position.Length == 2 && ValidCoordinate(player.position[0]) && ValidCoordinate(player.position[1]) &&
            (player.facingDir == -1 || player.facingDir == 1) && IsValidMovementAnimation(player.movementState, player.yVelocity);

        private static bool ValidSkillFills(float[] fills)
        {
            if (fills == null || fills.Length != 5) return false;
            foreach (float fill in fills)
                if (float.IsNaN(fill) || float.IsInfinity(fill) || fill < 0 || fill > 1) return false;
            return true;
        }

        public static bool IsValidMovementAnimation(MovementAnimationState state, float yVelocity) =>
            state >= MovementAnimationState.Idle && state <= MovementAnimationState.ParrySuccess && ValidCoordinate(yVelocity);

        public static bool ValidPlayerAttack(PlayerAttackDto attack, int entityId, int facing, ulong tick) => attack == null ||
            attack.attackInstanceId > 0 && entityId > 0 && attack.sourceEntityId == entityId && (facing == -1 || facing == 1) && attack.facingDir == facing &&
            attack.comboIndex >= 0 && attack.comboIndex <= 2 && attack.startTick > 0 && attack.startTick <= tick &&
            attack.durationTicks > 0 && attack.durationTicks <= 3000 && tick - attack.startTick < (ulong)attack.durationTicks;

        public static bool IsValidMovementAxis(float axis) =>
            !float.IsNaN(axis) && !float.IsInfinity(axis) && axis >= -1 && axis <= 1;

        private static bool ValidInitialPlayer(InitialPlayerDto player) => player != null && player.playerId > 0 && player.entityId > 0 &&
            ValidPlayerDeathCause(player.deathCause, false) &&
            (player.health == 0 ? player.deathCause != PlayerDeathCause.None : player.deathCause == PlayerDeathCause.None) &&
            ValidFlaskState(player.flask) &&
            player.position != null && player.position.Length == 2 && ValidCoordinate(player.position[0]) && ValidCoordinate(player.position[1]) &&
            (player.facingDir == -1 || player.facingDir == 1) &&
            IsValidMovementAnimation(player.movementState, player.yVelocity) &&
            ValidHealth(player.health, player.maxHealth, player.healthVersion) && player.currency >= 0 &&
            IsValidCombat(player.combat) && player.maxHealth == player.combat.stats[(int)CombatStatId.Health] + 5 * player.combat.stats[(int)CombatStatId.Vitality];

        public static bool IsValidCombat(PlayerCombatDto value)
        {
            if (value == null || !ValidConfigurationId(value.configurationId) || !ValidStats(value.stats) ||
                value.stats[(int)CombatStatId.Health] + 5 * value.stats[(int)CombatStatId.Vitality] <= 0 ||
                value.equipment == null || value.equipment.Length != 4 || value.skills == null || value.skills.Length != 7 ||
                value.equipmentVersion <= 0 || value.inventoryCapacity < 4 || value.inventoryCapacity > 128 ||
                value.inventory == null || value.inventory.Length > value.inventoryCapacity || !ValidMaterials(value.materials) || !ValidSkillTree(value.skillTree)) return false;
            var itemIds = new System.Collections.Generic.HashSet<string>();
            foreach (var item in value.inventory)
                if (item == null || !ValidConfigurationId(item.configurationId) || !itemIds.Add(item.configurationId) ||
                    item.slot < RoomEquipmentSlot.Weapon || item.slot > RoomEquipmentSlot.Flask || !ValidStats(item.modifiers) || !ValidItemPresentation(item)) return false;
            var equipmentIds = new System.Collections.Generic.HashSet<RoomEquipmentSlot>();
            foreach (var item in value.equipment)
            {
                if (item == null || item.slot < RoomEquipmentSlot.Weapon || item.slot > RoomEquipmentSlot.Flask ||
                    !equipmentIds.Add(item.slot) || item.configurationId == null ||
                    item.configurationId.Length > 0 && !ValidConfigurationId(item.configurationId) || !ValidStats(item.modifiers) || !ValidItemPresentation(item)) return false;
                if (item.configurationId.Length > 0 && !itemIds.Add(item.configurationId)) return false;
                for (int i = 0; i < value.stats.Length; i++)
                    if (item.modifiers[i] > value.stats[i] || item.configurationId.Length == 0 && item.modifiers[i] != 0) return false;
            }
            for (int i = 0; i < value.stats.Length; i++)
            {
                int bonus = 0;
                foreach (var item in value.equipment) bonus += item.modifiers[i];
                if (bonus > value.stats[i]) return false;
            }
            var skillIds = new System.Collections.Generic.HashSet<RoomSkillId>();
            foreach (var skill in value.skills)
                if (skill == null || skill.skillId < RoomSkillId.Dash || skill.skillId > RoomSkillId.Dodge || !skillIds.Add(skill.skillId) ||
                    !ValidCooldown(skill.cooldown) || !ValidCooldown(skill.remaining) || skill.remaining > skill.cooldown) return false;
            return true;
        }
        public static bool ValidEquipmentRequest(EquipmentChangeRequestDto value) => value != null &&
            Guid.TryParseExact(value.roomSessionId, "N", out _) && value.requestSeq > 0 && value.expectedVersion > 0 &&
            value.kind >= EquipmentChangeKind.Equip && value.kind <= EquipmentChangeKind.Unequip &&
            value.slot >= RoomEquipmentSlot.Weapon && value.slot <= RoomEquipmentSlot.Flask && ValidConfigurationId(value.configurationId);
        public static bool ValidPickupRequest(PickupRequestDto value) => value != null &&
            Guid.TryParseExact(value.roomSessionId, "N", out _) && value.requestSeq > 0 && value.dropId > 0;
        public static bool ValidRetryReady(RoomRetryReadyDto value) => value != null &&
            Guid.TryParseExact(value.roomSessionId, "N", out _) && value.loadId > 0;
        public static bool ValidCraftRequest(CraftRequestDto value) => value != null &&
            Guid.TryParseExact(value.roomSessionId, "N", out _) && value.requestSeq > 0 && value.expectedVersion > 0 && ValidConfigurationId(value.recipeId);
        public static bool ValidSkillUnlockRequest(SkillUnlockRequestDto value) => value != null &&
            Guid.TryParseExact(value.roomSessionId, "N", out _) && value.requestSeq > 0 && value.expectedVersion > 0 && ValidConfigurationId(value.nodeId);
        public static bool ValidFlaskRequest(FlaskUseRequestDto value) => value != null &&
            Guid.TryParseExact(value.roomSessionId, "N", out _) && value.requestSeq > 0;
        private static bool ValidRoomSkillObject(RoomSkillObjectDto value, System.Collections.Generic.HashSet<int> ids) =>
            value != null && value.entityId > 0 && ids.Add(value.entityId) && value.sourcePlayerId > 0 &&
            value.sourceEntityId > 0 && value.skillInstanceId > 0 && value.position != null &&
            value.position.Length == 2 && ValidCoordinate(value.position[0]) && ValidCoordinate(value.position[1]);
        private static bool ValidBlackholeStates(BlackholeStatesDto value)
        {
            if (!ValidAuthorityResult(value) || value.stateSeq <= 0 || value.blackholes == null || value.blackholes.Length > 2) return false;
            var ids = new System.Collections.Generic.HashSet<int>();
            foreach (var blackhole in value.blackholes)
            {
                if (!ValidRoomSkillObject(blackhole, ids) || !ValidCoordinate(blackhole.scale) || blackhole.scale < 0 ||
                    blackhole.prompts == null || blackhole.prompts.Length > 16) return false;
                foreach (var prompt in blackhole.prompts)
                    if (prompt == null || prompt.targetEntityId <= 0 || prompt.keyCode <= 0 || prompt.position == null ||
                        prompt.position.Length != 2 || !ValidCoordinate(prompt.position[0]) || !ValidCoordinate(prompt.position[1])) return false;
            }
            return true;
        }
        private static bool ValidCloneStates(CloneStatesDto value)
        {
            if (!ValidAuthorityResult(value) || value.stateSeq <= 0 || value.clones == null || value.clones.Length > 128) return false;
            var ids = new System.Collections.Generic.HashSet<int>();
            foreach (var clone in value.clones)
                if (!ValidRoomSkillObject(clone, ids) ||
                    clone.attackNumber < 0 || clone.attackNumber > 3 ||
                    float.IsNaN(clone.alpha) || clone.alpha < 0 || clone.alpha > 1) return false;
            return true;
        }
        private static bool ValidCrystalStates(CrystalStatesDto value)
        {
            if (!ValidAuthorityResult(value) || value.stateSeq <= 0 || value.crystals == null || value.crystals.Length > 128) return false;
            var ids = new System.Collections.Generic.HashSet<int>();
            foreach (var crystal in value.crystals)
                if (!ValidRoomSkillObject(crystal, ids) || !ValidCoordinate(crystal.scale) || crystal.scale <= 0) return false;
            return true;
        }
        private static bool ValidSwordDirection(float[] value) => value != null && value.Length == 2 && ValidCoordinate(value[0]) && ValidCoordinate(value[1]) &&
            Mathf.Abs(value[0] * value[0] + value[1] * value[1] - 1) < .01f;
        private static bool ValidSwordStates(SwordStatesDto value)
        {
            if (!ValidAuthorityResult(value) || value.stateSeq <= 0 || value.owners == null || value.owners.Length < 1 || value.owners.Length > 2 ||
                value.swords == null || value.swords.Length > 2) return false;
            var owners = new System.Collections.Generic.Dictionary<int, int>();
            var ids = new System.Collections.Generic.HashSet<int>(); var instances = new System.Collections.Generic.HashSet<long>();
            foreach (var owner in value.owners)
                if (owner == null || owner.playerId <= 0 || owner.entityId <= 0 || owners.ContainsKey(owner.playerId) || !ids.Add(owner.entityId)) return false;
                else owners.Add(owner.playerId, owner.entityId);
            var sources = new System.Collections.Generic.HashSet<int>();
            foreach (var sword in value.swords)
                if (!ValidRoomSkillObject(sword, ids) || !instances.Add(sword.skillInstanceId) ||
                    !sources.Add(sword.sourcePlayerId) || !owners.TryGetValue(sword.sourcePlayerId, out int entity) || entity != sword.sourceEntityId ||
                    sword.startTick == 0 || sword.startTick > value.authorityTick || sword.endTick <= sword.startTick ||
                    sword.phase < SwordPhase.Flying || sword.phase > SwordPhase.Returning || !ValidSwordDirection(sword.direction)) return false;
            return true;
        }
        public static bool ValidFlaskState(RoomFlaskStateDto value) => value != null && value.version > 0 && value.stateSeq > 0 &&
            value.equippedItemId != null && value.definitionId != null && value.equippedItemId.Length <= 128 && value.definitionId.Length <= 128 &&
            (value.equippedItemId.Length == 0 ? value.definitionId.Length == 0 : ValidConfigurationId(value.equippedItemId) && ValidConfigurationId(value.definitionId)) &&
            value.durationTicks >= 0 && value.cooldownEndTick >= value.cooldownStartTick &&
            value.cooldownEndTick - value.cooldownStartTick == (ulong)value.durationTicks;
        public static bool ValidSkillTree(SkillTreeStateDto value)
        {
            if (value == null || value.version <= 0 || value.currency < 0 || value.nodes == null || value.nodes.Length > 128 ||
                value.unlockedSkills == null || value.unlockedSkills.Length != 7) return false;
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var node in value.nodes)
                if (node == null || !ValidConfigurationId(node.nodeId) || !ids.Add(node.nodeId) || node.cost < 0) return false;
            return true;
        }
        private static bool ValidInventoryResult(RoomInventoryResultDto value) => ValidAuthorityResult(value) && ValidFlaskState(value.flask) &&
            value.flask.cooldownStartTick <= value.authorityTick && value.playerId > 0 && value.entityId > 0 &&
            value.requestSeq > 0 && IsValidCombat(value.combat) && ValidHealth(value.health, value.maxHealth, value.healthVersion) &&
            value.maxHealth == value.combat.stats[7] + 5 * value.combat.stats[3];
        private static bool ValidCraftProduct(CraftResultDto value)
        {
            if (!ValidConfigurationId(value.productId)) return false;
            foreach (var item in value.combat.inventory)
                if (item.configurationId == value.productId && item.definitionId == value.recipeId) return true;
            return false;
        }
        private static bool ValidItemPresentation(RoomEquipmentDto value) => value.definitionId != null && value.definitionId.Length <= 128 &&
            value.displayName != null && value.displayName.Length <= 128;
        public static bool ValidMaterials(RoomMaterialDto[] values)
        {
            if (values == null || values.Length > 128) return false;
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var value in values)
                if (value == null || !ValidConfigurationId(value.materialId) || !ids.Add(value.materialId) || value.count <= 0 || value.count > 1000000 ||
                    string.IsNullOrWhiteSpace(value.displayName) || value.displayName.Length > 128) return false;
            return true;
        }
        private static bool ValidConfigurationId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128;
        private static bool ValidStats(int[] stats)
        {
            if (stats == null || stats.Length != 14) return false;
            foreach (int stat in stats) if (stat < 0 || stat > 1000000) return false;
            return true;
        }
        private static bool ValidCooldown(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 3600;
        public static bool ValidAuthorityResult(AuthorityResultDto value) => value != null &&
            Guid.TryParseExact(value.roomSessionId, "N", out _) && value.authorityTick > 0 && value.resultId > 0;
        public static bool ValidHealth(int health, int maximum, long version) => maximum > 0 && health >= 0 && health <= maximum && version > 0;
        private static bool ValidDirectDamage(DamageResultDto result)
        {
            var value = result.components;
            return value != null && value.physicalDamage >= 0 && value.magicDamage >= 0 &&
                value.burnDamage >= 0 && value.shockDamage >= 0 && (long)value.physicalDamage + value.magicDamage + value.burnDamage + value.shockDamage == result.actualDamage &&
                (result.kind == DamageResultKind.Direct && value.burnDamage == 0 && value.shockDamage == 0 && result.chainInstanceId == 0 && result.effectInstanceId == 0 && result.effectTick == 0 ||
                 (result.kind == DamageResultKind.Burn && result.chainInstanceId == 0 && value.shockDamage == 0 ||
                  result.kind == DamageResultKind.ShockChain && result.chainInstanceId > 0 && value.burnDamage == 0) &&
                 result.effectInstanceId > 0 && result.effectTick > 0 && value.physicalDamage == 0 && value.magicDamage == 0 &&
                 !result.critical && !result.evaded && (result.kind == DamageResultKind.ShockChain || result.impact == null)) &&
                value.rawFire >= 0 && value.rawIce >= 0 && value.rawLightning >= 0 && value.rawIntelligence >= 0;
        }
        public static bool ValidDamageImpact(DamageImpactDto impact) => impact == null ||
            impact.position != null && impact.position.Length == 2 && ValidCoordinate(impact.position[0]) && ValidCoordinate(impact.position[1]) &&
            (impact.facingDir == -1 || impact.facingDir == 1) && impact.knockbackVelocity != null && impact.knockbackVelocity.Length == 2 &&
            ValidCoordinate(impact.knockbackVelocity[0]) && ValidCoordinate(impact.knockbackVelocity[1]) &&
            impact.knockbackTicks > 0 && impact.knockbackTicks <= 3000 && impact.reactionTicks > 0 && impact.reactionTicks <= 3000;
        private static bool ValidCoordinate(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) <= 1000000;
        private static bool ValidElementStates(ElementStatesDto value)
        {
            if (!ValidAuthorityResult(value) || value.stateSeq <= 0 || value.effects == null || value.effects.Length > 4096 || value.chains == null || value.chains.Length > 4096) return false;
            var targets = new System.Collections.Generic.HashSet<int>();
            foreach (var burn in value.effects)
                if (burn == null || burn.effectId <= 0 || burn.sourcePlayerId < 0 || burn.sourceEntityId <= 0 || burn.targetEntityId <= 0 ||
                    burn.sourceEntityId == burn.targetEntityId || !targets.Add(burn.targetEntityId) || burn.startTick == 0 || burn.startTick > value.authorityTick ||
                    burn.endTick <= burn.startTick || burn.intervalTicks <= 0 || burn.tickDamage < 0 || burn.active && value.authorityTick >= burn.endTick ||
                    !((burn.kind == ElementEffectKind.Burn || burn.kind == ElementEffectKind.Shock) && burn.movementMultiplier == 1 && burn.armorMultiplier == 1 ||
                      burn.kind == ElementEffectKind.Chill && burn.tickDamage == 0 && burn.movementMultiplier == DamageCalculations.ChillMultiplier && burn.armorMultiplier == DamageCalculations.ChillMultiplier)) return false;
            var chainIds = new System.Collections.Generic.HashSet<long>();
            foreach (var chain in value.chains)
                if (chain == null || chain.chainId <= 0 || !chainIds.Add(chain.chainId) || chain.effectId <= 0 || chain.sourcePlayerId <= 0 ||
                    chain.sourceEntityId <= 0 || chain.originEntityId <= 0 || chain.targetEntityId <= 0 || chain.originEntityId == chain.targetEntityId ||
                    chain.sourceEntityId == chain.targetEntityId || chain.sourceEntityId == chain.originEntityId || chain.startTick == 0 || chain.startTick > value.authorityTick ||
                    chain.phase < ChainStrikePhase.Flying || chain.phase > ChainStrikePhase.Impact || !ValidChainVector(chain.position) || !ValidChainVector(chain.direction)) return false;
            return true;
        }
        private static bool ValidChainVector(float[] value) => value != null && value.Length == 2 && ValidCoordinate(value[0]) && ValidCoordinate(value[1]);
    }
}
