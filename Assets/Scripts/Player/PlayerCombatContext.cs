using System;
using System.Collections.Generic;
using Rpg.Networking;
using Rpg.Networking.Protocol;

/// <summary>One actor's combat data. Room identity is read from the bound entity, never from request data.</summary>
public sealed partial class PlayerCombatContext
{
    public Player Owner { get; }
    public PlayerStats Stats { get; }
    public PlayerSkillRuntime Skills { get; }
    public PrimaryAttackRuntime PrimaryAttack { get; private set; }
    public PrimaryAttackHitDetection PrimaryAttackHits { get; private set; }
    internal void BindPrimaryAttackTargets(RoomSceneMonsters monsters)
    {
        if (!Owner.IsAuthoritative || PrimaryAttack == null || monsters == null)
            throw new InvalidOperationException("Bind the authority attack runtime and room monsters first.");
        if (PrimaryAttackHits == null) PrimaryAttackHits = new PrimaryAttackHitDetection(this, monsters.IsValidPrimaryAttackTarget);
    }
    internal void BindPrimaryAttack(RoomNetworkObjects room)
    {
        if (!Owner.IsAuthoritative || room == null || room.IsDisposed || room.RoomSessionId != RoomSessionId)
            throw new InvalidOperationException("Use this actor's live authority room.");
        if (PrimaryAttack == null) PrimaryAttack = new PrimaryAttackRuntime(RoomSessionId, PlayerId, EntityId, room.AllocatePrimaryAttackInstanceId);
    }
    public SkillManager LegacySkills { get; private set; }
    public Inventory LegacyInventory { get; private set; }
    public string RoomConfigurationId { get; private set; }
    private int roomDodgeBonus;
    private bool roomDodgeApplied;
    internal void ConfigureRoomDodgeBonus(int amount) => roomDodgeBonus = amount;
    private readonly List<ItemData_Equipment> ownedRoomEquipment = new List<ItemData_Equipment>();
    private ulong configurationTick;
    private long configurationSeq;
    private readonly Dictionary<EquipmentType, ItemData_Equipment> equipment = new Dictionary<EquipmentType, ItemData_Equipment>();
    public string RoomSessionId => EntityRuntimePermissions.Identity(Owner)?.RoomSessionId;
    public int PlayerId => EntityRuntimePermissions.Identity(Owner)?.PlayerId ?? 0;
    public int EntityId => EntityRuntimePermissions.Identity(Owner)?.EntityId ?? 0;

    internal PlayerCombatContext(Player owner)
    {
        Owner = owner;
        Stats = owner.GetComponent<PlayerStats>();
        if (Stats == null) throw new InvalidOperationException("Player combat requires this actor's PlayerStats.");
        Stats.EnsureIndependentStats();
        Skills = new PlayerSkillRuntime(this);
    }

    public ItemData_Equipment GetEquipment(EquipmentType slot) =>
        LegacyInventory != null && EntityRuntimePermissions.CanUseLegacyWorld(Owner)
            ? LegacyInventory.GetEquipment(slot)
            : equipment.TryGetValue(slot, out var item) ? item : null;

    // Configuration references can be shared; slot selection, modifiers and timers belong to the actor.
    // Room changes use A's validated inventory request path; legacy item effects/persistence stay disabled.
    public void SetEquipment(EquipmentType slot, ItemData_Equipment item)
    {
        if (!Owner.IsAuthoritative) throw new InvalidOperationException("Only authority may change combat equipment.");
        if (LegacyInventory != null) throw new InvalidOperationException("Use the bound single-player inventory to change equipment.");
        if (item != null && item.equipmentType != slot) throw new ArgumentException("Equipment slot mismatch.");
        equipment.TryGetValue(slot, out var previous);
        if (previous == item) return;
        if (previous != null) previous.RemoveModifiers(Stats);
        if (item == null) equipment.Remove(slot);
        else { equipment[slot] = item; item.AddModifiers(Stats); }
        if (slot == EquipmentType.Flask) RoomFlaskEquipmentChanged();
    }

