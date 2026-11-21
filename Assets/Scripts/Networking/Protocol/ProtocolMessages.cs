using System;
using UnityEngine;

namespace Rpg.Networking.Protocol
{
    public static class ProtocolSchema
    {
        // Sole version source. Increment for incompatible DTO or semantic changes.
        // Periodic monster attacks use a sparse list; player positions omit confirmed flask state and derived parry flags.
        public const int SchemaVersion = 62;
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
        SwordStates = 32,
        CrystalInput = 33,
        CrystalStates = 34,
        CloneStates = 35,
        BlackholeInput = 36,
        BlackholeStates = 37,
        ParryInput = 38,
        MonsterDeath = 39,
        PlayerDeath = 40,
        DropSpawn = 41,
        PickupRequest = 42,
        PickupResult = 43,
        RoomRetryReady = 44,
        DashExecution = 45,
        JumpExecution = 46,
        PrimaryAttackExecution = 47,
        SwordInstanceEvent = 48,
        CrystalInstanceEvent = 49,
        CloneInstanceEvent = 50,
        BlackholeInstanceEvent = 51,
        SkillRequestRejection = 52
    }

    public enum MovementAnimationState { Idle, Move, Jump, Fall, WallSlide, WallJump, Dash, Parry, ParrySuccess }
    public enum MonsterAiState { Idle, Patrol, Chase, Attack, Stunned, Teleport, Cast }

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
    public sealed class MonsterDeathDto : AuthorityResultDto
    {
        public int entityId;
        public long healthVersion;
        public int maxHealth;
        public int rewardPlayerId, rewardEntityId, rewardAmount;
        public SkillTreeStateDto rewardState;
    }
    [Serializable]
    public sealed class PlayerDeathDto : AuthorityResultDto
    {
        public int playerId;
        public int entityId;
        public long healthVersion;
        public int maxHealth;
        public PlayerDeathCause cause;
    }
    public enum PlayerDeathCause { None, Combat, DeadZone }
    [Serializable]
    public sealed class RoomRetryReadyDto
    {
        public string roomSessionId;
        public long loadId;
    }
    [Serializable]
    public sealed class DropSpawnDto : AuthorityResultDto
    {
        public int dropId;
        public int sourceMonsterEntityId;
        public string configurationId;
        public int quantity;
        public float[] position;
        public float[] velocity;
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
        public bool counterAttack;
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
    public sealed class PickupRequestDto
    {
        public string roomSessionId;
        public long requestSeq;
        public int dropId;
    }
    public enum PickupRejection { None, MissingDrop, InventoryFull, PlayerUnavailable }
    [Serializable]
    public sealed class PickupResultDto : RoomInventoryResultDto
    {
        public int dropId;
        public bool accepted;
        public PickupRejection rejection;
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
    [Serializable]
    public abstract class RoomSkillInputDto
    {
        public string roomSessionId;
        public long actionSeq;
    }
    [Serializable]
    public abstract class RoomSkillObjectDto
    {
        public int entityId, sourcePlayerId, sourceEntityId;
        public long skillInstanceId;
        public float[] position;
    }
    [Serializable]
    public abstract class RoomSkillStatesDto : AuthorityResultDto
    {
        public long stateSeq;
    }
    public enum SwordAction { Aim, Release, Recall, Cancel, Direction }
    public enum SkillRequestRejection { None, InputLocked, Dead, Busy, NotUnlocked, Cooldown, InvalidState, NoInstance, InvalidTarget, Interrupted, NoTarget }
    [Serializable]
    public sealed class SkillRequestRejectionDto : AuthorityResultDto
    {
        public long loadId, requestSeq;
        public int playerId, entityId;
        public RoomSkillId skillId;
        public SkillRequestRejection reason;
    }
    [Serializable]
    public sealed class ParryInputDto : RoomSkillInputDto { }
    [Serializable]
    public sealed class CrystalInputDto : RoomSkillInputDto { }
    [Serializable]
    public sealed class CrystalObjectDto : RoomSkillObjectDto
    {
        public bool exploding;
        public float scale = 1;
    }
    public enum CrystalInstanceEventKind { Spawn, Explode, End }
    [Serializable]
    public abstract class RoomSkillInstanceEventDto : AuthorityResultDto
    {
        public long loadId, requestSeq, skillInstanceId;
        public int entityId, sourcePlayerId, sourceEntityId;
        public ulong startTick;
        public abstract bool EndsInstance { get; }
    }
    [Serializable]
    public sealed class CrystalInstanceEventDto : RoomSkillInstanceEventDto
    {
        public override bool EndsInstance => kind == CrystalInstanceEventKind.End;
        public CrystalInstanceEventKind kind;
        public bool derived;
        public float[] position;
        public float scale;
    }
    [Serializable]
    public sealed class CrystalTrajectoryDto
    {
        public long skillInstanceId;
        public float[] position;
        public float scale;
    }
    [Serializable]
    public sealed class CrystalStatesDto : RoomSkillStatesDto
    {
        public CrystalTrajectoryDto[] crystals;
    }
    [Serializable]
    public sealed class CloneObjectDto : RoomSkillObjectDto
    {
        public bool facingLeft;
        public float alpha;
        public int attackNumber;
    }
    public enum CloneInstanceEventKind { Spawn, End }
    [Serializable]
    public sealed class CloneInstanceEventDto : RoomSkillInstanceEventDto
    {
        public override bool EndsInstance => kind == CloneInstanceEventKind.End;
        public CloneInstanceEventKind kind;
        public float[] position;
        public bool facingLeft;
        public int attackNumber;
    }
    [Serializable]
    public sealed class CloneTrajectoryDto
    {
        public long skillInstanceId;
        public float[] position;
        public bool facingLeft;
        public float alpha;
    }
    [Serializable]
    public sealed class CloneStatesDto : RoomSkillStatesDto
    {
        public CloneTrajectoryDto[] clones;
    }
    public enum BlackholeAction { Cast, Select, Release }
    [Serializable]
    public sealed class BlackholeInputDto : RoomSkillInputDto
    {
        public BlackholeAction action;
        public int targetEntityId;
        public int keyCode;
    }
    [Serializable]
    public sealed class BlackholePromptDto
    {
        public int targetEntityId;
        public int keyCode;
        public float[] position;
    }
    [Serializable]
    public sealed class BlackholeObjectDto : RoomSkillObjectDto
    {
        public float scale;
        public BlackholePromptDto[] prompts;
    }
    public enum BlackholeInstanceEventKind { Spawn, Select, Release, End }
    [Serializable]
    public sealed class BlackholeInstanceEventDto : RoomSkillInstanceEventDto
    {
        public override bool EndsInstance => kind == BlackholeInstanceEventKind.End;
        public BlackholeInstanceEventKind kind;
        public int targetEntityId;
        public float[] position;
        public float scale;
    }
    [Serializable]
    public sealed class BlackholeTrajectoryDto
    {
        public long skillInstanceId;
        public float[] position;
        public float scale;
        public BlackholePromptDto[] prompts;
    }
    [Serializable]
    public sealed class BlackholeStatesDto : RoomSkillStatesDto
    {
        public BlackholeTrajectoryDto[] blackholes;
    }
    public enum SwordPhase { Flying, Stuck, Returning }
    public enum SwordInstanceEventKind { Spawn, Phase, End }
    [Serializable]
    public sealed class SwordInputDto : RoomSkillInputDto
    {
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
    public sealed class SwordObjectDto : RoomSkillObjectDto
    {
        public ulong startTick, endTick;
        public SwordPhase phase;
        public float[] direction;
    }
    [Serializable]
    public sealed class SwordInstanceEventDto : RoomSkillInstanceEventDto
    {
        public override bool EndsInstance => kind == SwordInstanceEventKind.End;
        public ulong endTick;
        public SwordInstanceEventKind kind;
        public SwordPhase phase;
        public float[] position, direction;
    }
    [Serializable]
    public sealed class SwordTrajectoryDto
    {
        public long skillInstanceId;
        public float[] position, direction;
    }
    [Serializable]
    public sealed class SwordStatesDto : RoomSkillStatesDto
    {
        public SwordOwnerDto[] owners;
        public SwordTrajectoryDto[] swords;
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
        public MonsterAttackStateDto[] attacks;
        public MonsterSpellStateDto[] spells = Array.Empty<MonsterSpellStateDto>();
        public MonsterSpawnDto[] spawns = Array.Empty<MonsterSpawnDto>();
    }
    [Serializable]
    public sealed class MonsterAttackStateDto
    {
        public int entityId;
        public MonsterAttackDto attack;
    }
    [Serializable]
    public sealed class MonsterSpellDto
    {
        public long actionId;
        public ulong startTick;
        public float[] position;
    }
    [Serializable]
    public sealed class MonsterSpellStateDto
    {
        public int entityId;
        public MonsterSpellDto spell;
    }
    [Serializable]
    public sealed class MonsterSpawnDto
    {
        public int entityId, sourceEntityId;
        public float[] position;
        public int facingDir;
        public int health, maxHealth;
        public long healthVersion;
    }
    [Serializable]
    public sealed class MonsterStateDto
    {
        public int entityId;
        public int targetEntityId;
        public Vector2 position;
        public int facingDir;
        public MonsterAiState aiState;
        public bool moving;
        public bool counterWindow;
        public bool active;
        public bool dead;
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
        public Vector2 position;
        public int facingDir;
        public bool active;
        public bool dead;
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
        public string resourceVersion;
    }

