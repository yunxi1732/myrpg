using UnityEngine;

/// <summary>Displays inventory data through existing scene slots; owns no gameplay rules.</summary>
public sealed class InventoryView : System.IDisposable
{
    private readonly IArchitecture architecture;
    private readonly InventoryModel model;
    private readonly InventoryViewLifecycle[] lifecycle;
    private bool subscribed;
    private bool disposed;
    public int NotificationCount { get; private set; }
    private readonly UI_ItemSlot[] inventorySlots;
    private readonly UI_ItemSlot[] stashSlots;
    private readonly UI_EquipmentSlot[] equipmentSlots;
    public int InventoryCapacity => inventorySlots.Length;

    public InventoryView(Transform inventoryParent, Transform stashParent, Transform equipmentParent, IArchitecture architecture, InventoryModel model)
    {
        inventorySlots = inventoryParent.GetComponentsInChildren<UI_ItemSlot>(true);
        stashSlots = stashParent.GetComponentsInChildren<UI_ItemSlot>(true);
        equipmentSlots = equipmentParent.GetComponentsInChildren<UI_EquipmentSlot>(true);
        this.architecture = architecture;
        this.model = model;
        var hooks = new System.Collections.Generic.List<InventoryViewLifecycle>();
        foreach (Transform parent in new[] { inventoryParent, stashParent, equipmentParent })
        {
            var hook = parent.GetComponent<InventoryViewLifecycle>();
            if (hook == null) hook = parent.gameObject.AddComponent<InventoryViewLifecycle>();
            if (hooks.Contains(hook)) continue;
            hooks.Add(hook);
            hook.Changed += SynchronizeVisibility;
        }
        lifecycle = hooks.ToArray();
        SynchronizeVisibility();
    }

    private void SynchronizeVisibility()
    {
        if (disposed) return;
        bool visible = false;
        foreach (var hook in lifecycle)
            if (hook != null && hook.isActiveAndEnabled) visible = true;
        if (visible && !subscribed)
        {
            architecture.RegistEvent<InventoryChanged>(OnInventoryChanged);
            subscribed = true;
        }
        else if (!visible && subscribed)
        {
            architecture.UnRegistEvent<InventoryChanged>(OnInventoryChanged);
            subscribed = false;
        }
        if (visible) Refresh(model);
    }

    private void OnInventoryChanged(object data)
    {
        NotificationCount++;
        Refresh((InventoryModel)data);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        architecture.UnRegistEvent<InventoryChanged>(OnInventoryChanged);
        subscribed = false;
        foreach (var hook in lifecycle)
            if (hook != null) hook.Changed -= SynchronizeVisibility;
    }

    public void Refresh(InventoryModel model)
    {
        foreach (UI_EquipmentSlot slot in equipmentSlots)
        {
            if (!slot.gameObject.activeInHierarchy) continue;
            slot.CleanUpSlot();
            foreach (var pair in model.EquipmentDictionary)
                if (pair.Key.equipmentType == slot.slotType) slot.UpdateSlot(pair.Value);
        }
        foreach (UI_ItemSlot slot in inventorySlots) if (slot.gameObject.activeInHierarchy) slot.CleanUpSlot();
        foreach (UI_ItemSlot slot in stashSlots) if (slot.gameObject.activeInHierarchy) slot.CleanUpSlot();
        for (int i = 0; i < model.Items.Count; i++) if (inventorySlots[i].gameObject.activeInHierarchy) inventorySlots[i].UpdateSlot(model.Items[i]);
        for (int i = 0; i < model.Stash.Count; i++) if (stashSlots[i].gameObject.activeInHierarchy) stashSlots[i].UpdateSlot(model.Stash[i]);
    }
}
