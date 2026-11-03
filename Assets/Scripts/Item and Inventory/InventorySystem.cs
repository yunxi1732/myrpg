using System.Collections.Generic;

/// <summary>Inventory rules; scene capacity and UI remain with the Inventory facade.</summary>
public sealed class InventorySystem : ISystem
{
    private readonly InventoryModel model;
    private float lastTimeUsedFlask;
    private float lastTimeUsedArmor;
    private float armorCooldown;
    public float FlaskCooldown { get; private set; }
    public InventorySystem(InventoryModel model) { this.model = model; }
    public void Init() { }

    public bool UseFlask(float time)
    {
        ItemData_Equipment flask = GetEquipment(EquipmentType.Flask);
        if (flask == null || time <= lastTimeUsedFlask + FlaskCooldown) return false;
        FlaskCooldown = flask.itemCooldown;
        flask.Effect(null);
        lastTimeUsedFlask = time;
        return true;
    }

    public bool CanUseArmor(float time)
    {
        ItemData_Equipment armor = GetEquipment(EquipmentType.Armor);
        if (armor == null || time <= lastTimeUsedArmor + armorCooldown) return false;
        armorCooldown = armor.itemCooldown;
        lastTimeUsedArmor = time;
        return true;
    }

    private ItemData_Equipment GetEquipment(EquipmentType type)
    {
        ItemData_Equipment result = null;
        foreach (var pair in model.EquipmentDictionary)
            if (pair.Key.equipmentType == type) result = pair.Key;
        return result;
    }

    public bool CanAddItem(int capacity) => model.Items.Count < capacity;

    public bool AddItem(ItemData item, int capacity)
    {
        if (item.itemType == ItemType.Equipment)
        {
            // Preserve baseline: a full inventory rejects even an existing equipment stack.
            if (!CanAddItem(capacity)) return false;
            Add(model.Items, model.ItemDictionary, item);
            return true;
        }
        if (item.itemType == ItemType.Material)
        {
            Add(model.Stash, model.StashDictionary, item);
            return true;
        }
        return false;
    }

    public bool CanCraft(ItemData_Equipment product, List<InventoryItem> requiredMaterials, int capacity, out bool productAdded)
    {
        productAdded = false;
        // Validate all recipe entries before changing any inventory data.
        foreach (InventoryItem required in requiredMaterials)
        {
            if (!model.StashDictionary.TryGetValue(required.data, out InventoryItem available)
                || available.stackSize < required.stackSize)
                return false;
        }
        foreach (InventoryItem required in requiredMaterials)
        {
            for (int amount = 0; amount < required.stackSize; amount++)
                RemoveItem(required.data);
        }
        // Preserve the existing capacity behavior of the compatibility entry point.
        productAdded = AddItem(product, capacity);
        return true;
    }

    public void RemoveItem(ItemData item)
    {
        Remove(model.Items, model.ItemDictionary, item);
        Remove(model.Stash, model.StashDictionary, item);
    }

    // Returns whether returning the old equipment succeeded (same capacity rule as AddItem).
    public bool EquipItem(ItemData item, int capacity)
    {
        var newEquipment = item as ItemData_Equipment;
        var newItem = new InventoryItem(newEquipment);
        ItemData_Equipment oldEquipment = null;
        foreach (KeyValuePair<ItemData_Equipment, InventoryItem> pair in model.EquipmentDictionary)
        {
            if (pair.Key.equipmentType == newEquipment.equipmentType)
                oldEquipment = pair.Key;
        }

        bool returnedOldEquipment = true;
        if (oldEquipment != null)
        {
            UnequipItem(oldEquipment);
            returnedOldEquipment = AddItem(oldEquipment, capacity);
        }

        model.Equipment.Add(newItem);
        model.EquipmentDictionary.Add(newEquipment, newItem);
        newEquipment.AddModifiers();
        RemoveItem(item);
        return returnedOldEquipment;
    }

    public void UnequipItem(ItemData_Equipment itemToRemove)
    {
        if (model.EquipmentDictionary.TryGetValue(itemToRemove, out InventoryItem item))
        {
            model.Equipment.Remove(item);
            model.EquipmentDictionary.Remove(itemToRemove);
            itemToRemove.RemoveModifiers();
        }
    }

    private static void Remove(List<InventoryItem> items, Dictionary<ItemData, InventoryItem> dictionary, ItemData data)
    {
        if (!dictionary.TryGetValue(data, out InventoryItem item)) return;
        if (item.stackSize <= 1)
        {
            items.Remove(item);
            dictionary.Remove(data);
        }
        else item.RemoveStack();
    }

    private static void Add(List<InventoryItem> items, Dictionary<ItemData, InventoryItem> dictionary, ItemData data)
    {
        if (dictionary.TryGetValue(data, out InventoryItem existing)) existing.AddStack();
        else
        {
            var item = new InventoryItem(data);
            items.Add(item);
            dictionary.Add(data, item);
        }
    }
}
