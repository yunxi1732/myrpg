using System.Collections.Generic;
using Rpg.Networking;
using Rpg.Networking.Protocol;

public sealed partial class PlayerCombatContext
{
    private readonly InventoryModel roomInventoryModel = new InventoryModel();
    private readonly InventorySystem roomInventorySystem;
    public IReadOnlyList<ItemData_Equipment> RoomInventory
    {
        get
        {
            var items = new ItemData_Equipment[roomInventoryModel.Items.Count];
            for (int i = 0; i < items.Length; i++) items[i] = (ItemData_Equipment)roomInventoryModel.Items[i].data;
            return items;
        }
    }
    public int RoomInventoryCapacity { get; private set; } = 24;
    public long EquipmentVersion { get; private set; } = 1;
    private long equipmentRequestSeq;
    internal bool TryReceiveRoomDrop(ItemData item, int dropId, ulong tick)
    {
        if (item.itemType == ItemType.Equipment)
        {
            if (!roomInventorySystem.CanAddItem(RoomInventoryCapacity)) return false;
            var source = (ItemData_Equipment)item;
            var value = RoomCombatConfiguration.CaptureEquipment((RoomEquipmentSlot)source.equipmentType, source);
            value.configurationId = "drop/" + dropId;
            var owned = RoomCombatConfiguration.CreateEquipment(value);
            ownedRoomEquipment.Add(owned);
            roomInventorySystem.AddItem(owned, RoomInventoryCapacity);
        }
        else
        {
            string materialId = RoomCraftCatalog.MaterialId(item);
            if (!roomMaterialData.TryGetValue(materialId, out var owned))
            {
                owned = UnityEngine.ScriptableObject.CreateInstance<ItemData>();
                owned.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                owned.name = materialId; owned.itemId = materialId;
                owned.itemName = string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;
                owned.itemType = ItemType.Material;
                ownedRoomMaterials.Add(owned); roomMaterialData.Add(materialId, owned);
            }
            roomInventorySystem.AddItem(owned, RoomInventoryCapacity);
        }
        EquipmentVersion = checked(EquipmentVersion + 1); equipmentTick = tick;
        return true;
    }
    private ulong equipmentTick;

    // Called on A's main thread only, after resolving the actor from the admitted connection/local identity.
    internal EquipmentChangeResultDto ChangeRoomEquipment(EquipmentChangeRequestDto request, ulong tick, AuthorityResultSequence results)
    {
        if (!ProtocolCodec.ValidEquipmentRequest(request) ||
            request.roomSessionId != RoomSessionId || request.requestSeq <= equipmentRequestSeq) return null;
        equipmentRequestSeq = request.requestSeq; // Rejected requests also consume their own sequence.
        var rejection = EquipmentChangeRejection.None;
        var slot = RoomCombatConfiguration.EquipmentSlot(request.slot);
        var previous = GetEquipment(slot);
        ItemData_Equipment next = null;
        if (!Owner.IsRuntimeInitialized || !Stats.IsRuntimeInitialized || Stats.isDead || Stats.currentHealth <= 0)
            rejection = EquipmentChangeRejection.PlayerUnavailable;
        else if (request.expectedVersion != EquipmentVersion) rejection = EquipmentChangeRejection.StaleInventory;
        else if (request.kind == EquipmentChangeKind.Unequip)
        {
            if (previous == null || previous.name != request.configurationId) rejection = EquipmentChangeRejection.MissingItem;
            else if (!roomInventorySystem.CanAddItem(RoomInventoryCapacity)) rejection = EquipmentChangeRejection.InventoryFull;
        }
        else
        {
            foreach (var entry in roomInventoryModel.Items)
                if (entry.data is ItemData_Equipment item && item.name == request.configurationId && item.equipmentType == slot)
                { next = item; break; }
            if (next == null) rejection = EquipmentChangeRejection.MissingItem;
        }
        if (rejection == EquipmentChangeRejection.None)
        {
            // Remove incoming item first: swapping in a full bag retains the outgoing item without loss.
            if (next != null) roomInventorySystem.RemoveItem(next);
            if (previous != null) roomInventorySystem.AddItem(previous, RoomInventoryCapacity);
            SetEquipment(slot, next);
            EquipmentVersion = checked(EquipmentVersion + 1); equipmentTick = tick;
            // Changing max HP never heals; lowering it clamps HP and advances the common health version.
            Stats.ApplyAuthorityNetworkHealth(UnityEngine.Mathf.Min(Stats.currentHealth, Stats.GetMaxHealthValue()));
        }
        return new EquipmentChangeResultDto { roomSessionId = RoomSessionId, authorityTick = tick, resultId = results.Allocate(),
            playerId = PlayerId, entityId = EntityId, requestSeq = request.requestSeq,
            accepted = rejection == EquipmentChangeRejection.None, rejection = rejection, combat = CaptureRoomConfiguration(), flask = CaptureRoomFlaskState(),
            health = Stats.currentHealth, maxHealth = Stats.GetMaxHealthValue(), healthVersion = Stats.NetworkHealthVersion };
    }

    internal bool ApplyEquipmentConfirmation(EquipmentChangeResultDto value)
        => ApplyInventoryConfirmation(value);

    internal bool ApplyInventoryConfirmation(RoomInventoryResultDto value)
    {
        if (EntityRuntimePermissions.Identity(Owner).Role != NetworkEntityRole.Display ||
            !ProtocolCodec.IsValidCombat(value.combat) || value.authorityTick == 0 ||
            value.roomSessionId != RoomSessionId || value.playerId != PlayerId || value.entityId != EntityId) return false;
        ApplyRoomSkillTreeSnapshot(value.combat.skillTree, value.authorityTick);
        if (value.authorityTick < equipmentTick || value.combat.equipmentVersion <= EquipmentVersion && RoomConfigurationId != null) return false;
        InstallRoomConfiguration(value.combat, true);
        equipmentTick = value.authorityTick;
        return true;
    }
}
