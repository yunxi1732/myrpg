using System;
using System.Collections.Generic;

namespace Rpg.Networking
{
    public enum PrimaryAttackRejection { None, InvalidOperator, NotReady, InvalidSequence, Locked, Dead, Busy, ActionState, Interval, Configuration }

    public readonly struct PrimaryHitSegmentTiming
    {
        public int StartOffsetTicks { get; }
        public int EndOffsetTicks { get; }
        public PrimaryHitSegmentTiming(int start, int end) { StartOffsetTicks = start; EndOffsetTicks = end; }
    }

    /// <summary>Frozen authored timings. ComboIndex is the strike (0..2); segment IDs are marker order within that strike (1..N).</summary>
    public sealed class PrimaryAttackTiming
    {
        public int ComboIndex { get; }
        public int DurationTicks { get; }
        public IReadOnlyList<PrimaryHitSegmentTiming> Segments { get; }
        public PrimaryAttackTiming(int combo, int duration, params PrimaryHitSegmentTiming[] segments)
        {
            ComboIndex = combo; DurationTicks = duration;
            Segments = Array.AsReadOnly(segments == null ? Array.Empty<PrimaryHitSegmentTiming>() : (PrimaryHitSegmentTiming[])segments.Clone());
        }
        public bool IsValid
        {
            get
            {
                if (ComboIndex < 0 || ComboIndex > 2 || DurationTicks <= 0) return false;
                int previousEnd = 0;
                foreach (var segment in Segments)
                {
                    if (segment.StartOffsetTicks < previousEnd || segment.EndOffsetTicks <= segment.StartOffsetTicks || segment.EndOffsetTicks > DurationTicks) return false;
                    previousEnd = segment.EndOffsetTicks;
                }
                return true;
            }
        }
    }

    public sealed class PrimaryAttackHitSegment
    {
        public int SegmentId { get; }
        public ulong StartTick { get; }
        public ulong EndTick { get; }
        internal PrimaryAttackHitSegment(int id, ulong start, ulong end) { SegmentId = id; StartTick = start; EndTick = end; }
    }

    /// <summary>Authority-created execution metadata. Request sequence, attack instance ID and future resultId are separate domains.</summary>
    public sealed class PrimaryAttackInstance
    {
        public string RoomSessionId { get; }
        public int PlayerId { get; }
        public int EntityId { get; }
        public long AttackInstanceId { get; }
        public long RequestSeq { get; }
        public ulong StartTick { get; }
        public ulong EndTick { get; }
        public int ComboIndex { get; }
        public bool IsClosed { get; private set; }
        public int AcceptedHitCount { get; private set; }
        public IReadOnlyList<PrimaryAttackHitSegment> Segments { get; }
        private readonly HashSet<(int segment, int target)> hits = new HashSet<(int, int)>();
        private readonly HashSet<(int segment, int target)> damagedHits = new HashSet<(int, int)>();
        public ulong HitStartTick => Segments.Count > 0 ? Segments[0].StartTick : 0;
        public ulong HitEndTick => Segments.Count > 0 ? Segments[0].EndTick : 0;
        public bool IsHitWindowOpen(ulong tick) => SegmentAt(tick) != null;
        public PrimaryAttackHitSegment SegmentAt(ulong tick)
        {
            if (IsClosed || tick < StartTick || tick >= EndTick) return null;
            foreach (var segment in Segments) if (tick >= segment.StartTick && tick < segment.EndTick) return segment;
            return null;
        }
        internal bool TryRegisterHit(int segmentId, int targetEntityId, ulong tick)
        {
            if (targetEntityId <= 0 || SegmentAt(tick)?.SegmentId != segmentId || !hits.Add((segmentId, targetEntityId))) return false;
            AcceptedHitCount++; return true;
        }
        internal bool TryConsumeDamageHit(int segmentId, int targetEntityId) => !IsClosed &&
            hits.Contains((segmentId, targetEntityId)) && damagedHits.Add((segmentId, targetEntityId));
        internal void Close() { IsClosed = true; hits.Clear(); damagedHits.Clear(); }
        internal PrimaryAttackInstance(string room, int player, int entity, long id, long request, ulong start, ulong end, PrimaryAttackTiming timing)
        {
            RoomSessionId = room; PlayerId = player; EntityId = entity; AttackInstanceId = id; RequestSeq = request; StartTick = start; EndTick = end;
            ComboIndex = timing.ComboIndex;
            var segments = new PrimaryAttackHitSegment[timing.Segments.Count];
            for (int i = 0; i < segments.Length; i++)
                segments[i] = new PrimaryAttackHitSegment(i + 1, checked(start + (ulong)timing.Segments[i].StartOffsetTicks), checked(start + (ulong)timing.Segments[i].EndOffsetTicks));
            Segments = Array.AsReadOnly(segments);
        }
    }

    /// <summary>Per-actor request deduplication, one pending press and successful-attack interval, advanced only by A ticks.</summary>
    public sealed class PrimaryAttackRuntime
    {
        private readonly string room;
        private readonly int playerId, entityId;
        private readonly Func<long> allocateInstance;
        private long pendingSeq;
        private int nextComboIndex;
        private ulong lastCompletedTick;
        public long LastRequestSeq { get; private set; }
        public ulong NextAllowedTick { get; private set; }
        public bool HasPending => pendingSeq > 0;
        public PrimaryAttackInstance Current { get; private set; }
        public PrimaryAttackRejection LastRejection { get; private set; }

