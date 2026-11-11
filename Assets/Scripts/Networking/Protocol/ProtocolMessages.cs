using System;

namespace Rpg.Networking.Protocol
{
    public static class ProtocolSchema
    {
        // Sole version source. Increment for incompatible DTO or semantic changes.
        // A-owned base sword requests and complete object snapshots.
        public const int SchemaVersion = 31;
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
        DashRequest = 16,
        LocalInputLock = 17,
        MonsterInitialization = 18,
        MonsterStates = 19,
        PrimaryAttackRequest = 20,
        DamageResult = 21,
        ElementStates = 22,
        EquipmentChangeRequest = 23,
        EquipmentChangeResult = 24,
        CraftRequest = 25,
        CraftResult = 26,
        SkillUnlockRequest = 27,
        SkillUnlockResult = 28,
        FlaskUseRequest = 29,
        FlaskUseResult = 30,
        SwordInput = 31,
        SwordStates = 32
    }

    public enum MovementAnimationState { Idle, Move, Jump, Fall, WallSlide, WallJump, Dash }
    public enum MonsterAiState { Idle, Patrol, Chase, Attack }

    [Serializable]
    public sealed class PlayerAttackDto
    {
        public long attackInstanceId;
        public int sourceEntityId;
        public int comboIndex;
        public int facingDir;
        public ulong startTick;
        public int durationTicks;
    }

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
        public DamageResultKind kind;
        public long effectInstanceId;
        public int effectTick;
        public long chainInstanceId;
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
        public DirectDamageDto components;
        public DamageImpactDto impact; // Null for evasion/invincibility. Never runs a second damage calculation.
    }
    [Serializable]
    public sealed class DirectDamageDto
    {
        public int physicalDamage, magicDamage, burnDamage, shockDamage; // Actual HP removed; sum equals actualDamage.
        // Raw A-owned inputs identify fire/ice/lightning/intelligence without inventing per-element resistances.
        public int rawFire, rawIce, rawLightning, rawIntelligence;
    }
    public enum DamageResultKind { Direct, Burn, ShockChain }
    [Serializable]
    public sealed class ElementStatesDto : AuthorityResultDto
    {
        public long stateSeq; // Complete elemental-state stream, including ended-state tombstones.
        public ElementStateDto[] effects;
        public ChainStrikeDto[] chains = Array.Empty<ChainStrikeDto>();
    }
    public enum ElementEffectKind { Burn, Chill, Shock }
    public enum ChainStrikePhase { Flying, Impact }
    [Serializable]
    public sealed class ChainStrikeDto
    {
        public long chainId, effectId;
        public int sourcePlayerId, sourceEntityId, originEntityId, targetEntityId;
        public ulong startTick;
        public ChainStrikePhase phase;
        public float[] position, direction;
    }
    [Serializable]
    public sealed class ElementStateDto
    {
        public long effectId;
        public ElementEffectKind kind;
        public float movementMultiplier = 1f, armorMultiplier = 1f;
        public int sourcePlayerId, sourceEntityId, targetEntityId;
        public bool active;
        public ulong startTick, endTick;
        public int intervalTicks, tickDamage;
    }
    [Serializable]
    public sealed class DamageImpactDto
    {
        public float[] position; // A's contact position, for the pooled hit effect and damage number.
        public int facingDir;
        public float[] knockbackVelocity; // A applies this once; B must never integrate this velocity.
        public int knockbackTicks;
        public int reactionTicks;
    }
    // Array order for combat stats is fixed by this schema, independent of runtime StatType.
    public enum CombatStatId { Strength, Agility, Intelligence, Vitality, Damage, CritChance, CritPower, Health, Armor, Evasion, MagicResistance, FireDamage, IceDamage, LightningDamage }
    public enum RoomEquipmentSlot { Weapon, Armor, Amulet, Flask }
    public enum RoomSkillId { Dash, Clone, Sword, Blackhole, Crystal, Parry, Dodge }

    [Serializable]
    public sealed class PlayerCombatDto
    {
        public long equipmentVersion = 1;
        public int inventoryCapacity = 24;
        public RoomEquipmentDto[] inventory = Array.Empty<RoomEquipmentDto>();
        public RoomMaterialDto[] materials = Array.Empty<RoomMaterialDto>();
        public SkillTreeStateDto skillTree = new SkillTreeStateDto();
        public string configurationId;
        public int[] stats; // A's final values, including equipment. B never reads local assets to recalculate them.
        public RoomEquipmentDto[] equipment;
        public RoomSkillDto[] skills;
    }
    [Serializable]
    public sealed class RoomEquipmentDto
    {
        public string definitionId = "", displayName = "";
        public RoomEquipmentSlot slot;
        public string configurationId; // Empty string explicitly means an empty slot.
        public int[] modifiers; // Same CombatStatId order. Data-only first-round equipment, no legacy effects.
    }
    public enum EquipmentChangeKind { Equip, Unequip }
    public enum EquipmentChangeRejection { None, StaleInventory, MissingItem, InventoryFull, PlayerUnavailable }
    [Serializable]
    public sealed class EquipmentChangeRequestDto
    {
        public string roomSessionId;
        public long requestSeq, expectedVersion;
        public EquipmentChangeKind kind;
        public RoomEquipmentSlot slot;
        public string configurationId; // Identifies an item in A's room inventory; never accepts client modifiers.
    }
    [Serializable]
    public abstract class RoomInventoryResultDto : AuthorityResultDto
    {
        public RoomFlaskStateDto flask = new RoomFlaskStateDto();
        public int playerId, entityId;
        public long requestSeq;
        public PlayerCombatDto combat;
        public int health, maxHealth;
        public long healthVersion;
    }
    [Serializable]
    public sealed class EquipmentChangeResultDto : RoomInventoryResultDto
    {
        public bool accepted;
        public EquipmentChangeRejection rejection;
    }
    [Serializable]
    public sealed class RoomMaterialDto
    {
        public string materialId, displayName;
        public int count;
    }
    public enum CraftRejection { None, StaleInventory, UnknownRecipe, MissingMaterials, InventoryFull, PlayerUnavailable }
    [Serializable]
    public sealed class CraftRequestDto
    {
        public string roomSessionId, recipeId;
        public long requestSeq, expectedVersion;
    }
    [Serializable]
    public sealed class CraftResultDto : RoomInventoryResultDto
    {
        public string recipeId, productId;
        public bool accepted;
        public CraftRejection rejection;
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
    public sealed class SkillTreeStateDto
    {
        public long version = 1;
        public int currency;
        public SkillTreeNodeDto[] nodes = Array.Empty<SkillTreeNodeDto>();
        public bool[] unlockedSkills = new bool[7];
    }
    [Serializable]
    public sealed class SkillTreeNodeDto
    {
        public string nodeId;
        public int cost;
        public bool unlocked;
    }
    public enum SkillUnlockRejection { None, StaleState, UnknownNode, AlreadyUnlocked, Prerequisite, Excluded, InsufficientCurrency, PlayerUnavailable }
    [Serializable]
    public sealed class SkillUnlockRequestDto
    {
        public string roomSessionId, nodeId;
        public long requestSeq, expectedVersion;
    }
    [Serializable]
    public sealed class SkillUnlockResultDto : AuthorityResultDto
    {
        public int playerId, entityId;
        public long requestSeq;
        public string nodeId;
        public bool accepted;
        public SkillUnlockRejection rejection;
        public SkillTreeStateDto state;
    }
    [Serializable]
    public sealed class RoomFlaskStateDto
    {
        public long version = 1, stateSeq = 1;
        public string equippedItemId = "", definitionId = "";
        public ulong cooldownStartTick, cooldownEndTick;
        public int durationTicks;
    }
    public enum FlaskUseRejection { None, PlayerUnavailable, InputLocked, NoFlask, UnsupportedFlask, Cooldown }
    public enum SwordAction { Aim, Release, Recall, Cancel, Direction }
    public enum SwordPhase { Flying, Stuck, Returning }
    [Serializable]
    public sealed class SwordInputDto
    {
        public string roomSessionId;
        public long actionSeq;
        public SwordAction action;
        public float[] direction;
    }
    [Serializable]
    public sealed class SwordOwnerDto
    {
        public int playerId, entityId;
        public bool aiming, catching;
    }
    [Serializable]
    public sealed class SwordObjectDto
    {
        public int entityId, sourcePlayerId, sourceEntityId;
        public long skillInstanceId;
        public ulong startTick, endTick;
        public SwordPhase phase;
        public float[] position, direction;
    }
    [Serializable]
    public sealed class SwordStatesDto : AuthorityResultDto
    {
        public long stateSeq;
        public SwordOwnerDto[] owners;
        public SwordObjectDto[] swords;
    }
    [Serializable]
    public sealed class FlaskUseRequestDto
    {
        public string roomSessionId;
        public long requestSeq; // Own press stream; no player ID, recovery amount or client cooldown claim.
    }
    [Serializable]
    public sealed class FlaskUseResultDto : AuthorityResultDto
    {
        public int playerId, entityId;
        public long requestSeq;
        public bool accepted;
        public FlaskUseRejection rejection;
        public int actualHealing, health, maxHealth;
        public long healthVersion;
        public RoomFlaskStateDto flask;
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
        public int targetEntityId;
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
        public int targetEntityId;
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
        public RoomFlaskStateDto flask = new RoomFlaskStateDto();
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
        public PlayerAttackDto attack;
    }

    // Connection identifies the player. No position/delta, player ID or one-shot action fields.
    [Serializable]
    public sealed class MovementInputDto
    {
        public long inputSeq;
        public float currentAxis;
        public bool slideDown;
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
        public RoomFlaskStateDto flask = new RoomFlaskStateDto();
        public int entityId;
        public float[] position;
        public int facingDir;
        public MovementAnimationState movementState;
        public float yVelocity;
        public PlayerAttackDto attack;
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
