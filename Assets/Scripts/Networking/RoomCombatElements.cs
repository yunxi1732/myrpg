using System;
using System.Collections.Generic;
using Rpg.Networking.Protocol;
using UnityEngine;

namespace Rpg.Networking
{
    public sealed partial class RoomCombatDamage
    {
        private sealed class ElementRecord
        {
            public ElementStateDto State;
            public long AttackId;
            public int Segment, Ordinal;
            public ulong NextTick;
        }
        private readonly Dictionary<int, ElementRecord> effects = new Dictionary<int, ElementRecord>();
        private readonly HashSet<int> displayedElements = new HashSet<int>();
        private readonly Action<ElementStatesDto> publishElements;
        private long nextElementId, elementStateSeq, receivedElementSeq;
        private ulong lastElementStep, receivedElementTick;

        // Same strict-largest / ordered random tie selection as the existing AttemptToApplyElements.
        private static ElementEffectKind? SelectElement(int fire, int ice, int lightning)
        {
            if (Math.Max(fire, Math.Max(ice, lightning)) <= 0) return null;
            if (fire > ice && fire > lightning) return ElementEffectKind.Burn;
            if (ice > fire && ice > lightning) return ElementEffectKind.Chill;
            if (lightning > fire && lightning > ice) return ElementEffectKind.Shock;
            while (true)
            {
                if (UnityEngine.Random.value < .3f && fire > 0) return ElementEffectKind.Burn;
                if (UnityEngine.Random.value < .5f && ice > 0) return ElementEffectKind.Chill;
                if (UnityEngine.Random.value < 1f && lightning > 0) return ElementEffectKind.Shock;
            }
        }

        private void TryApplyElement(Entity source, Entity target, PrimaryAttackInstance attack, int segment, ulong tick, DirectDamageDto input)
        {
            var stats = target.GetComponent<CharaterStats>();
            if (stats.isIgnited || stats.isChilled) return;
            var kind = SelectElement(input.rawFire, input.rawIce, input.rawLightning);
            if (!kind.HasValue) return;
            if (stats.isShocked)
            {
                if (kind == ElementEffectKind.Shock && target is Enemy origin && effects.TryGetValue(EntityRuntimePermissions.Identity(target).EntityId, out var existing))
                    StartShockChain(origin, existing, tick);
                return;
            }
            StartElement(target, kind.Value, attack.PlayerId, attack.EntityId, attack.AttackInstanceId, segment, tick,
                kind == ElementEffectKind.Burn ? DamageCalculations.IgniteTickDamage(input.rawFire) : kind == ElementEffectKind.Shock ? DamageCalculations.ShockChainDamage(input.rawLightning) : 0);
        }

        private void StartElement(Entity target, ElementEffectKind kind, int playerId, int sourceId, long attackId, int segment, ulong tick, int tickDamage)
        {
            var stats = target.GetComponent<CharaterStats>();
            if (!(stats.IgniteDuration > 0) || stats.IgniteDuration > 3600 || !(stats.IgniteInterval > 0) || stats.IgniteInterval > 3600) return;
            // Tolerance removes float representation noise (0.3f / 0.02f must be 15, not 16 ticks).
            int duration = Mathf.Max(1, Mathf.CeilToInt(stats.IgniteDuration / GameSimulationClock.FixedDeltaTime - .00001f));
            int interval = Mathf.Max(1, Mathf.CeilToInt(stats.IgniteInterval / GameSimulationClock.FixedDeltaTime - .00001f));
            int id = EntityRuntimePermissions.Identity(target).EntityId;
            var state = new ElementStateDto { effectId = checked(++nextElementId), kind = kind, sourcePlayerId = playerId, sourceEntityId = sourceId,
                targetEntityId = id, active = true, startTick = tick, endTick = checked(tick + (ulong)duration), intervalTicks = interval,
                tickDamage = tickDamage,
                movementMultiplier = kind == ElementEffectKind.Chill ? DamageCalculations.ChillMultiplier : 1,
                armorMultiplier = kind == ElementEffectKind.Chill ? DamageCalculations.ChillMultiplier : 1 };
            effects[id] = new ElementRecord { State = state, AttackId = attackId, Segment = segment, NextTick = tick + 1 };
            SetElementState(id, state);
            PublishElementSnapshot(tick);
            Debug.Log($"[Element] START kind={state.kind} effectId={state.effectId} source={state.sourceEntityId} target={id} tick={tick} end={state.endTick} move={state.movementMultiplier} armor={state.armorMultiplier}");
        }

