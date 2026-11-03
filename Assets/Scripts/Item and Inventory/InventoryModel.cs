using System.Collections.Generic;

/// <summary>Owns ordinary inventory, material stash and equipped item data.</summary>
public sealed class InventoryModel : IModel
{
    public List<InventoryItem> Items { get; } = new List<InventoryItem>();
    public Dictionary<ItemData, InventoryItem> ItemDictionary { get; } = new Dictionary<ItemData, InventoryItem>();
    public List<InventoryItem> Stash { get; } = new List<InventoryItem>();
    public Dictionary<ItemData, InventoryItem> StashDictionary { get; } = new Dictionary<ItemData, InventoryItem>();
    public List<InventoryItem> Equipment { get; } = new List<InventoryItem>();
    public Dictionary<ItemData_Equipment, InventoryItem> EquipmentDictionary { get; }
        = new Dictionary<ItemData_Equipment, InventoryItem>();

    public void Init() { }
}
