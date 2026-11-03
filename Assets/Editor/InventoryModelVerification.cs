using System;
using UnityEditor;
using UnityEngine;

public static class InventoryModelVerification
{
    [MenuItem("Tools/RPG/Verify Inventory Model (Play Mode)")]
    public static void Verify()
    {
        Require(Application.isPlaying, "Enter Play mode first");
        Inventory inventory = Inventory.instance;
        Require(inventory != null && inventory.Model != null, "Wait for Inventory.Start");
        Require(inventory.IsInitialized, "Wait for save loading and initial item population");
        Require(ReferenceEquals(inventory.inventory, inventory.Model.Items), "List must alias Model.Items");
        Require(ReferenceEquals(inventory.inventoryDictionary, inventory.Model.ItemDictionary), "Dictionary must alias Model.ItemDictionary");
        Require(inventory.inventory.Count == inventory.inventoryDictionary.Count, "Collection counts agree");
        foreach (InventoryItem item in inventory.inventory)
        {
            Require(item != null && item.data != null && item.stackSize > 0, "Valid live stack");
            Require(inventory.inventoryDictionary.TryGetValue(item.data, out InventoryItem indexed)
                && ReferenceEquals(item, indexed), "List and dictionary share each item instance");
        }
        Require(ReferenceEquals(inventory.stash, inventory.Model.Stash), "Stash must alias Model.Stash");
        Require(ReferenceEquals(inventory.stashDictionary, inventory.Model.StashDictionary), "Stash dictionary must alias Model.StashDictionary");
        Require(ReferenceEquals(inventory.GetStashList(), inventory.Model.Stash), "Public stash getter uses Model data");
        Require(inventory.stash.Count == inventory.stashDictionary.Count, "Stash collection counts agree");
        foreach (InventoryItem item in inventory.stash)
        {
            Require(item != null && item.data != null && item.stackSize > 0, "Valid material stack");
            Require(inventory.stashDictionary.TryGetValue(item.data, out InventoryItem indexed)
                && ReferenceEquals(item, indexed), "Stash list and dictionary share each item instance");
        }
        Require(ReferenceEquals(inventory.equipment, inventory.Model.Equipment), "Equipment must alias Model.Equipment");
        Require(ReferenceEquals(inventory.equipmentDictionary, inventory.Model.EquipmentDictionary), "Equipment dictionary must alias Model data");
        Require(ReferenceEquals(inventory.GetEquipmentList(), inventory.Model.Equipment), "Public equipment getter uses Model data");
        Require(inventory.equipment.Count == inventory.equipmentDictionary.Count, "Equipment collection counts agree");
        var equippedTypes = new System.Collections.Generic.HashSet<EquipmentType>();
        foreach (InventoryItem item in inventory.equipment)
        {
            Require(item != null && item.data is ItemData_Equipment && item.stackSize == 1, "Valid equipped item");
            var data = (ItemData_Equipment)item.data;
            Require(inventory.equipmentDictionary.TryGetValue(data, out InventoryItem indexed)
                && ReferenceEquals(item, indexed), "Equipment list and dictionary share each item instance");
            Require(equippedTypes.Add(data.equipmentType), "Only one equipped item per type");
            Require(ReferenceEquals(inventory.GetEquipment(data.equipmentType), data), "Equipped item query matches Model");
        }
        foreach (EquipmentType type in Enum.GetValues(typeof(EquipmentType)))
        {
            if (!equippedTypes.Contains(type))
                Require(inventory.GetEquipment(type) == null, "Empty equipment type returns null");
        }
        Debug.Log("[InventoryModel] PASS: shared data and equipment queries, inventory stacks="
            + inventory.inventory.Count + ", material stacks=" + inventory.stash.Count
            + ", equipped items=" + inventory.equipment.Count);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("[InventoryModel] FAIL: " + message);
    }
}
