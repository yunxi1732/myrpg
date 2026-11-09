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
            Register<WallSlideInputDto>(ProtocolId.WallSlideInput),
            Register<DashRequestDto>(ProtocolId.DashRequest),
            Register<LocalInputLockDto>(ProtocolId.LocalInputLock),
            Register<MonsterInitializationDto>(ProtocolId.MonsterInitialization),
            Register<MonsterStatesDto>(ProtocolId.MonsterStates),
            Register<PrimaryAttackRequestDto>(ProtocolId.PrimaryAttackRequest),
            Register<DamageResultDto>(ProtocolId.DamageResult)
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
            Validate(dto);
            return dto;
        }

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
            if (dto is DamageResultDto damage && ValidAuthorityResult(damage) && damage.sourcePlayerId >= 0 &&
                damage.sourceEntityId > 0 && damage.targetEntityId > 0 && damage.sourceEntityId != damage.targetEntityId &&
                damage.attackInstanceId > 0 && damage.hitSegmentId > 0 && damage.actualDamage >= 0 &&
                ValidHealth(damage.finalHealth, damage.maxHealth, damage.targetStateVersion) &&
                (long)damage.actualDamage + damage.finalHealth <= damage.maxHealth &&
                (!damage.evaded || damage.actualDamage == 0 && !damage.critical)) return;
            if (dto is PrimaryAttackRequestDto attack && attack.requestSeq > 0) return;
            if (dto is MonsterStatesDto states && ValidMonsterStates(states)) return;
            if (dto is MonsterInitializationDto monsters && ValidMonsterInitialization(monsters)) return;
            if (dto is LocalInputLockDto inputLock && inputLock.lockSeq > 0) return;
            if (dto is DashRequestDto dash && dash.actionSeq > 0 && IsValidMovementAxis(dash.currentAxis)) return;
            if (dto is WallSlideInputDto wallSlide && wallSlide.inputSeq > 0) return;
            if (dto is JumpRequestDto jump && jump.actionSeq > 0) return;
            if (dto is PlayerPositionsDto positions && Guid.TryParseExact(positions.roomSessionId, "N", out _) &&
                positions.loadId > 0 && positions.authorityTick > 0 && positions.stateSeq > 0 &&
                positions.players != null && positions.players.Length == 2 &&
                ValidPlayerPosition(positions.players[0]) && ValidPlayerPosition(positions.players[1]) &&
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
                if (monster == null || monster.entityId <= 0 || !ids.Add(monster.entityId) ||
                    string.IsNullOrEmpty(monster.sceneKey) || monster.sceneKey.Length > 1024 || !keys.Add(monster.sceneKey) ||
                    string.IsNullOrEmpty(monster.monsterType) || monster.monsterType.Length > 256 ||
                    !ValidHealth(monster.health, monster.maxHealth, monster.healthVersion) ||
                    monster.position == null || monster.position.Length != 2 || !ValidCoordinate(monster.position[0]) || !ValidCoordinate(monster.position[1]) ||
                    (monster.facingDir != -1 && monster.facingDir != 1) || !ValidMonsterAi(monster.aiState) ||
                    !ValidMonsterAction(monster.aiState, monster.moving, monster.facingDir, monster.attack, value.authorityTick)) return false;
            return true;
        }

        private static bool ValidMonsterAi(MonsterAiState state) => state >= MonsterAiState.Idle && state <= MonsterAiState.Attack;
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
                if (monster == null || monster.entityId <= 0 || !ids.Add(monster.entityId) || !ValidMonsterAi(monster.aiState) ||
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

        private static bool ValidPlayerPosition(PlayerPositionDto player) => player != null && player.entityId > 0 &&
            player.position != null && player.position.Length == 2 && ValidCoordinate(player.position[0]) && ValidCoordinate(player.position[1]) &&
            (player.facingDir == -1 || player.facingDir == 1) && IsValidMovementAnimation(player.movementState, player.yVelocity);

        public static bool IsValidMovementAnimation(MovementAnimationState state, float yVelocity) =>
            state >= MovementAnimationState.Idle && state <= MovementAnimationState.Dash && ValidCoordinate(yVelocity);

        public static bool IsValidMovementAxis(float axis) =>
            !float.IsNaN(axis) && !float.IsInfinity(axis) && axis >= -1 && axis <= 1;

        private static bool ValidInitialPlayer(InitialPlayerDto player) => player != null && player.playerId > 0 && player.entityId > 0 &&
            player.position != null && player.position.Length == 2 && ValidCoordinate(player.position[0]) && ValidCoordinate(player.position[1]) &&
            (player.facingDir == -1 || player.facingDir == 1) &&
            IsValidMovementAnimation(player.movementState, player.yVelocity) &&
            ValidHealth(player.health, player.maxHealth, player.healthVersion) && player.currency >= 0 &&
            IsValidCombat(player.combat) && player.maxHealth == player.combat.stats[(int)CombatStatId.Health] + 5 * player.combat.stats[(int)CombatStatId.Vitality];

        public static bool IsValidCombat(PlayerCombatDto value)
        {
            if (value == null || !ValidConfigurationId(value.configurationId) || !ValidStats(value.stats) ||
                value.stats[(int)CombatStatId.Health] + 5 * value.stats[(int)CombatStatId.Vitality] <= 0 ||
                value.equipment == null || value.equipment.Length != 4 || value.skills == null || value.skills.Length != 7) return false;
            var equipmentIds = new System.Collections.Generic.HashSet<RoomEquipmentSlot>();
            foreach (var item in value.equipment)
            {
                if (item == null || item.slot < RoomEquipmentSlot.Weapon || item.slot > RoomEquipmentSlot.Flask ||
                    !equipmentIds.Add(item.slot) || item.configurationId == null ||
                    item.configurationId.Length > 0 && !ValidConfigurationId(item.configurationId) || !ValidStats(item.modifiers)) return false;
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
        private static bool ValidCoordinate(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) <= 1000000;
    }
}
