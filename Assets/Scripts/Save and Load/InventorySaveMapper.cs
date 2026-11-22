using System;
using System.Collections.Generic;

/// <summary>Maps existing save fields and item references without applying equipment effects.</summary>
public static class InventorySaveMapper
{
    public static void Read(GameData data, List<ItemData> database,
        List<InventoryItem> loadedItems, List<ItemData_Equipment> loadedEquipment)
    {
        loadedItems.Clear();
        loadedEquipment.Clear();
        foreach (var pair in data.inventory)
        {
            foreach (ItemData item in database)
            {
                if (item != null && item.StableItemId == pair.Key)
                    loadedItems.Add(new InventoryItem(item) { stackSize = pair.Value });
            }
        }
        foreach (string id in data.equipmentId)
        {
            foreach (ItemData item in database)
            {
                if (item != null && item.StableItemId == id)
                    loadedEquipment.Add(item as ItemData_Equipment);
            }
        }
    }

    public static void Write(InventoryModel model, GameData data)
    {
        // Validate before changing GameData, so a bad asset cannot corrupt the
        // in-memory snapshot or overwrite the last valid save on exit.
        var items = new Dictionary<string, int>();
        var equipment = new List<string>();
        foreach (var pair in model.ItemDictionary)
            AddItem(items, pair.Key, pair.Value);
        foreach (var pair in model.StashDictionary)
            AddItem(items, pair.Key, pair.Value);
        foreach (var pair in model.EquipmentDictionary)
        {
            string id = pair.Key != null ? pair.Key.StableItemId : null;
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Equipped item has no stable itemId.");
            equipment.Add(id);
        }

        data.inventory.Clear();
        data.equipmentId.Clear();
        foreach (var pair in items) data.inventory.Add(pair.Key, pair.Value);
        data.equipmentId.AddRange(equipment);
    }

    private static void AddItem(Dictionary<string, int> items, ItemData item, InventoryItem stack)
    {
        string id = item != null ? item.StableItemId : null;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Inventory item has no stable itemId: " +
                                                (item != null ? item.name : "<null>"));
        if (items.ContainsKey(id))
            throw new InvalidOperationException("Duplicate itemId in inventory: " + id);
        items.Add(id, stack.stackSize);
    }
}
