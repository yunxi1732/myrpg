using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Networking
{
    /// <summary>A freezes pure healing definitions; mixed/unadapted item effects never execute through solo globals.</summary>
    public sealed class RoomFlaskCatalog
    {
        internal sealed class Definition
        {
            public float Cooldown;
            public float[] Percentages;
            public int Heal(int health, int maximum)
            {
                int result = health;
                foreach (float percent in Percentages) result += Math.Min(maximum - result, Mathf.RoundToInt(maximum * percent));
                return result;
            }
        }
        private readonly Dictionary<string, Definition> definitions = new Dictionary<string, Definition>();
        public RoomFlaskCatalog(IEnumerable<ItemData_Equipment> items)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item == null || item.equipmentType != EquipmentType.Flask || float.IsNaN(item.itemCooldown) || float.IsInfinity(item.itemCooldown) ||
                    item.itemCooldown < 0 || item.itemCooldown > 3600 || item.itemEffects == null || item.itemEffects.Length == 0) continue;
                var percentages = new List<float>(); bool valid = true;
                foreach (var effect in item.itemEffects)
                {
                    if (!(effect is HealEffect heal) || float.IsNaN(heal.RoomHealPercent) || float.IsInfinity(heal.RoomHealPercent) ||
                        heal.RoomHealPercent <= 0 || heal.RoomHealPercent > 1) { valid = false; break; }
                    percentages.Add(heal.RoomHealPercent);
                }
                if (valid) definitions.Add(RoomCraftCatalog.RecipeId(item), new Definition { Cooldown = item.itemCooldown, Percentages = percentages.ToArray() });
            }
        }
        internal bool TryGet(string id, out Definition definition) => definitions.TryGetValue(id, out definition);
    }
}
