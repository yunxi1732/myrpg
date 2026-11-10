using System;
using Rpg.Networking;
using UnityEngine;

/// <summary>The existing slots display only the local room actor's A-confirmed inventory.</summary>
public sealed class NetworkInventoryView : IDisposable
{
    private readonly UI_ItemSlot[] bag;
    private readonly UI_EquipmentSlot[] equipment;
    private readonly UI_ItemSlot[] materials;
    private readonly System.Collections.Generic.List<ItemData> materialViews = new System.Collections.Generic.List<ItemData>();
    private long displayedVersion;
    private readonly Inventory source;
    private RoomEntityPresentation local;
    private Action changed;
    private int bindingVersion;
    public Transform BagRoot => source != null ? source.InventorySlotParent : null;
    public Transform EquipmentRoot => source != null ? source.EquipmentSlotParent : null;
    public Transform MaterialsRoot => source != null ? source.StashSlotParent : null;
    public NetworkInventoryView(Inventory source)
    {
        this.source = source;
        bag = BagRoot != null ? BagRoot.GetComponentsInChildren<UI_ItemSlot>(true) : Array.Empty<UI_ItemSlot>();
        equipment = EquipmentRoot != null ? EquipmentRoot.GetComponentsInChildren<UI_EquipmentSlot>(true) : Array.Empty<UI_EquipmentSlot>();
        materials = MaterialsRoot != null ? MaterialsRoot.GetComponentsInChildren<UI_ItemSlot>(true) : Array.Empty<UI_ItemSlot>();
    }
    public void Bind(RoomEntityPresentation entity)
    {
        Dispose();
        if (entity == null || !entity.IsValid || !entity.IsReady || entity.View == null ||
            entity.View.GetComponent<NetworkEntityIdentity>()?.IsLocalInputOwner != true) return;
        local = entity;
        int version = bindingVersion;
        changed = () => { if (version == bindingVersion) Refresh(); };
        local.Changed += changed;
        foreach (var slot in bag) if (slot != null) slot.BindNetwork(local);
        foreach (var slot in equipment) if (slot != null) slot.BindNetwork(local);
        foreach (var slot in materials) if (slot != null) slot.BindNetwork(local);
        Refresh();
    }
    private void Refresh()
    {
        var combat = local != null && local.IsValid && local.IsReady && local.View != null ? local.View.GetComponent<Player>()?.Combat : null;
        if (combat != null && combat.RoomConfigurationId != null && displayedVersion == combat.EquipmentVersion) return;
        foreach (var slot in bag) if (slot != null) slot.CleanUpSlot();
        foreach (var slot in equipment) if (slot != null) slot.CleanUpSlot();
        foreach (var slot in materials) if (slot != null) slot.CleanUpSlot();
        ClearMaterialViews();
        if (combat?.RoomConfigurationId == null) return;
        displayedVersion = combat.EquipmentVersion;
        for (int i = 0; i < combat.RoomInventory.Count && i < bag.Length; i++)
            if (bag[i] != null) bag[i].UpdateSlot(Item(combat.RoomInventory[i]));
        foreach (var slot in equipment)
        {
            if (slot == null) continue;
            var item = combat.GetEquipment(slot.slotType);
            if (item != null) slot.UpdateSlot(Item(item));
        }
        var values = combat.CaptureRoomMaterials();
        for (int i = 0; i < values.Length && i < materials.Length; i++)
        {
            var value = values[i];
            var data = ScriptableObject.CreateInstance<ItemData>(); data.hideFlags = HideFlags.HideAndDontSave;
            data.name = value.materialId; data.itemName = value.displayName; data.itemType = ItemType.Material; data.itemId = value.materialId;
            if (source != null && source.itemDataBase != null) foreach (var visual in source.itemDataBase)
                if (visual != null && visual.itemType == ItemType.Material && RoomCraftCatalog.MaterialId(visual) == value.materialId)
                { data.itemIcon = visual.itemIcon; break; }
            materialViews.Add(data);
            if (materials[i] != null) materials[i].UpdateSlot(new InventoryItem(data) { stackSize = value.count });
        }
    }
    private InventoryItem Item(ItemData_Equipment item)
    {
        // Optional local icon only. Modifiers, identity and item counts always come from A.
        if (item.itemIcon == null && source != null && source.itemDataBase != null)
        {
            foreach (var data in source.itemDataBase)
                if (data is ItemData_Equipment visual && RoomCraftCatalog.RecipeId(visual) == item.itemId)
                { item.itemIcon = visual.itemIcon; break; }
            foreach (var data in source.itemDataBase)
                if (item.itemIcon == null && data is ItemData_Equipment visual && visual.equipmentType == item.equipmentType && visual.itemIcon != null)
                { item.itemIcon = visual.itemIcon; break; }
        }
        return new InventoryItem(item);
    }
    public void Dispose()
    {
        ++bindingVersion;
        if (local != null && changed != null) local.Changed -= changed;
        local = null; changed = null;
        foreach (var slot in bag) if (slot != null) slot.BindNetwork(null);
        foreach (var slot in equipment) if (slot != null) slot.BindNetwork(null);
        foreach (var slot in materials) if (slot != null) slot.BindNetwork(null);
        displayedVersion = 0; ClearMaterialViews();
    }
    private void ClearMaterialViews()
    {
        foreach (var data in materialViews) if (data != null)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(data); else UnityEngine.Object.DestroyImmediate(data); }
        materialViews.Clear();
    }
}