    internal void InstallRoomConfiguration(PlayerCombatDto value, bool display)
    {
        if (!ProtocolCodec.IsValidCombat(value)) throw new ArgumentException("Invalid room combat configuration.");
        var identity = EntityRuntimePermissions.Identity(Owner);
        if (identity == null || !identity.IsBound || display != (identity.Role == NetworkEntityRole.Display) ||
            !display && !Owner.IsAuthoritative || LegacyInventory != null || LegacySkills != null)
            throw new InvalidOperationException("Room combat configuration requires the assigned entity role.");
        ReleaseRoomEquipment(); equipment.Clear(); roomInventory.Clear(); roomMaterials.Clear();
        foreach (var material in value.materials) roomMaterials.Add(material.materialId, new RoomMaterialDto {
            materialId = material.materialId, displayName = material.displayName, count = material.count });
        EquipmentVersion = value.equipmentVersion; RoomInventoryCapacity = value.inventoryCapacity;
        for (int i = 0; i < value.stats.Length; i++)
        {
            Stat stat = RoomCombatConfiguration.StatAt(Stats, i);
            stat.modifiers.Clear();
            int basis = value.stats[i];
            if (!display) foreach (var item in value.equipment) basis -= item.modifiers[i];
            stat.SetDefaultValue(basis);
        }
        foreach (var entry in value.equipment)
        {
            var item = RoomCombatConfiguration.CreateEquipment(entry);
            if (item == null) continue;
            ownedRoomEquipment.Add(item);
            if (display) equipment.Add(RoomCombatConfiguration.EquipmentSlot(entry.slot), item);
            else SetEquipment(RoomCombatConfiguration.EquipmentSlot(entry.slot), item);
        }
        RestoreRoomSkillTree(value.skillTree);
        roomDodgeApplied = display && value.skillTree.unlockedSkills[(int)RoomSkillId.Dodge];
        foreach (var entry in value.skills) Skills.Get(entry.skillId).Restore(entry);
        ProjectRoomSkillUnlocks();
        foreach (var entry in value.inventory)
        {
            var item = RoomCombatConfiguration.CreateEquipment(entry);
            ownedRoomEquipment.Add(item); roomInventory.Add(item);
        }
        RoomConfigurationId = value.configurationId;
        Owner.ConfigureNetworkDash(Skills.Get(RoomSkillId.Dash).Cooldown);
    }

    // Called only by the existing A-connection initialization route after room/load/entity checks.
    internal bool ApplyConfirmedRoomConfiguration(PlayerCombatDto value, string room, ulong tick, long seq)
    {
        if (EntityRuntimePermissions.Identity(Owner)?.Role != NetworkEntityRole.Display ||
            room != RoomSessionId || tick == 0 || seq <= configurationSeq || tick < configurationTick ||
            !ProtocolCodec.IsValidCombat(value)) return false;
        ApplyRoomSkillTreeSnapshot(value.skillTree, tick);
        if (RoomConfigurationId != null && value.equipmentVersion < EquipmentVersion) return false;
        if (RoomConfigurationId != null && value.equipmentVersion == EquipmentVersion)
        {
            configurationTick = tick; configurationSeq = seq; equipmentTick = tick;
            return true;
        }
        InstallRoomConfiguration(value, true);
        configurationTick = tick; configurationSeq = seq; equipmentTick = tick;
        return true;
    }

    public PlayerCombatDto CaptureRoomConfiguration()
    {
        if (RoomConfigurationId == null) throw new InvalidOperationException("Room combat configuration is not initialized.");
        var value = new PlayerCombatDto { configurationId = RoomConfigurationId, stats = new int[14],
            equipment = new RoomEquipmentDto[4], skills = new RoomSkillDto[7], equipmentVersion = EquipmentVersion,
            inventoryCapacity = RoomInventoryCapacity, inventory = new RoomEquipmentDto[roomInventory.Count], materials = CaptureRoomMaterials(),
            skillTree = CaptureRoomSkillTree() };
        for (int i = 0; i < roomInventory.Count; i++) value.inventory[i] = RoomCombatConfiguration.CaptureEquipment(
            (RoomEquipmentSlot)roomInventory[i].equipmentType, roomInventory[i]);
        for (int i = 0; i < 14; i++) value.stats[i] = RoomCombatConfiguration.StatAt(Stats, i).GetValue();
        for (int i = 0; i < 4; i++) value.equipment[i] = RoomCombatConfiguration.CaptureEquipment((RoomEquipmentSlot)i,
            GetEquipment(RoomCombatConfiguration.EquipmentSlot((RoomEquipmentSlot)i)));
        for (int i = 0; i < 7; i++)
        {
            var slot = Skills.Get((RoomSkillId)i);
            value.skills[i] = new RoomSkillDto { skillId = (RoomSkillId)i, unlocked = slot.Unlocked,
                cooldown = slot.Cooldown, remaining = slot.Remaining };
        }
        return value;
    }