        public void AdvanceElements(ulong tick)
        {
            if (disposed || context.IsGuest || tick == 0 || tick <= lastElementStep) return;
            lastElementStep = tick;
            foreach (var record in new List<ElementRecord>(effects.Values))
            {
                var state = record.State;
                if (!state.active)
                {
                    if (!context.TryGetEntity(state.targetEntityId, out _)) { effects.Remove(state.targetEntityId); displayedElements.Remove(state.targetEntityId); }
                    continue;
                }
                if (!context.TryGetEntity(state.targetEntityId, out var target) || !context.TryGetEntity(state.sourceEntityId, out _))
                { EndElement(state.targetEntityId, tick); continue; }
                var stats = target.View.GetComponent<CharaterStats>();
                if (stats == null || !stats.IsAuthoritative || !stats.IsRuntimeInitialized || stats.currentHealth <= 0 || !target.View.gameObject.activeInHierarchy)
                { EndElement(state.targetEntityId, tick); continue; }
                // All due ticks are settled by A, never by an FX callback. The end boundary is exclusive.
                while (state.kind == ElementEffectKind.Burn && record.NextTick <= tick && record.NextTick < state.endTick && state.active)
                {
                    record.NextTick += (ulong)state.intervalTicks; record.Ordinal++;
                    int damage = Math.Min(stats.currentHealth, DamageCalculations.ApplyVulnerability(state.tickDamage, stats.isVulnerable));
                    int health = stats.currentHealth - damage;
                    var result = new DamageResultDto { roomSessionId = context.RoomSessionId, authorityTick = tick, resultId = sequence.Allocate(),
                        kind = DamageResultKind.Burn, effectInstanceId = state.effectId, effectTick = record.Ordinal,
                        sourcePlayerId = state.sourcePlayerId, sourceEntityId = state.sourceEntityId, targetEntityId = state.targetEntityId,
                        attackInstanceId = record.AttackId, hitSegmentId = record.Segment, actualDamage = damage, finalHealth = health,
                        maxHealth = stats.GetMaxHealthValue(), targetStateVersion = stats.ApplyAuthorityNetworkHealth(health),
                        components = new DirectDamageDto { burnDamage = damage } };
                    ProtocolCodec.ValidateMessage(result, true);
                    target.ApplyVersionedHealth(result.roomSessionId, tick, result.targetStateVersion, health, result.maxHealth);
                    lastAuthorityResult = result; PresentOnce(result, target.View); publish?.Invoke(result);
                    Debug.Log($"[Burn] TICK burnId={state.effectId} jump={record.Ordinal} resultId={result.resultId} target={state.targetEntityId} tick={tick} damage={damage} hp={health}");
                    if (health == 0)
                    {
                        if (target.View.GetComponent<Player>() is Player player) player.HoldNetworkAtZeroHealth();
                        else target.View.GetComponent<Enemy>()?.HoldNetworkAtZeroHealth();
                        EndElement(state.targetEntityId, tick);
                    }
                }
                if (state.active && tick >= state.endTick) EndElement(state.targetEntityId, tick);
            }
            AdvanceShockChains(tick);
        }

        private void EndElement(int target, ulong tick)
        {
            if (!effects.TryGetValue(target, out var record) || !record.State.active) return;
            record.State.active = false; SetElementState(target, null); PublishElementSnapshot(tick);
            Debug.Log($"[Element] END kind={record.State.kind} effectId={record.State.effectId} target={target} tick={tick}");
        }

        public void PublishElementBaseline(ulong tick)
        {
            if (disposed || context.IsGuest) return;
            AdvanceElements(tick); PublishElementSnapshot(tick);
        }
        public void RemovePlayerElements(int playerId, ulong tick)
        {
            if (disposed || context.IsGuest) return;
            foreach (var record in new List<ElementRecord>(effects.Values))
                if (record.State.sourcePlayerId == playerId || context.TryGetEntity(record.State.targetEntityId, out var target) && target.PlayerId == playerId)
                    EndElement(record.State.targetEntityId, tick);
            RemovePlayerChains(playerId, tick);
        }
        private void PublishElementSnapshot(ulong tick)
        {
            var states = new List<ElementStateDto>();
            foreach (var record in effects.Values)
            {
                var s = record.State;
                if (!context.TryGetEntity(s.targetEntityId, out _)) continue;
                states.Add(new ElementStateDto { effectId = s.effectId, kind = s.kind, movementMultiplier = s.movementMultiplier, armorMultiplier = s.armorMultiplier,
                    sourcePlayerId = s.sourcePlayerId, sourceEntityId = s.sourceEntityId,
                    targetEntityId = s.targetEntityId, active = s.active && tick < s.endTick, startTick = s.startTick, endTick = s.endTick,
                    intervalTicks = s.intervalTicks, tickDamage = s.tickDamage });
            }
            var result = new ElementStatesDto { roomSessionId = context.RoomSessionId, authorityTick = tick,
                resultId = sequence.Allocate(), stateSeq = ++elementStateSeq, effects = states.ToArray(), chains = CaptureChains() };
            ProtocolCodec.ValidateMessage(result, true); publishElements?.Invoke(result);
        }

        public bool ApplyElementStates(ElementStatesDto value)
        {
            if (disposed || !context.IsGuest) return false;
            ProtocolCodec.ValidateMessage(value, true);
            if (value.roomSessionId != context.RoomSessionId || !receipts.TryAccept(value) || value.stateSeq <= receivedElementSeq || value.authorityTick < receivedElementTick) return false;
            receivedElementSeq = value.stateSeq; receivedElementTick = value.authorityTick;
            var active = new HashSet<int>();
            foreach (var state in value.effects) if (state.active) active.Add(state.targetEntityId);
            foreach (int id in new List<int>(displayedElements)) if (!active.Contains(id)) SetElementState(id, null);
            foreach (var state in value.effects) if (state.active) SetElementState(state.targetEntityId, state);
            ApplyChainViews(value.chains);
            return true;
        }
        private void SetElementState(int entityId, ElementStateDto state)
        {
            bool active = state != null && state.active;
            if (active) displayedElements.Add(entityId); else displayedElements.Remove(entityId);
            if (!context.TryGetEntity(entityId, out var view)) return;
            var component = view.View.GetComponent<NetworkElementState>();
            if (component == null && active) component = view.View.gameObject.AddComponent<NetworkElementState>();
            component?.ApplyConfirmed(state);
        }
        private void ClearElements()
        {
            foreach (int id in new List<int>(displayedElements)) SetElementState(id, null);
            effects.Clear(); displayedElements.Clear();
            ClearChains();
        }
    }
}
