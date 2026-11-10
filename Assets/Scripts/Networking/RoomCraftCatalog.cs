using System;
using System.Collections.Generic;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    /// <summary>A freezes the existing scene recipes; requests carry no costs, quantities or modifiers.</summary>
    public sealed class RoomCraftCatalog
    {
        public sealed class Recipe
        {
            internal RoomEquipmentDto Product;
            internal readonly Dictionary<string, int> Costs = new Dictionary<string, int>();
        }
        private readonly Dictionary<string, Recipe> recipes = new Dictionary<string, Recipe>();
        // Authored assets currently omit serialized itemId. Names + category form a build-stable catalog key.
        public static string RecipeId(ItemData_Equipment item) => item == null ? "" : "recipe/" + (int)item.equipmentType + "/" + item.name;
        public static string MaterialId(ItemData item) => item == null ? "" : "material/" + item.name;
        public RoomCraftCatalog(IEnumerable<ItemData_Equipment> products)
        {
            if (products == null) return;
            foreach (var product in products)
            {
                if (product == null) continue;
                string id = RecipeId(product);
                if (id.Length > 128 || recipes.ContainsKey(id)) throw new ArgumentException("Duplicate/invalid room recipe key: " + id);
                var recipe = new Recipe { Product = RoomCombatConfiguration.CaptureEquipment((RoomEquipmentSlot)product.equipmentType, product) };
                recipe.Product.definitionId = id;
                if (product.itemType != ItemType.Equipment || product.craftingMaterials == null) continue;
                bool valid = true;
                foreach (var cost in product.craftingMaterials)
                {
                    if (cost == null || cost.data == null || cost.data.itemType != ItemType.Material || cost.stackSize <= 0)
                    { valid = false; break; }
                    string material = MaterialId(cost.data);
                    recipe.Costs.TryGetValue(material, out int previous);
                    long total = (long)previous + cost.stackSize;
                    if (material.Length > 128 || total > 1000000) { valid = false; break; }
                    recipe.Costs[material] = (int)total; // Duplicate rows are summed before validating or deducting.
                }
                if (!valid) continue; // Malformed authored recipes are unavailable, never a free client-selected product.
                // Validate item shape against an ordinary room bag, before accepting it into A's catalog.
                var probe = RoomCombatConfiguration.CreateTestProfile(true);
                probe.inventory = new[] { recipe.Product };
                if (!ProtocolCodec.IsValidCombat(probe)) continue;
                recipes.Add(id, recipe);
            }
        }
        internal bool TryGet(string id, out Recipe recipe) => recipes.TryGetValue(id, out recipe);
        public static RoomMaterialDto[] StartingMaterials(IEnumerable<ItemData> startingItems)
        {
            var materials = new Dictionary<string, RoomMaterialDto>();
            if (startingItems != null) foreach (var item in startingItems)
            {
                if (item == null || item.itemType != ItemType.Material) continue;
                string id = MaterialId(item);
                if (!materials.TryGetValue(id, out var value))
                    materials.Add(id, value = new RoomMaterialDto { materialId = id, displayName = string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName });
                value.count++;
            }
            var result = new List<RoomMaterialDto>(materials.Values).ToArray();
            if (!ProtocolCodec.ValidMaterials(result)) throw new ArgumentException("Invalid room starting materials.");
            return result;
        }
    }
}