    internal void ReleaseRoomEquipment()
    {
        foreach (var item in ownedRoomEquipment)
            if (item != null)
            {
                if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(item);
                else UnityEngine.Object.DestroyImmediate(item);
            }
        ownedRoomEquipment.Clear();
    }

    internal void BindLegacySkills(SkillManager manager)
    {
        if (!EntityRuntimePermissions.CanUseLegacyActions(Owner) || manager == null ||
            LegacySkills != null && LegacySkills != manager)
            throw new InvalidOperationException("Legacy skills require one explicit single-player owner.");
        LegacySkills = manager;
    }

    internal void BindLegacyInventory(Inventory inventory)
    {
        if (!EntityRuntimePermissions.CanUseLegacyWorld(Owner) || inventory == null ||
            LegacyInventory != null && LegacyInventory != inventory || equipment.Count != 0)
            throw new InvalidOperationException("Legacy inventory requires one explicit single-player owner.");
        LegacyInventory = inventory;
    }
}

/// <summary>Per-player runtime slots; existing solo Skill components are adapters, not room-wide runtime state.</summary>
public sealed class PlayerSkillRuntime
{
    public sealed class Slot
    {
        private float remaining, cooldown;
        internal Skill Legacy;
        public bool Unlocked { get; private set; } = true;
        public float Cooldown => Legacy != null ? Legacy.cooldown : cooldown;
        public float Remaining => Legacy != null ? UnityEngine.Mathf.Max(0, Legacy.cooldownTimer) : remaining;
        internal void Configure(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            cooldown = seconds;
        }
        internal void SetRoomUnlocked(bool value)
        {
            if (Legacy != null) throw new InvalidOperationException("Do not apply room unlocks to solo skills.");
            Unlocked = value;
        }
        internal bool Commit()
        {
            if (!Unlocked || Remaining > 0) return false;
            if (Legacy != null) Legacy.cooldownTimer = Cooldown;
            else remaining = Cooldown;
            return true;
        }
        internal void Advance(float delta) { if (Legacy == null) remaining = UnityEngine.Mathf.Max(0, remaining - delta); }
        internal void Restore(RoomSkillDto value)
        {
            if (Legacy != null) throw new InvalidOperationException("Do not apply room state to solo skills.");
            Configure(value.cooldown); Unlocked = value.unlocked; remaining = value.remaining;
        }
    }

    public PlayerCombatContext Context { get; }
    private readonly Dictionary<Type, Slot> slots = new Dictionary<Type, Slot>();
    internal PlayerSkillRuntime(PlayerCombatContext context) { Context = context; }
    private Slot Get(Type type)
    {
        if (!slots.TryGetValue(type, out var slot)) slots.Add(type, slot = new Slot());
        return slot;
    }
    public Slot Get<T>() where T : Skill => Get(typeof(T));
    public Slot Get(RoomSkillId id)
    {
        switch (id)
        {
            case RoomSkillId.Dash: return Get<DashSkill>();
            case RoomSkillId.Clone: return Get<CloneSkill>();
            case RoomSkillId.Sword: return Get<SwordSkill>();
            case RoomSkillId.Blackhole: return Get<BlackholeSkill>();
            case RoomSkillId.Crystal: return Get<CrystalSkill>();
            case RoomSkillId.Parry: return Get<ParrySkill>();
            case RoomSkillId.Dodge: return Get<DodgeSkill>();
            default: throw new ArgumentOutOfRangeException(nameof(id));
        }
    }
    public void Configure<T>(float cooldown) where T : Skill => Get<T>().Configure(cooldown);
    // Call only after action validation and execution succeed; a rejected action never starts a timer.
    public bool Commit<T>() where T : Skill => Context.Owner.IsAuthoritative && Get<T>().Commit();
    internal void Bind(Skill skill)
    {
        var slot = Get(skill.GetType());
        if (slot.Legacy != null && slot.Legacy != skill) throw new InvalidOperationException("Duplicate skill runtime slot.");
        slot.Legacy = skill;
    }
    public void Advance(float delta)
    {
        if (!Context.Owner.IsAuthoritative) return;
        if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        foreach (var slot in slots.Values) slot.Advance(delta);
    }
}
