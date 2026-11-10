using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Networking
{
    public sealed class MonsterAttackConfirmedHit
    {
        public EnemySkeleton Source { get; }
        public PrimaryAttackInstance Attack { get; }
        public int SegmentId { get; }
        public Player Target { get; }
        public int TargetEntityId { get; }
        public ulong AuthorityTick { get; }
        internal MonsterAttackConfirmedHit(EnemySkeleton source, PrimaryAttackInstance attack, int segment, Player target, int id, ulong tick)
        { Source = source; Attack = attack; SegmentId = segment; Target = target; TargetEntityId = id; AuthorityTick = tick; }
    }

    /// <summary>Monster windows use the same immutable segment ledger as player attacks; no client animation callback enters here.</summary>
    public sealed class MonsterAttackHitDetection
    {
        private readonly EnemySkeleton source;
        private readonly Func<long> allocateInstance;
        private readonly Func<Player, bool> validTarget;
        private readonly List<(Player target, int id)> candidates = new List<(Player, int)>();
        public PrimaryAttackInstance Current { get; private set; }
        public event Action<MonsterAttackConfirmedHit> HitConfirmed;
        public MonsterAttackHitDetection(EnemySkeleton source, Func<long> allocateInstance, Func<Player, bool> validTarget)
        {
            this.source = source != null ? source : throw new ArgumentNullException(nameof(source));
            this.allocateInstance = allocateInstance ?? throw new ArgumentNullException(nameof(allocateInstance));
            this.validTarget = validTarget ?? throw new ArgumentNullException(nameof(validTarget));
        }

        internal void Begin(ulong tick, int facing, PrimaryAttackTiming timing)
        {
            Reset();
            var identity = source != null ? EntityRuntimePermissions.Identity(source) : null;
            if (identity == null || !identity.IsAuthoritative || identity.PlayerId != 0 || tick == 0 || timing == null || !timing.IsValid) return;
            // Room-wide melee instance IDs are independent of the monster's animation actionId and result IDs.
            long id = allocateInstance();
            if (id <= 0) throw new InvalidOperationException("Invalid room melee instance ID.");
            Current = new PrimaryAttackInstance(identity.RoomSessionId, 0, identity.EntityId, id, 0,
                tick, checked(tick + (ulong)timing.DurationTicks), timing);
            Current.SetFacingAtStart(facing);
        }

        public void Step(ulong tick)
        {
            var attack = Current;
            var identity = source != null ? EntityRuntimePermissions.Identity(source) : null;
            if (source == null || !source.NetworkMovementOnly || !source.isActiveAndEnabled || identity == null || !identity.IsAuthoritative || identity.MovementHeld ||
                source.stats == null || !source.stats.IsRuntimeInitialized || source.stats.isDead || source.stats.currentHealth <= 0 ||
                source.NetworkAiState != Protocol.MonsterAiState.Attack || attack == null || attack.IsClosed ||
                attack.RoomSessionId != identity.RoomSessionId || attack.EntityId != identity.EntityId)
            { Reset(); return; }
            if (tick >= attack.EndTick) { Reset(); return; }
            var segment = attack.SegmentAt(tick);
            if (segment == null) return;
            source.FlipController(attack.FacingDir);
            candidates.Clear();
            AuthorityMeleeQuery.Collect<Player>(source, validTarget, (target, collider, id) => candidates.Add((target, id)));
            foreach (var candidate in candidates)
            {
                if (!attack.TryRegisterHit(segment.SegmentId, candidate.id, tick)) continue;
                Debug.Log($"[MonsterHit] CONFIRM source={attack.EntityId} attackInstanceId={attack.AttackInstanceId} segment={segment.SegmentId} targetPlayer={candidate.target.Combat.PlayerId} targetEntity={candidate.id} tick={tick}");
                HitConfirmed?.Invoke(new MonsterAttackConfirmedHit(source, attack, segment.SegmentId, candidate.target, candidate.id, tick));
            }
        }
        public void Reset() { Current?.Close(); Current = null; candidates.Clear(); }
    }
}
