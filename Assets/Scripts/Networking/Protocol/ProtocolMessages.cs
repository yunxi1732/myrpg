using System;

namespace Rpg.Networking.Protocol
{
    public static class ProtocolSchema
    {
        // Sole version source. Increment for incompatible DTO or semantic changes.
        // Both peers must support A's periodic two-player position snapshots.
        public const int SchemaVersion = 6;
    }

    public enum ProtocolId : short
    {
        HandshakeHello = 1,
        VerificationEcho = 2,
        HandshakeResult = 3,
        Heartbeat = 4,
        GuestLeave = 5,
        RoomClosed = 6,
        SceneLoad = 7,
        SceneReady = 8,
        SceneStart = 9,
        SceneFailed = 10,
        PlayerInitialization = 11,
        MovementInput = 12,
        PlayerPositions = 13
    }

    // Fixed bootstrap shape. No initializer: a missing version must remain invalid (zero).
    // Positive incompatible versions reach the future handshake handler for explicit rejection.
    [Serializable]
    public sealed class HandshakeHelloDto
    {
        public int schemaVersion;
    }

    public enum HandshakeRejection { None, VersionMismatch, RoomFull, InvalidRequest }

    [Serializable]
    public sealed class HeartbeatDto
    {
        public string roomSessionId;
        public long heartbeatSeq;
    }

    [Serializable]
    public sealed class GuestLeaveDto
    {
        public string roomSessionId;
    }

    [Serializable]
    public sealed class RoomClosedDto
    {
        public string roomSessionId;
    }

    [Serializable]
    public sealed class SceneLoadDto { public string roomSessionId; public long loadId; public string sceneName; }
    [Serializable]
    public sealed class SceneReadyDto { public string roomSessionId; public long loadId; public string sceneName; }
    [Serializable]
    public sealed class SceneStartDto { public string roomSessionId; public long loadId; public string sceneName; }
    [Serializable]
    public sealed class SceneFailedDto { public string roomSessionId; public long loadId; public string sceneName; }

    // One initial baseline, not a periodic movement/state stream. Position is exactly [x, y].
    [Serializable]
    public sealed class PlayerInitializationDto
    {
        public string roomSessionId;
        public long loadId;
        public ulong authorityTick;
        public long stateSeq;
        public InitialPlayerDto[] players;
    }

    [Serializable]
    public sealed class InitialPlayerDto
    {
        public int playerId;
        public int entityId;
        public float[] position;
        public int health;
        public int maxHealth;
        public int currency;
    }

    // Connection identifies the player. No position/delta, player ID or one-shot action fields.
    [Serializable]
    public sealed class MovementInputDto
    {
        public long inputSeq;
        public float currentAxis;
    }

    // Both entries share the snapshot's authority tick and sequence. Position is exactly [x, y].
    [Serializable]
    public sealed class PlayerPositionsDto
    {
        public string roomSessionId;
        public long loadId;
        public ulong authorityTick;
        public long stateSeq;
        public PlayerPositionDto[] players;
    }

    [Serializable]
    public sealed class PlayerPositionDto
    {
        public int entityId;
        public float[] position;
    }

    public static class RoomScene
    {
        public const string Name = "MainScene";
    }

    [Serializable]
    public sealed class HandshakeResultDto
    {
        public int schemaVersion;
        public bool accepted;
        public HandshakeRejection rejection;
        public string roomSessionId;
        public int hostPlayerId;
        public int hostEntityId;
        public int guestPlayerId;
        public int guestEntityId;
    }

    [Serializable]
    public sealed class VerificationEchoDto
    {
        public string text;
    }
}
