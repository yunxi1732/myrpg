using System;
using System.Collections.Generic;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    /// <summary>A freezes authored names/costs/prerequisites/exclusions; UI flags and client messages cannot alter these rules.</summary>
    public sealed class RoomSkillTreeCatalog
    {
        internal sealed class Node
        {
            public string Id;
            public int Cost;
            public string[] Required, Excluded;
        }
        private readonly Dictionary<string, Node> nodes = new Dictionary<string, Node>();
        // These are the existing save/node IDs. Upgrade nodes are retained without enabling legacy skill effects.
        private static readonly Dictionary<string, RoomSkillId> baseNodes = new Dictionary<string, RoomSkillId>
        {
            { "Dash", RoomSkillId.Dash }, { "Time mirage", RoomSkillId.Clone }, { "Sword throw", RoomSkillId.Sword },
            { "Blackhole", RoomSkillId.Blackhole }, { "Crystal", RoomSkillId.Crystal }, { "Parry", RoomSkillId.Parry }, { "Dodge", RoomSkillId.Dodge }
        };
        public RoomSkillTreeCatalog(IEnumerable<UI_SkillTreeSlot> slots)
        {
            if (slots == null) return;
            foreach (var slot in slots)
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.RoomNodeId) || slot.RoomNodeId.Length > 128 || slot.RoomCost < 0 || nodes.ContainsKey(slot.RoomNodeId))
                    throw new ArgumentException("Invalid/duplicate room skill-tree node.");
                nodes.Add(slot.RoomNodeId, new Node { Id = slot.RoomNodeId, Cost = slot.RoomCost,
                    Required = Names(slot.RoomRequired), Excluded = Names(slot.RoomExcluded) });
            }
            foreach (var node in nodes.Values)
            {
                foreach (var id in node.Required) if (id == node.Id || !nodes.ContainsKey(id)) throw new ArgumentException("Invalid room skill prerequisite: " + node.Id);
                foreach (var id in node.Excluded) if (id == node.Id || !nodes.ContainsKey(id) || Array.IndexOf(node.Required, id) >= 0)
                    throw new ArgumentException("Invalid room skill exclusion: " + node.Id);
            }
        }
        private static string[] Names(IReadOnlyList<UI_SkillTreeSlot> slots)
        {
            var result = new HashSet<string>();
            if (slots != null) foreach (var slot in slots)
            { if (slot == null) throw new ArgumentException("Missing room skill-tree dependency."); result.Add(slot.RoomNodeId); }
            var values = new List<string>(result); values.Sort(StringComparer.Ordinal); return values.ToArray();
        }
        internal bool TryGet(string id, out Node value) => nodes.TryGetValue(id, out value);
        internal static bool BaseSkill(string id, out RoomSkillId skill) => baseNodes.TryGetValue(id, out skill);
        internal SkillTreeStateDto InitialState(PlayerCombatContext owner, int currency)
        {
            var value = new SkillTreeStateDto { currency = currency, nodes = new SkillTreeNodeDto[nodes.Count] };
            for (int i = 0; i < 7; i++) value.unlockedSkills[i] = owner.Skills.Get((RoomSkillId)i).Unlocked;
            var keys = new List<string>(nodes.Keys); keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                var node = nodes[keys[i]];
                value.nodes[i] = new SkillTreeNodeDto { nodeId = node.Id, cost = node.Cost,
                    unlocked = BaseSkill(node.Id, out var skill) && value.unlockedSkills[(int)skill] };
            }
            return value;
        }
    }
}
