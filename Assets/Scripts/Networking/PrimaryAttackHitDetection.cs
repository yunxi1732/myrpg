using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Networking
{
    public sealed class PrimaryAttackHitCandidate
    {
        public Enemy Target { get; }
        public Collider2D Collider { get; }
        public int EntityId { get; }
        internal PrimaryAttackHitCandidate(Enemy target, Collider2D collider, int id)
        { Target = target; Collider = collider; EntityId = id; }
    }

    /// <summary>Local authority input to damage resolution; not a wire result and has no resultId.</summary>
    public sealed class PrimaryAttackConfirmedHit
    {
        public PrimaryAttackInstance Attack { get; }
        public int SegmentId { get; }
        public int TargetEntityId { get; }
        public Enemy Target { get; }
        public ulong AuthorityTick { get; }
        internal PrimaryAttackConfirmedHit(PrimaryAttackInstance attack, int segment, PrimaryAttackHitCandidate candidate, ulong tick)
        { Attack = attack; SegmentId = segment; TargetEntityId = candidate.EntityId; Target = candidate.Target; AuthorityTick = tick; }
    }

    /// <summary>A's window/geometry query and hit admission; confirmed hits route synchronously to room damage authority.</summary>
    public sealed class PrimaryAttackHitDetection
    {
        private readonly PlayerCombatContext context;
        private readonly Func<Enemy, bool> validTarget;
        private readonly List<PrimaryAttackHitCandidate> candidates = new List<PrimaryAttackHitCandidate>();
        private readonly List<PrimaryAttackConfirmedHit> confirmedHits = new List<PrimaryAttackConfirmedHit>();
        private long openAttack;
        private int openSegment;
        public IReadOnlyList<PrimaryAttackHitCandidate> LastCandidates => candidates;
        public IReadOnlyList<PrimaryAttackConfirmedHit> LastConfirmedHits => confirmedHits;
        public event Action<PrimaryAttackConfirmedHit> HitConfirmed;
        public PrimaryAttackHitDetection(PlayerCombatContext context, Func<Enemy, bool> validTarget)
        { this.context = context ?? throw new ArgumentNullException(nameof(context)); this.validTarget = validTarget ?? throw new ArgumentNullException(nameof(validTarget)); }

        public void Step(ulong tick)
        {
            candidates.Clear();
            confirmedHits.Clear();
            Player source = context.Owner;
            var attack = context.PrimaryAttack?.Current;
            bool open = GameSimulationClock.IsNetwork && source != null && source.IsAuthoritative && source.isActiveAndEnabled && source.IsRuntimeInitialized &&
                EntityRuntimePermissions.Identity(source)?.MovementHeld != true &&
                source.stats != null && source.stats.IsRuntimeInitialized && !source.stats.isDead && source.stats.currentHealth > 0 &&
                source.stateMachine?.currentState == source.primaryAttackState && attack != null &&
                attack.RoomSessionId == context.RoomSessionId && attack.PlayerId == context.PlayerId && attack.EntityId == context.EntityId &&
                attack.IsHitWindowOpen(tick);
            if (!open) { Close(tick); return; }
            var segment = attack.SegmentAt(tick);
            if (openAttack != attack.AttackInstanceId || openSegment != segment.SegmentId)
            {
                Close(tick); openAttack = attack.AttackInstanceId; openSegment = segment.SegmentId;
                Debug.Log($"[PrimaryHit] OPEN attackInstanceId={openAttack} combo={attack.ComboIndex + 1} segment={openSegment} tick={tick} closeTick={segment.EndTick} player={context.PlayerId}");
            }
            CollectCandidates(source, validTarget, candidates);
            foreach (var candidate in candidates)
            {
                // The ledger belongs to the instance, not this sample/collider or window-open notification.
                if (!context.PrimaryAttack.TryRegisterHit(attack, segment.SegmentId, candidate.EntityId, tick)) continue;
                var hit = new PrimaryAttackConfirmedHit(attack, segment.SegmentId, candidate, tick);
                confirmedHits.Add(hit);
                Debug.Log($"[PrimaryHit] CONFIRM attackInstanceId={attack.AttackInstanceId} combo={attack.ComboIndex + 1} segment={segment.SegmentId} tick={tick} player={context.PlayerId} targetEntity={candidate.EntityId}; deduplicated.");
                HitConfirmed?.Invoke(hit);
            }
        }

        private void Close(ulong tick)
        {
            if (openAttack > 0) Debug.Log($"[PrimaryHit] CLOSE attackInstanceId={openAttack} segment={openSegment} tick={tick}");
            openAttack = 0; openSegment = 0;
        }
        public void Reset() { candidates.Clear(); confirmedHits.Clear(); openAttack = 0; openSegment = 0; }

        // Shared query for runtime and preview validation. It cannot apply damage or create authoritative results.
        // Raw geometry retains collider samples; Step admits each target only once per attack instance and segment.
        public static void CollectCandidates(Player source, Func<Enemy, bool> validTarget, List<PrimaryAttackHitCandidate> output)
        {
            output.Clear();
            if (source == null || EntityRuntimePermissions.Identity(source)?.PlayerId <= 0 || validTarget == null) return;
            AuthorityMeleeQuery.Collect<Enemy>(source, target => EntityRuntimePermissions.Identity(target)?.PlayerId == 0 && validTarget(target),
                (target, collider, id) => output.Add(new PrimaryAttackHitCandidate(target, collider, id)));
        }
    }
}
