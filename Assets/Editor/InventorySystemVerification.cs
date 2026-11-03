using System;
using UnityEditor;
using UnityEngine;

public static class InventorySystemVerification
{
    [MenuItem("Tools/RPG/Verify Inventory System")]
    public static void Verify()
    {
        var equipment = ScriptableObject.CreateInstance<ItemData>();
        var other = ScriptableObject.CreateInstance<ItemData>();
        var material = ScriptableObject.CreateInstance<ItemData>();
        var product = ScriptableObject.CreateInstance<ItemData_Equipment>();
        try
        {
            equipment.itemType = other.itemType = ItemType.Equipment;
            material.itemType = ItemType.Material;
            using (var architecture = new Architecture())
            {
                var model = new InventoryModel();
                var system = new InventorySystem(model);
                architecture.RegistModel<InventoryModel>(model);
                architecture.RegistSystem<InventorySystem>(system);
                architecture.InitAllModules();
                Require(system.AddItem(equipment, 2) && system.AddItem(equipment, 2), "Equipment addition and stacking");
                Require(model.Items.Count == 1 && model.ItemDictionary[equipment].stackSize == 2
                    && ReferenceEquals(model.Items[0], model.ItemDictionary[equipment]), "Shared equipment stack");
                Require(system.AddItem(other, 2) && !system.CanAddItem(2), "Full inventory");
                Require(!system.AddItem(equipment, 2) && model.ItemDictionary[equipment].stackSize == 2, "Full inventory rejects existing equipment stack");
                Require(system.AddItem(material, 2) && system.AddItem(material, 2), "Materials accepted when inventory full");
                Require(model.Stash.Count == 1 && model.StashDictionary[material].stackSize == 2
                    && ReferenceEquals(model.Stash[0], model.StashDictionary[material]), "Shared material stack");
                Require(model.Items.Count == 2 && !model.ItemDictionary.ContainsKey(material)
                    && !model.StashDictionary.ContainsKey(equipment), "Item routing");
                system.RemoveItem(equipment);
                Require(model.ItemDictionary[equipment].stackSize == 1
                    && ReferenceEquals(model.Items[0], model.ItemDictionary[equipment]), "Equipment decrements shared stack");
                system.RemoveItem(equipment);
                Require(!model.ItemDictionary.ContainsKey(equipment) && model.Items.Count == 1
                    && ReferenceEquals(model.Items[0], model.ItemDictionary[other]), "Last equipment removed from both collections");
                system.RemoveItem(equipment);
                Require(model.Items.Count == 1 && model.ItemDictionary[other].stackSize == 1, "Missing equipment removal leaves other stacks unchanged");
                system.RemoveItem(material);
                Require(model.StashDictionary[material].stackSize == 1
                    && ReferenceEquals(model.Stash[0], model.StashDictionary[material]), "Material decrements shared stack");
                system.RemoveItem(material);
                system.RemoveItem(material);
                Require(model.Stash.Count == 0 && model.StashDictionary.Count == 0, "Last material removed from both collections");
            }
            product.itemType = ItemType.Equipment;
            other.itemType = ItemType.Material;
            var craftModel = new InventoryModel();
            var craftSystem = new InventorySystem(craftModel);
            var recipe = new System.Collections.Generic.List<InventoryItem>
            {
                new InventoryItem(material) { stackSize = 3 },
                new InventoryItem(other) { stackSize = 2 }
            };
            for (int i = 0; i < 4; i++) craftSystem.AddItem(material, 2);
            Require(!craftSystem.CanCraft(product, recipe, 2, out bool added)
                && !added && craftModel.StashDictionary[material].stackSize == 4
                && craftModel.Items.Count == 0, "Missing later material changes nothing");
            craftSystem.AddItem(other, 2);
            Require(!craftSystem.CanCraft(product, recipe, 2, out added)
                && craftModel.StashDictionary[material].stackSize == 4
                && craftModel.StashDictionary[other].stackSize == 1, "Insufficient quantity changes nothing");
            craftSystem.AddItem(other, 2);
            Require(craftSystem.CanCraft(product, recipe, 2, out added) && added, "Craft succeeds");
            Require(craftModel.StashDictionary[material].stackSize == 1
                && !craftModel.StashDictionary.ContainsKey(other) && craftModel.Stash.Count == 1
                && craftModel.ItemDictionary[product].stackSize == 1, "Exact consumption and product");
            Require(recipe[0].stackSize == 3 && recipe[1].stackSize == 2, "Recipe unchanged");
            var cooldownModel = new InventoryModel();
            var cooldownSystem = new InventorySystem(cooldownModel);
            Require(!cooldownSystem.UseFlask(1) && !cooldownSystem.CanUseArmor(1), "No equipped item cannot trigger");
            product.equipmentType = EquipmentType.Flask;
            product.itemCooldown = 5;
            product.itemEffects = new ItemEffect[0];
            cooldownModel.EquipmentDictionary.Add(product, new InventoryItem(product));
            Require(!cooldownSystem.UseFlask(0) && cooldownSystem.UseFlask(1)
                && cooldownSystem.FlaskCooldown == 5, "Flask initial use and duration");
            Require(!cooldownSystem.UseFlask(2) && !cooldownSystem.UseFlask(6)
                && cooldownSystem.UseFlask(6.1f), "Flask cooldown and strict boundary");
            product.equipmentType = EquipmentType.Armor;
            Require(cooldownSystem.CanUseArmor(1) && !cooldownSystem.CanUseArmor(2)
                && !cooldownSystem.CanUseArmor(6) && cooldownSystem.CanUseArmor(6.1f), "Armor cooldown independent of flask and strict boundary");
            Require(new InventorySystem(new InventoryModel()).FlaskCooldown == 0, "New system has fresh cooldown state");
            Debug.Log("[InventorySystem] PASS: flask and armor cooldowns, crafting, routing, stacking, capacity and removal consistency");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(equipment);
            UnityEngine.Object.DestroyImmediate(other);
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(product);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("[InventorySystem] FAIL: " + message);
    }
}
