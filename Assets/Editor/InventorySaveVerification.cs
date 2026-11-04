using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class InventorySaveVerification
{
    [MenuItem("Tools/RPG/Verify Inventory Save Mapping")]
    public static void Verify()
    {
        var equipment = ScriptableObject.CreateInstance<ItemData_Equipment>();
        var material = ScriptableObject.CreateInstance<ItemData>();
        try
        {
            equipment.itemId = "mapping-equipment";
            equipment.itemType = ItemType.Equipment;
            material.itemId = "mapping-material";
            material.itemType = ItemType.Material;
            var original = new GameData();
            original.inventory.Add(equipment.itemId, 3);
            original.inventory.Add(material.itemId, 7);
            original.equipmentId.Add(equipment.itemId);
            var database = new List<ItemData> { equipment, material };
            var items = new List<InventoryItem>();
            var equipped = new List<ItemData_Equipment>();
            InventorySaveMapper.Read(original, database, items, equipped);
            InventorySaveMapper.Read(original, database, items, equipped);
            Require(items.Count == 2 && equipped.Count == 1 && ReferenceEquals(equipped[0], equipment), "Repeat mapping does not accumulate equipment or items");
            var model = new InventoryModel();
            var system = new InventorySystem(model);
            foreach (var item in items)
                for (int i = 0; i < item.stackSize; i++) system.AddItem(item.data, 10);
            // Mapping test only: no player or equipment effects are invoked.
            var equippedItem = new InventoryItem(equipped[0]);
            model.Equipment.Add(equippedItem);
            model.EquipmentDictionary.Add(equipped[0], equippedItem);
            var saved = new GameData();
            saved.currency = 123;
            InventorySaveMapper.Write(model, saved);
            var restarted = JsonUtility.FromJson<GameData>(JsonUtility.ToJson(saved));
            InventorySaveMapper.Read(restarted, database, items, equipped);
            Require(restarted.inventory.Count == 2 && restarted.inventory[equipment.itemId] == 3
                && restarted.inventory[material.itemId] == 7 && equipped.Count == 1
                && restarted.equipmentId[0] == equipment.itemId && restarted.currency == 123,
                "Save/JSON/load preserves IDs, quantities, equipment and unrelated fields");
            Require(items.Count == 2 && items.Find(x => x.data == material).stackSize == 7,
                "Reload restores stack quantities");
            Debug.Log("[InventorySaveMapping] PASS: repeat mapping and serialized round trip; player attributes require Play mode verification");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(equipment);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("[InventorySaveMapping] FAIL: " + message);
    }
}
