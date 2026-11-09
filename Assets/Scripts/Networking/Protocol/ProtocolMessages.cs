using System;

namespace Rpg.Networking.Protocol
{
    public static class ProtocolSchema
    {
        // Sole version source. Increment for incompatible DTO or semantic changes.
        // Versioned health baselines and A-confirmed physical damage results.
        public const int SchemaVersion = 19;
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
        PlayerPositions = 13,
        JumpRequest = 14,
        WallSlideInput = 15,
        DashRequest = 16,
        LocalInputLock = 17,
        MonsterInitialization = 18,
        MonsterStates = 19,
        PrimaryAttackRequest = 20,
        DamageResult = 21
    }

    public enum MovementAnimationState { Idle, Move, Jump, Fall, WallSlide, WallJump, Dash }
    public enum MonsterAiState { Idle, Patrol, Chase, Attack }

    [Serializable]
    public abstract class AuthorityResultDto
    {
        public string roomSessionId;
        public ulong authorityTick;
        public long resultId;
    }
    [Serializable]
    public sealed class DamageResultDto : AuthorityResultDto
    {
        public int sourcePlayerId;
        public int sourceEntityId;
        public int targetEntityId;
        public long attackInstanceId;
        public int hitSegmentId;
        public int actualDamage;
        public int finalHealth;
        public int maxHealth;
        public long targetStateVersion;
        public bool critical;
        public bool evaded;
    }
    // Array order for combat stats is fixed by this schema, independent of runtime StatType.
    public enum CombatStatId { Strength, Agility, Intelligence, Vitality, Damage, CritChance, CritPower, Health, Armor, Evasion, MagicResistance, FireDamage, IceDamage, LightningDamage }
    public enum RoomEquipmentSlot { Weapon, Armor, Amulet, Flask }
    public enum RoomSkillId { Dash, Clone, Sword, Blackhole, Crystal, Parry, Dodge }

    [Serializable]
    public sealed class PlayerCombatDto
    {
        public string configurationId;
        public int[] stats; // A's final values, including equipment. B never reads local assets to recalculate them.
        public RoomEquipmentDto[] equipment;
        public RoomSkillDto[] skills;
    }
    [Serializable]
    public sealed class RoomEquipmentDto
    {
        public RoomEquipmentSlot slot;
        public string configurationId; // Empty string explicitly means an empty slot.
        public int[] modifiers; // Same CombatStatId order. Data-only first-round equipment, no legacy effects.
    }
    [Serializable]
    public sealed class RoomSkillDto
    {
        public RoomSkillId skillId;
        public bool unlocked;
        public float cooldown;
        public float remaining;
    }

    [Serializable]
    public sealed class MonsterAttackDto
    {
        public long actionId; // Monotonic per monster in this room; zero-filled metadata means no attack.
        public int actionNumber; // Skeleton's existing attack animation is action 1.
        public ulong startTick;
        public int durationTicks;
        public int facingDir;
    }

    [Serializable]
    public sealed class MonsterStatesDto
    {
        public string roomSessionId;
        public long loadId;
        public ulong authorityTick;
        public long stateSeq;
        public MonsterStateDto[] monsters;
    }
    [Serializable]
    public sealed class MonsterStateDto
    {
        public int entityId;
        public float[] position;
        public int facingDir;
        public MonsterAiState aiState;
        public bool moving;
        public bool active;
        public MonsterAttackDto attack;
    }

    [Serializable]
    public sealed class MonsterInitializationDto
    {
        public string roomSessionId;
        public long loadId;
        public ulong authorityTick;
        public long stateSeq;
        public InitialMonsterDto[] monsters;
    }

    [Serializable]
    public sealed class InitialMonsterDto
    {
        public int entityId;
        public string sceneKey;
        public string monsterType;
        public float[] position;
        public int facingDir;
        public bool active;
        public MonsterAiState aiState;
        public bool moving;
        public MonsterAttackDto attack;
        public int health;
        public int maxHealth;
        public long healthVersion;
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
        public int facingDir;
        public MovementAnimationState movementState;
        public float yVelocity;
        public int health;
        public int maxHealth;
        public long healthVersion;
        public int currency;
        public PlayerCombatDto combat;
    }

    // Connection identifies the player. No position/delta, player ID or one-shot action fields.
    [Serializable]
    public sealed class MovementInputDto
    {
        public long inputSeq;
        public float currentAxis;
    }

    [Serializable]
    public sealed class JumpRequestDto
    {
        public long actionSeq; // One press. A decides ground/wall jump; B supplies no wall side, impulse or duration.
    }

    // A dedicated per-player press stream. No actor ID, hit target, attack instance ID or resultId supplied by B.
    [Serializable]
    public sealed class PrimaryAttackRequestDto
    {
        public long requestSeq;
    }

    [Serializable]
    public sealed class DashRequestDto
    {
        public long actionSeq;
        public float currentAxis; // Input at the press; zero asks A to use its own current facing.
    }

    [Serializable]
    public sealed class LocalInputLockDto
    {
        public long lockSeq;
        public bool locked;
    }

    [Serializable]
    public sealed class WallSlideInputDto
    {
        public long inputSeq;
        public bool slideDown; // Current hold state; release=false replaces true. Never a client wall/contact claim.
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
        public int facingDir;
        public MovementAnimationState movementState;
        public float yVelocity;
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
