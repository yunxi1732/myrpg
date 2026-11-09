using System;
using Rpg.Networking.Protocol;
using UnityEngine;

namespace Rpg.Networking
{
    /// <summary>Explicit first-round room fixtures. Only A chooses a fixture; B receives its full data in initialization.</summary>
    public static class RoomCombatConfiguration
    {
        private static readonly StatType[] RuntimeStats =
        {
            StatType.strength, StatType.agility, StatType.intelligence, StatType.vitality,
            StatType.damage, StatType.critChance, StatType.critPower, StatType.health,
            StatType.armor, StatType.evasion, StatType.magicRes, StatType.fireDamage, StatType.iceDamage, StatType.lightingDamage
        };
        public static Stat StatAt(PlayerStats stats, int index) => stats.GetStat(RuntimeStats[index]);

        public static PlayerCombatDto CreateTestProfile(bool host)
        {
            var value = new PlayerCombatDto
            {
                configurationId = host ? "room-test-v1/A" : "room-test-v1/B",
                // Final stats: strength, agility, intelligence, vitality, damage, crit chance/power,
                // health (before vitality), armor, evasion, resistance, fire/ice/lightning.
                stats = host ? new[] { 4, 0, 0, 4, 14, 0, 150, 120, 3, 0, 0, 0, 0, 0 }
                             : new[] { 7, 0, 0, 6, 28, 0, 150, 150, 6, 0, 0, 0, 0, 0 },
                equipment = new RoomEquipmentDto[4], skills = new RoomSkillDto[7]
            };
            for (int i = 0; i < 4; i++)
                value.equipment[i] = new RoomEquipmentDto { slot = (RoomEquipmentSlot)i, configurationId = "", modifiers = new int[14] };
            value.equipment[0].configurationId = host ? "test-weapon-a-v1" : "test-weapon-b-v1";
            value.equipment[0].modifiers[(int)CombatStatId.Damage] = host ? 3 : 7;
            value.equipment[1].configurationId = host ? "test-armor-a-v1" : "test-armor-b-v1";
            value.equipment[1].modifiers[(int)CombatStatId.Health] = host ? 20 : 30;
            value.equipment[1].modifiers[(int)CombatStatId.Armor] = host ? 3 : 6;
            float[] cooldowns = { host ? 1.5f : 2.5f, 4, 3, 8, 4, 2, 1 };
            for (int i = 0; i < 7; i++)
                value.skills[i] = new RoomSkillDto
                {
                    skillId = (RoomSkillId)i, cooldown = cooldowns[i], remaining = 0,
                    unlocked = i == (int)RoomSkillId.Dash || i == (int)(host ? RoomSkillId.Sword : RoomSkillId.Crystal)
                };
            return value;
        }

        public static void InitializeAuthority(Player player, bool host)
        {
            if (!player.IsAuthoritative || EntityRuntimePermissions.Identity(player)?.IsBound != true)
                throw new InvalidOperationException("Only a bound A authority chooses the room combat profile.");
            if (player.Combat.RoomConfigurationId != null) return; // A survives guest departure/rejoin.
            player.Combat.InstallRoomConfiguration(CreateTestProfile(host), false);
        }

        internal static EquipmentType EquipmentSlot(RoomEquipmentSlot slot)
        {
            switch (slot)
            {
                case RoomEquipmentSlot.Weapon: return EquipmentType.Weapon;
                case RoomEquipmentSlot.Armor: return EquipmentType.Armor;
                case RoomEquipmentSlot.Amulet: return EquipmentType.Amulet;
                case RoomEquipmentSlot.Flask: return EquipmentType.Flask;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }
        internal static ItemData_Equipment CreateEquipment(RoomEquipmentDto value)
        {
            if (value.configurationId.Length == 0) return null;
            var item = ScriptableObject.CreateInstance<ItemData_Equipment>();
            item.hideFlags = HideFlags.HideAndDontSave;
            item.name = value.configurationId; item.itemType = ItemType.Equipment; item.equipmentType = EquipmentSlot(value.slot);
            item.itemEffects = Array.Empty<ItemEffect>();
            int[] s = value.modifiers;
            item.strength = s[0]; item.agility = s[1]; item.intelligence = s[2]; item.vitality = s[3];
            item.damage = s[4]; item.critChance = s[5]; item.critPower = s[6]; item.health = s[7];
            item.armor = s[8]; item.evasion = s[9]; item.magicResistance = s[10];
            item.fireDamage = s[11]; item.iceDamage = s[12]; item.lightningDamage = s[13];
            return item;
        }
        internal static RoomEquipmentDto CaptureEquipment(RoomEquipmentSlot slot, ItemData_Equipment item) => new RoomEquipmentDto
        {
            slot = slot, configurationId = item == null ? "" : item.name,
            modifiers = item == null ? new int[14] : new[]
            {
                item.strength, item.agility, item.intelligence, item.vitality, item.damage, item.critChance, item.critPower,
                item.health, item.armor, item.evasion, item.magicResistance, item.fireDamage, item.iceDamage, item.lightningDamage
            }
        };

        public static float[] HudFills(PlayerCombatDto value)
        {
            RoomSkillId[] order = { RoomSkillId.Dash, RoomSkillId.Parry, RoomSkillId.Crystal, RoomSkillId.Sword, RoomSkillId.Blackhole };
            var fills = new float[5];
            for (int i = 0; i < order.Length; i++)
                foreach (var skill in value.skills)
                    if (skill.skillId == order[i]) fills[i] = skill.cooldown > 0 ? skill.remaining / skill.cooldown : 0;
            return fills;
        }
    }
}