        public int PeekComboIndex(ulong tick, int comboWindowTicks) => comboWindowTicks >= 0 && lastCompletedTick > 0 &&
            tick >= lastCompletedTick && tick - lastCompletedTick <= (ulong)comboWindowTicks ? nextComboIndex : 0;

        public PrimaryAttackRuntime(string room, int player, int entity, Func<long> allocateInstance)
        {
            if (!Guid.TryParseExact(room, "N", out _) || player <= 0 || entity <= 0 || allocateInstance == null)
                throw new ArgumentException("Use the assigned authority actor and room attack-ID allocator.");
            this.room = room; playerId = player; entityId = entity; this.allocateInstance = allocateInstance;
        }
        public bool TryQueue(long requestSeq, ulong tick, PrimaryAttackRejection condition)
        {
            if (requestSeq <= 0 || requestSeq <= LastRequestSeq) return Reject(PrimaryAttackRejection.InvalidSequence);
            LastRequestSeq = requestSeq; // Consume even a rejected request; replay cannot become a later valid attack.
            if (condition != PrimaryAttackRejection.None) return Reject(condition);
            if (HasPending || Current != null && !Current.IsClosed && tick < Current.EndTick) return Reject(PrimaryAttackRejection.Busy);
            if (tick == 0) return Reject(PrimaryAttackRejection.NotReady);
            if (tick < NextAllowedTick) return Reject(PrimaryAttackRejection.Interval);
            pendingSeq = requestSeq; LastRejection = PrimaryAttackRejection.None;
            return true;
        }
        public bool TryBegin(ulong tick, PrimaryAttackRejection condition, int durationTicks, int intervalTicks, int hitStartTicks = 0, int hitEndTicks = 0)
        {
            if (hitStartTicks < 0 || hitEndTicks < 0 || (hitStartTicks != 0 || hitEndTicks != 0) && hitEndTicks <= hitStartTicks)
                return TryBegin(tick, PrimaryAttackRejection.Configuration, null, intervalTicks, 0);
            var segments = hitEndTicks > hitStartTicks ? new[] { new PrimaryHitSegmentTiming(hitStartTicks, hitEndTicks) } : Array.Empty<PrimaryHitSegmentTiming>();
            return TryBegin(tick, condition, new PrimaryAttackTiming(0, durationTicks, segments), intervalTicks, 0);
        }

        public bool TryBegin(ulong tick, PrimaryAttackRejection condition, PrimaryAttackTiming timing, int intervalTicks, int comboWindowTicks)
        {
            long request = pendingSeq; pendingSeq = 0; // Never buffer an invalid press until landing/unlock/cooldown expiry.
            if (request <= 0) return false;
            if (condition != PrimaryAttackRejection.None) return Reject(condition);
            if (tick == 0) return Reject(PrimaryAttackRejection.NotReady);
            if (Current != null && !Current.IsClosed && tick < Current.EndTick) return Reject(PrimaryAttackRejection.Busy);
            if (tick < NextAllowedTick) return Reject(PrimaryAttackRejection.Interval);
            if (timing == null || !timing.IsValid || intervalTicks <= 0 || comboWindowTicks < 0 || timing.ComboIndex != PeekComboIndex(tick, comboWindowTicks))
                return Reject(PrimaryAttackRejection.Configuration);
            ulong end = checked(tick + (ulong)timing.DurationTicks), next = checked(tick + (ulong)intervalTicks);
            long id = allocateInstance(); // Room monotonic ID; unrelated to request sequence and result IDs.
            if (id <= 0) throw new InvalidOperationException("Invalid attack instance ID.");
            Current?.Close(); // Only the current instance can admit hits; no room-wide history accumulates.
            Current = new PrimaryAttackInstance(room, playerId, entityId, id, request, tick, end, timing);
            NextAllowedTick = next; LastRejection = PrimaryAttackRejection.None;
            return true;
        }
        public void CancelPending() => pendingSeq = 0;
        public bool TryRegisterHit(PrimaryAttackInstance attack, int segmentId, int targetEntityId, ulong tick) =>
            ReferenceEquals(Current, attack) && attack != null && attack.TryRegisterHit(segmentId, targetEntityId, tick);

        public void FinishCurrent(PrimaryAttackInstance attack, ulong tick, bool completed)
        {
            if (attack == null || !ReferenceEquals(Current, attack) || attack.IsClosed) return;
            attack.Close();
            if (completed && tick >= attack.EndTick)
            { lastCompletedTick = tick; nextComboIndex = (attack.ComboIndex + 1) % 3; }
            else { lastCompletedTick = 0; nextComboIndex = 0; }
        }
        public void InterruptCurrent()
        {
            CancelPending(); FinishCurrent(Current, 0, false);
            lastCompletedTick = 0; nextComboIndex = 0;
        }
        private bool Reject(PrimaryAttackRejection reason) { LastRejection = reason; return false; }
    }
}
