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
                if (item != null && item.itemId == pair.Key)
                    loadedItems.Add(new InventoryItem(item) { stackSize = pair.Value });
            }
        }
        foreach (string id in data.equipmentId)
        {
            foreach (ItemData item in database)
            {
                if (item != null && item.itemId == id)
                    loadedEquipment.Add(item as ItemData_Equipment);
            }
        }
    }

    public static void Write(InventoryModel model, GameData data)
    {
        data.inventory.Clear();
        data.equipmentId.Clear();
        foreach (var pair in model.ItemDictionary)
            data.inventory.Add(pair.Key.itemId, pair.Value.stackSize);
        foreach (var pair in model.StashDictionary)
            data.inventory.Add(pair.Key.itemId, pair.Value.stackSize);
        foreach (var pair in model.EquipmentDictionary)
            data.equipmentId.Add(pair.Key.itemId);
    }
}