    public enum HandshakeRejection { None, VersionMismatch, RoomFull, InvalidRequest, ResourceVersionMismatch }

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
        public Vector2 position;
        public int facingDir;
        public MovementAnimationState movementState;
        public float yVelocity;
        public int health;
        public int maxHealth;
        public long healthVersion;
        public PlayerDeathCause deathCause;
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

    [Serializable]
    public sealed class JumpExecutionDto
    {
        public string roomSessionId;
        public long loadId;
        public long actionSeq;
        public ulong authorityTick;
        public bool accepted;
        public bool wallJump;
        public int facingDir; // A's facing after the jump starts; zero on rejection.
    }

    // A dedicated per-player press stream. No actor ID, hit target, attack instance ID or resultId supplied by B.
    [Serializable]
    public sealed class PrimaryAttackRequestDto
    {
        public long requestSeq;
    }

    [Serializable]
    public sealed class PrimaryAttackExecutionDto
    {
        public string roomSessionId;
        public long loadId;
        public long requestSeq;
        public ulong authorityTick;
        public bool accepted;
        public PrimaryAttackRejection rejection;
        public long attackInstanceId;
        public ulong startTick;
        public int facingDir;
        public int comboIndex;
        public int durationTicks;
    }

    [Serializable]
    public sealed class DashRequestDto
    {
        public long actionSeq;
        public float currentAxis; // Input at the press; zero asks A to use its own current facing.
    }

    [Serializable]
    public sealed class DashExecutionDto
    {
        public string roomSessionId;
        public long loadId;
        public long actionSeq;
        public ulong authorityTick; // Start tick when accepted; decision tick when rejected.
        public bool accepted;
        public int facingDir; // Zero on rejection.
    }

    [Serializable]
    public sealed class LocalInputLockDto
    {
        public long lockSeq;
        public bool locked;
    }

    // Both entries share the snapshot's authority tick and sequence.
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
        public float[] skillFills = new float[5];
        public int entityId;
        public long processedInputSeq;
        public Vector2 position;
        public int facingDir;
        public MovementAnimationState movementState;
        public float yVelocity;
        public PlayerDeathCause deathCause;
        public long deathHealthVersion;
        public int deathMaxHealth;
        public bool invincible;
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
        public string resourceVersion;
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
