using UnityEngine;

/// <summary>Refreshes the existing attribute panel without changing stat calculations.</summary>
public sealed class InventoryStatsView
{
    private readonly UI_StatSlot[] slots;

    public InventoryStatsView(Transform parent)
    {
        slots = parent.GetComponentsInChildren<UI_StatSlot>(true);
    }

    public void Refresh()
    {
        foreach (UI_StatSlot slot in slots) slot.UpdateStatValueUI();
    }
}
