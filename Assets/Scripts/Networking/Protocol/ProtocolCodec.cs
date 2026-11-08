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
            Register<PlayerPositionsDto>(ProtocolId.PlayerPositions)
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
            player.position != null && player.position.Length == 2 && ValidCoordinate(player.position[0]) && ValidCoordinate(player.position[1]);

        public static bool IsValidMovementAxis(float axis) =>
            !float.IsNaN(axis) && !float.IsInfinity(axis) && axis >= -1 && axis <= 1;

        private static bool ValidInitialPlayer(InitialPlayerDto player) => player != null && player.playerId > 0 && player.entityId > 0 &&
            player.position != null && player.position.Length == 2 && ValidCoordinate(player.position[0]) && ValidCoordinate(player.position[1]) &&
            player.maxHealth > 0 && player.health >= 0 && player.health <= player.maxHealth && player.currency >= 0;
        private static bool ValidCoordinate(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) <= 1000000;
    }
}
