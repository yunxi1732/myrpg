using System;
using System.Collections.Generic;
using System.Threading;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    /// <summary>One A-owned sequence for every result kind in the room, retained across guest departures.</summary>
    public sealed class AuthorityResultSequence : IDisposable
    {
        public string RoomSessionId { get; }
        public long LastAllocated { get; private set; }
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private bool disposed;
        public AuthorityResultSequence(string room)
        {
            if (!Guid.TryParseExact(room, "N", out _)) throw new ArgumentException("Use the live room ID.");
            RoomSessionId = room;
        }
        public long Allocate()
        {
            if (disposed) throw new ObjectDisposedException(nameof(AuthorityResultSequence));
            if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("Allocate results on A's main thread.");
            return LastAllocated = checked(LastAllocated + 1);
        }
        public void Dispose() => disposed = true;
    }
    /// <summary>Exact room-scoped receipts for all result kinds. Gaps/out-of-order IDs never suppress unrelated results.</summary>
    public sealed class AuthorityResultReceipts
    {
        private readonly string room;
        private readonly HashSet<long> seen = new HashSet<long>();
        public int Count => seen.Count;
        public AuthorityResultReceipts(string room)
        {
            if (!Guid.TryParseExact(room, "N", out _)) throw new ArgumentException("Use the admitted room ID.");
            this.room = room;
        }
        public bool TryAccept(AuthorityResultDto result) => result != null && result.roomSessionId == room &&
            result.authorityTick > 0 && result.resultId > 0 && seen.Add(result.resultId);
        public void Clear() => seen.Clear();
    }
    public struct PhysicalDamageInput
    {
        public int damage, strength, armor, critChance, critPower, agility, evasion, targetAgility, health;
        public bool shocked, chilled, vulnerable, invincible;
    }
    public readonly struct PhysicalDamageOutcome
    {
        public int ActualDamage { get; }
        public int FinalHealth { get; }
        public bool Critical { get; }
        public bool Evaded { get; }
        internal PhysicalDamageOutcome(int damage, int health, bool critical, bool evaded)
        { ActualDamage = damage; FinalHealth = health; Critical = critical; Evaded = evaded; }
    }
    public static class PhysicalDamageRules
    {
        // Existing flat-armor/strength/critical-power rules. Network 0%/100% chances are exact; solo code is unchanged.
        public static PhysicalDamageOutcome Calculate(PhysicalDamageInput input, Func<int> rollPercent)
        {
            if (input.health <= 0 || input.invincible) return new PhysicalDamageOutcome(0, Math.Max(0, input.health), false, false);
            int dodge = Chance((long)input.evasion + input.targetAgility + (input.shocked ? 20 : 0));
            if (rollPercent() < dodge) return new PhysicalDamageOutcome(0, input.health, false, true);
            bool critical = rollPercent() < Chance((long)input.critChance + input.agility);
            int damage = Clamp((long)input.damage + input.strength);
            if (critical) damage = Round(damage * Math.Max(0, (long)input.critPower + input.strength) * .01d);
            int armor = input.chilled ? Round(Math.Max(0, input.armor) * .8d) : Math.Max(0, input.armor);
            damage = Clamp((long)damage - armor);
            if (input.vulnerable) damage = Round(damage * 1.1d);
            damage = Math.Min(input.health, damage);
            return new PhysicalDamageOutcome(damage, input.health - damage, critical, false);
        }
        private static int Chance(long value) => (int)Math.Max(0, Math.Min(100, value));
        private static int Clamp(long value) => (int)Math.Max(0, Math.Min(int.MaxValue, value));
        private static int Round(double value) => (int)Math.Max(0, Math.Min(int.MaxValue, Math.Round(value, MidpointRounding.ToEven)));
    }
}
