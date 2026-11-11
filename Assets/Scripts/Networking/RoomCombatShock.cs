using System;
using System.Collections.Generic;
using Rpg.Networking.Protocol;
using UnityEngine;

namespace Rpg.Networking
{
    public sealed partial class RoomCombatDamage
    {
        private sealed class ChainRecord
        {
            public ChainStrikeDto State;
            public ElementRecord Parent;
            public int Ordinal;
            public float Speed;
            public ulong damageTick, endTick;
            public bool settled;
        }
        private readonly Dictionary<long, ChainRecord> chains = new Dictionary<long, ChainRecord>();
        private readonly Dictionary<long, ShockStrikeController> chainViews = new Dictionary<long, ShockStrikeController>();
        private long nextChainId;
        private ulong lastChainSnapshot;

        private void StartShockChain(Enemy origin, ElementRecord parent, ulong tick)
        {
            if (!parent.State.active || parent.State.kind != ElementEffectKind.Shock || parent.State.sourcePlayerId <= 0 ||
                !context.TryGetEntity(parent.State.sourceEntityId, out var source)) return;
            var target = monsters.FindShockTarget(origin);
            var prefab = source.View.GetComponent<CharaterStats>().ShockStrikePrefab;
            var controller = prefab != null ? prefab.GetComponent<ShockStrikeController>() : null;
            if (target == null || controller == null || !(controller.TravelSpeed > 0) || float.IsInfinity(controller.TravelSpeed)) return;
            Vector2 position = origin.transform.position;
            var state = new ChainStrikeDto { chainId = checked(++nextChainId), effectId = parent.State.effectId,
                sourcePlayerId = parent.State.sourcePlayerId, sourceEntityId = parent.State.sourceEntityId,
                originEntityId = parent.State.targetEntityId, targetEntityId = EntityRuntimePermissions.Identity(target).EntityId,
                startTick = tick, position = new[] { position.x, position.y }, direction = new[] { 1f, 0f } };
            chains.Add(state.chainId, new ChainRecord { State = state, Parent = parent, Ordinal = ++parent.Ordinal, Speed = controller.TravelSpeed });
            PublishElementSnapshot(tick); ApplyChainViews(CaptureChains());
            Debug.Log($"[ShockChain] START chain={state.chainId} player={state.sourcePlayerId} source={state.sourceEntityId} origin={state.originEntityId} target={state.targetEntityId} tick={tick}");
        }

