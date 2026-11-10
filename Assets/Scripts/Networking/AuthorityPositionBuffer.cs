using System;
using System.Collections.Generic;
using UnityEngine;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    /// <summary>A bounded position timeline in A's fixed simulation ticks, not packet arrival timestamps.</summary>
    public sealed class AuthorityPositionBuffer
    {
        private struct Sample { public ulong Tick; public Vector2 Position; public int Facing; public MovementAnimationState Movement; public float YVelocity; public MonsterAttackDto Attack; public PlayerAttackDto PlayerAttack; }
        private readonly List<Sample> samples = new List<Sample>(32);
        public int Count => samples.Count;
        public double RenderTick { get; private set; }
        public ulong LatestTick { get; private set; }
        public long LatestSequence { get; private set; }
        public Vector2 Position { get; private set; }
        public int FacingDirection { get; private set; } = 1;
        public MovementAnimationState MovementState { get; private set; }
        public float YVelocity { get; private set; }
        public bool Snapped { get; private set; }
        public MonsterAttackDto MonsterAttack { get; private set; }
        private MonsterAttackDto latestAttack;
        public PlayerAttackDto PlayerAttack { get; private set; }
        private PlayerAttackDto latestPlayerAttack;
        private long retiredPlayerAttack;

        public bool Add(ulong tick, long sequence, Vector2 position, double delayTicks, float snapDistance, bool reset = false, int facingDir = 1,
            MovementAnimationState movementState = MovementAnimationState.Idle, float yVelocity = 0, MonsterAttackDto monsterAttack = null, PlayerAttackDto playerAttack = null)
        {
            if (tick == 0 || sequence <= 0 || !ProtocolCodec.IsValidMovementAnimation(movementState, yVelocity) ||
                (facingDir != -1 && facingDir != 1) || float.IsNaN(position.x) || float.IsInfinity(position.x) ||
                float.IsNaN(position.y) || float.IsInfinity(position.y) || Count > 0 &&
                (sequence <= LatestSequence || tick < LatestTick || tick < RenderTick)) return false;
            if (playerAttack != null)
            {
                if (!ProtocolCodec.ValidPlayerAttack(playerAttack, playerAttack.sourceEntityId, facingDir, tick) ||
                    playerAttack.attackInstanceId <= retiredPlayerAttack || latestPlayerAttack != null && playerAttack.sourceEntityId != latestPlayerAttack.sourceEntityId || latestPlayerAttack != null &&
                    (playerAttack.attackInstanceId < latestPlayerAttack.attackInstanceId || playerAttack.attackInstanceId == latestPlayerAttack.attackInstanceId &&
                    (playerAttack.sourceEntityId != latestPlayerAttack.sourceEntityId || playerAttack.comboIndex != latestPlayerAttack.comboIndex ||
                    playerAttack.startTick != latestPlayerAttack.startTick || playerAttack.durationTicks != latestPlayerAttack.durationTicks || playerAttack.facingDir != latestPlayerAttack.facingDir))) return false;
                playerAttack = new PlayerAttackDto { attackInstanceId = playerAttack.attackInstanceId, sourceEntityId = playerAttack.sourceEntityId,
                    comboIndex = playerAttack.comboIndex, startTick = playerAttack.startTick, durationTicks = playerAttack.durationTicks, facingDir = playerAttack.facingDir };
            }
            if (monsterAttack != null)
            {
                if (!ProtocolCodec.IsValidMonsterAttack(monsterAttack, tick) || monsterAttack.facingDir != facingDir ||
                    latestAttack != null && (monsterAttack.actionId < latestAttack.actionId || monsterAttack.actionId == latestAttack.actionId &&
                    (monsterAttack.actionNumber != latestAttack.actionNumber || monsterAttack.startTick != latestAttack.startTick ||
                     monsterAttack.durationTicks != latestAttack.durationTicks || monsterAttack.facingDir != latestAttack.facingDir))) return false;
                // Keep samples independent of the caller's mutable receive DTO.
                monsterAttack = new MonsterAttackDto { actionId = monsterAttack.actionId, actionNumber = monsterAttack.actionNumber,
                    startTick = monsterAttack.startTick, durationTicks = monsterAttack.durationTicks, facingDir = monsterAttack.facingDir };
                latestAttack = monsterAttack;
            }
            if (playerAttack != null) latestPlayerAttack = playerAttack;
            else if (latestPlayerAttack != null) retiredPlayerAttack = latestPlayerAttack.attackInstanceId;
            bool hadSamples = Count > 0;
            double previousRenderTick = RenderTick;
            Snapped = reset || Count == 0 || (position - samples[Count - 1].Position).sqrMagnitude > snapDistance * snapDistance;
            LatestTick = tick; LatestSequence = sequence;
            if (Snapped)
            {
                samples.Clear();
                RenderTick = hadSamples ? Math.Max(previousRenderTick, (double)tick - Math.Max(0, delayTicks)) : (double)tick - Math.Max(0, delayTicks);
                Position = position;
                FacingDirection = facingDir;
                MovementState = movementState; YVelocity = yVelocity;
                MonsterAttack = monsterAttack;
                PlayerAttack = playerAttack;
            }
            var sample = new Sample { Tick = tick, Position = position, Facing = facingDir, Movement = movementState, YVelocity = yVelocity, Attack = monsterAttack, PlayerAttack = playerAttack };
            if (Count > 0 && samples[Count - 1].Tick == tick) samples[Count - 1] = sample;
            else samples.Add(sample);
            // Recover from long gaps without accumulating an ever-growing playback delay.
            RenderTick = Math.Max(RenderTick, (double)tick - Math.Max(0, delayTicks));
            while (Count > 32) samples.RemoveAt(0);
            return true;
        }

        public bool Advance(float unscaledDeltaTime, float tickSeconds)
        {
            if (Count == 0 || tickSeconds <= 0) return false;
            RenderTick = Math.Min(LatestTick, RenderTick + Math.Max(0, unscaledDeltaTime) / tickSeconds);
            // Keep the bracketing pair, discard consumed samples, and never extrapolate beyond A's latest state.
            while (Count > 1 && samples[1].Tick <= RenderTick) samples.RemoveAt(0);
            Sample first = samples[0];
            FacingDirection = first.Facing; // Discrete state: switch at the sample tick, never interpolate a direction through zero.
            MovementState = first.Movement; YVelocity = first.YVelocity;
            MonsterAttack = first.Attack;
            PlayerAttack = first.PlayerAttack;
            // A's next snapshot can reveal an action that began between position samples.
            // Start at its authority start tick, rather than delaying until that packet's sample tick.
            if (Count > 1 && samples[1].PlayerAttack != null && RenderTick >= samples[1].PlayerAttack.startTick)
                PlayerAttack = samples[1].PlayerAttack;
            if (PlayerAttack != null && RenderTick >= PlayerAttack.startTick && RenderTick - PlayerAttack.startTick < PlayerAttack.durationTicks)
                FacingDirection = PlayerAttack.facingDir;
            if (Count == 1 || RenderTick <= first.Tick) Position = first.Position;
            else
            {
                Sample next = samples[1];
                float alpha = (float)((RenderTick - first.Tick) / ((double)next.Tick - first.Tick));
                Position = Vector2.Lerp(first.Position, next.Position, alpha);
            }
            return true;
        }

        public void Clear()
        {
            samples.Clear();
            RenderTick = 0; LatestTick = 0; LatestSequence = 0;
            Position = Vector2.zero; Snapped = false;
            FacingDirection = 1;
            MovementState = MovementAnimationState.Idle; YVelocity = 0;
            MonsterAttack = null; latestAttack = null;
            PlayerAttack = null; latestPlayerAttack = null; retiredPlayerAttack = 0;
        }
    }
}