        private void AdvanceShockChains(ulong tick)
        {
            bool changed = false;
            foreach (var pair in new List<KeyValuePair<long, ChainRecord>>(chains))
            {
                var record = pair.Value; var s = record.State;
                if (!context.TryGetEntity(s.sourceEntityId, out var source) ||
                    s.sourcePlayerId > 0 && source.View.GetComponent<Player>().stats.currentHealth <= 0 ||
                    !context.TryGetEntity(s.targetEntityId, out var target) ||
                    target.View.GetComponent<Enemy>() is not Enemy enemy ||
                    (!record.settled && !monsters.IsValidPrimaryAttackTarget(enemy)))
                { chains.Remove(pair.Key); changed = true; continue; }
                if (s.phase == ChainStrikePhase.Flying)
                {
                    Vector2 goal = target.View.position;
                    Vector2 position = Vector2.MoveTowards(new Vector2(s.position[0], s.position[1]), goal, record.Speed * GameSimulationClock.FixedDeltaTime);
                    Vector2 direction = position - goal;
                    s.position = new[] { position.x, position.y }; s.direction = new[] { direction.x, direction.y };
                    if ((position - goal).sqrMagnitude < .01f)
                    {
                        s.phase = ChainStrikePhase.Impact;
                        record.damageTick = tick + (ulong)Mathf.Max(1, Mathf.CeilToInt(.2f / GameSimulationClock.FixedDeltaTime - .00001f));
                        record.endTick = record.damageTick + (ulong)Mathf.Max(1, Mathf.CeilToInt(.4f / GameSimulationClock.FixedDeltaTime - .00001f));
                        changed = true;
                    }
                }
                if (s.phase == ChainStrikePhase.Impact && !record.settled && tick >= record.damageTick)
                {
                    record.settled = true; changed = true;
                    var stats = target.View.GetComponent<CharaterStats>();
                    // The old strike applies shock and then TakeDamage, without crit/evasion/resistance or recursive chain search.
                    if (!stats.isIgnited && !stats.isChilled && !stats.isShocked)
                        StartElement(enemy, ElementEffectKind.Shock, s.sourcePlayerId, s.sourceEntityId, record.Parent.AttackId,
                            record.Parent.Segment, tick, record.Parent.State.tickDamage);
                    int damage = stats.isInvincible ? 0 : Math.Min(stats.currentHealth,
                        DamageCalculations.ApplyVulnerability(record.Parent.State.tickDamage, stats.isVulnerable));
                    int health = stats.currentHealth - damage;
                    context.TryGetEntity(s.originEntityId, out var originView);
                    context.TryGetEntity(s.sourceEntityId, out var ownerView);
                    var impactSource = originView?.View != null ? originView.View.GetComponent<Entity>() : ownerView?.View.GetComponent<Entity>();
                    var result = new DamageResultDto { roomSessionId = context.RoomSessionId, authorityTick = tick, resultId = sequence.Allocate(),
                        kind = DamageResultKind.ShockChain, effectInstanceId = s.effectId, effectTick = record.Ordinal, chainInstanceId = s.chainId,
                        sourcePlayerId = s.sourcePlayerId, sourceEntityId = s.sourceEntityId, targetEntityId = s.targetEntityId,
                        attackInstanceId = record.Parent.AttackId, hitSegmentId = record.Parent.Segment,
                        actualDamage = damage, finalHealth = health, maxHealth = stats.GetMaxHealthValue(),
                        targetStateVersion = stats.ApplyAuthorityNetworkHealth(health), components = new DirectDamageDto { shockDamage = damage },
                        impact = stats.isInvincible ? null : NetworkHitFeedback.CreateAuthorityImpact(enemy, impactSource) };
                    ConfirmAuthorityResult(result, target);
                    publish?.Invoke(result);
                    Debug.Log($"[ShockChain] HIT chain={s.chainId} resultId={result.resultId} player={s.sourcePlayerId} target={s.targetEntityId} damage={damage} hp={health} tick={tick}");
                    if (health == 0)
                    {
                        if (enemy.GetComponent<NetworkHitFeedback>()?.IsAuthorityKnockbackActive != true) enemy.HoldNetworkAtZeroHealth();
                        EndElement(s.targetEntityId, tick);
                    }
                }
                if (record.settled && tick >= record.endTick) { chains.Remove(pair.Key); changed = true; }
            }
            if (changed || chains.Count > 0 && tick - lastChainSnapshot >= (ulong)Mathf.Max(1, Mathf.CeilToInt(.1f / GameSimulationClock.FixedDeltaTime - .00001f)))
            { lastChainSnapshot = tick; PublishElementSnapshot(tick); ApplyChainViews(CaptureChains()); }
        }

        private ChainStrikeDto[] CaptureChains()
        {
            var result = new List<ChainStrikeDto>();
            foreach (var record in chains.Values)
            {
                var s = record.State;
                result.Add(new ChainStrikeDto { chainId = s.chainId, effectId = s.effectId, sourcePlayerId = s.sourcePlayerId, sourceEntityId = s.sourceEntityId,
                    originEntityId = s.originEntityId, targetEntityId = s.targetEntityId, startTick = s.startTick, phase = s.phase,
                    position = (float[])s.position.Clone(), direction = (float[])s.direction.Clone() });
            }
            return result.ToArray();
        }
        private void ApplyChainViews(ChainStrikeDto[] states)
        {
            if (!Application.isPlaying) return;
            var active = new HashSet<long>();
            foreach (var state in states)
            {
                active.Add(state.chainId);
                if (!context.TryGetEntity(state.sourceEntityId, out var source) || !context.TryGetEntity(state.targetEntityId, out _)) continue;
                if (!chainViews.TryGetValue(state.chainId, out var view) || view == null)
                {
                    var prefab = source.View.GetComponent<CharaterStats>().ShockStrikePrefab;
                    if (prefab == null) continue;
                    view = TemporaryEffectPool.ForScene(source.View.gameObject.scene).Take(prefab,
                        new Vector3(state.position[0], state.position[1]), Quaternion.identity).GetComponent<ShockStrikeController>();
                    chainViews[state.chainId] = view;
                }
                view.PlayConfirmedChain(state);
            }
            foreach (var pair in new List<KeyValuePair<long, ShockStrikeController>>(chainViews))
                if (!active.Contains(pair.Key)) { if (pair.Value != null) pair.Value.ReturnNetworkView(); chainViews.Remove(pair.Key); }
        }
        private void RemovePlayerChains(int player, ulong tick)
        {
            bool changed = false;
            foreach (var pair in new List<KeyValuePair<long, ChainRecord>>(chains))
                if (pair.Value.State.sourcePlayerId == player) { chains.Remove(pair.Key); changed = true; }
            if (changed) { PublishElementSnapshot(tick); ApplyChainViews(CaptureChains()); }
        }
        private void ClearChains()
        {
            foreach (var view in chainViews.Values) if (view != null) view.ReturnNetworkView();
            chainViews.Clear(); chains.Clear();
        }
    }
}
