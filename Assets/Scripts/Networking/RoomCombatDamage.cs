using System;
using Rpg.Networking.Protocol;
using UnityEngine;

namespace Rpg.Networking
{
    /// <summary>A settles physical and direct magic together; B applies the final value without executing legacy combat.</summary>
    public sealed partial class RoomCombatDamage : IDisposable
    {
        private readonly RoomPresentationContext context;
        private readonly RoomSceneMonsters monsters;
        private readonly RoomNetworkObjects objects;
        private readonly AuthorityResultSequence sequence;
        private readonly AuthorityResultReceipts receipts;
        private readonly Action<DamageResultDto> publish;
        private DamageResultDto lastAuthorityResult;
        private bool disposed;
        public AuthorityResultReceipts Receipts => receipts; // Future result handlers share exact receipts, never a maximum-ID watermark.

        public RoomCombatDamage(RoomPresentationContext context, RoomSceneMonsters monsters,
            AuthorityResultSequence sequence = null, Action<DamageResultDto> publish = null, Action<ElementStatesDto> publishElements = null,
            RoomNetworkObjects objects = null)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.monsters = monsters;
            this.objects = objects;
            if (!context.IsGuest && (sequence == null || sequence.RoomSessionId != context.RoomSessionId))
                throw new ArgumentException("A must use its room's shared result sequence.");
            this.sequence = sequence; this.publish = publish;
            this.publishElements = publishElements;
            receipts = new AuthorityResultReceipts(context.RoomSessionId);
        }

        public void OnPrimaryHit(PrimaryAttackConfirmedHit hit)
        {
            if (disposed || context.IsGuest || hit.Attack.RoomSessionId != context.RoomSessionId ||
                !context.TryGetEntity(hit.Attack.EntityId, out var sourceView) || sourceView.PlayerId != hit.Attack.PlayerId ||
                !context.TryGetEntity(hit.TargetEntityId, out var targetView) || targetView.PlayerId != 0 || targetView.View != hit.Target.transform ||
                !monsters.IsValidPrimaryAttackTarget(hit.Target)) return;
            var source = sourceView.View.GetComponent<Player>();
            var target = targetView.View.GetComponent<EnemyStats>();
            if (!source.IsAuthoritative || !source.isActiveAndEnabled || !source.IsRuntimeInitialized ||
                !source.stats.IsRuntimeInitialized || source.stats.currentHealth <= 0 || source.stats.isDead ||
                !target.IsAuthoritative || !ReferenceEquals(source.Combat.PrimaryAttack.Current, hit.Attack) ||
                hit.Attack.SegmentAt(hit.AuthorityTick)?.SegmentId != hit.SegmentId ||
                !hit.Attack.TryConsumeDamageHit(hit.SegmentId, hit.TargetEntityId)) return;
            ResolveDirectHit(source, hit.Target, targetView, hit.Attack, hit.SegmentId, hit.AuthorityTick);
        }

        internal bool OnSkillHit(PrimaryAttackInstance attack, Enemy enemy, bool parry = false, float multiplier = 0, bool magicOnly = false)
        {
            if (disposed || context.IsGuest || !monsters.IsValidPrimaryAttackTarget(enemy)) return false;
            if (!context.TryGetEntity(attack.EntityId, out var source) || source.PlayerId != attack.PlayerId ||
                !IsValidPlayerTarget(source.View.GetComponent<Player>())) return false;
            int targetId = enemy.GetComponent<NetworkEntityIdentity>().EntityId;
            ulong tick = GameSimulationClock.AuthorityTick;
            if (!attack.TryRegisterHit(1, targetId, tick) || !attack.TryConsumeDamageHit(1, targetId)) return false;
            context.TryGetEntity(targetId, out var target);
            ResolveDirectHit(source.View.GetComponent<Player>(), enemy, target, attack, 1, tick, parry, multiplier, magicOnly);
            return true;
        }

        public bool TryParry(Player player, EnemySkeleton enemy)
        {
            if (disposed || context.IsGuest || player.counterAttackState.Succeeded ||
                player.stateMachine.currentState != player.counterAttackState || !IsValidPlayerTarget(player) ||
                !monsters.IsValidPrimaryAttackTarget(enemy) || enemy.NetworkAiState != MonsterAiState.Attack ||
                !enemy.CounterWindowOpen || !enemy.CanBeStunned()) return false;
            player.counterAttackState.ConfirmNetworkSuccess();
            ulong tick = GameSimulationClock.AuthorityTick;
            var attack = new PrimaryAttackInstance(context.RoomSessionId, player.Combat.PlayerId, player.Combat.EntityId,
                objects.AllocatePrimaryAttackInstanceId(), 0, tick, checked(tick + 1),
                new PrimaryAttackTiming(0, 1, new PrimaryHitSegmentTiming(0, 1)));
            OnSkillHit(attack, enemy, true);
            attack.Close();
            return true;
        }

        public bool IsValidPlayerTarget(Player player)
        {
            var identity = EntityRuntimePermissions.Identity(player);
            return !disposed && !context.IsGuest && player != null && player.isActiveAndEnabled && player.IsRuntimeInitialized &&
                identity != null && identity.IsAuthoritative && !identity.MovementHeld && identity.RoomSessionId == context.RoomSessionId && identity.PlayerId > 0 &&
                context.TryGetEntity(identity.EntityId, out var view) && view.PlayerId == identity.PlayerId && view.View == player.transform &&
                player.stats.IsRuntimeInitialized && !player.stats.isDead && player.stats.currentHealth > 0;
        }

        public void OnMonsterHit(MonsterAttackConfirmedHit hit)
        {
            if (disposed || context.IsGuest ||
                hit.Attack.RoomSessionId != context.RoomSessionId || hit.Attack.PlayerId != 0 ||
                !hit.Source.NetworkMovementOnly || EntityRuntimePermissions.Identity(hit.Source)?.MovementHeld != false ||
                !monsters.IsValidPrimaryAttackTarget(hit.Source) || !IsValidPlayerTarget(hit.Target) ||
                !context.TryGetEntity(hit.Attack.EntityId, out var sourceView) || sourceView.PlayerId != 0 || sourceView.View != hit.Source.transform ||
                !context.TryGetEntity(hit.TargetEntityId, out var targetView) || targetView.View != hit.Target.transform || targetView.PlayerId <= 0 ||
                hit.Source.NetworkAiState != MonsterAiState.Attack || !ReferenceEquals(hit.Source.NetworkHits.Current, hit.Attack) ||
                hit.Attack.SegmentAt(hit.AuthorityTick)?.SegmentId != hit.SegmentId ||
                !hit.Attack.TryConsumeDamageHit(hit.SegmentId, hit.TargetEntityId)) return;
            if (TryParry(hit.Target, hit.Source)) return;
            ResolveDirectHit(hit.Source, hit.Target, targetView, hit.Attack, hit.SegmentId, hit.AuthorityTick);
        }

        private void ResolveDirectHit(Entity source, Entity targetEntity, RoomEntityPresentation targetView, PrimaryAttackInstance attack, int segment, ulong tick,
            bool parry = false, float multiplier = 0, bool magicOnly = false)
        {
            var stats = source.stats;
            var target = targetEntity.GetComponent<CharaterStats>();
            var outcome = magicOnly ? new PhysicalDamageOutcome(0, target.currentHealth, false, false) : PhysicalDamageRules.Calculate(new PhysicalDamageInput
            {
                damage = multiplier > 0 ? Mathf.RoundToInt((stats.damage.GetValue() + stats.strength.GetValue()) * multiplier) : stats.damage.GetValue(),
                strength = multiplier > 0 ? 0 : stats.strength.GetValue(), critChance = stats.critChance.GetValue(),
                critPower = stats.critPower.GetValue(), agility = stats.agility.GetValue(), shocked = stats.isShocked,
                armor = target.armor.GetValue(), evasion = target.evasion.GetValue(), targetAgility = target.agility.GetValue(),
                chilled = target.isChilled, vulnerable = target.isVulnerable, invincible = target.isInvincible, health = target.currentHealth
            }, () => UnityEngine.Random.Range(0, 100));
            var components = new DirectDamageDto { physicalDamage = outcome.ActualDamage,
                rawFire = Math.Max(0, stats.fireDamage.GetValue()), rawIce = Math.Max(0, stats.iceDamage.GetValue()),
                rawLightning = Math.Max(0, stats.lightningDamage.GetValue()), rawIntelligence = Math.Max(0, stats.intelligence.GetValue()) };
            if (!outcome.Evaded && !target.isInvincible && outcome.FinalHealth > 0)
            {
                int magic = DamageCalculations.SumDirectMagic(components.rawFire, components.rawIce, components.rawLightning, components.rawIntelligence);
                magic = DamageCalculations.ApplyResistance(magic, target.magicResistance.GetValue(), target.intelligence.GetValue());
                magic = DamageCalculations.ApplyVulnerability(magic, target.isVulnerable);
                components.magicDamage = Math.Min(outcome.FinalHealth, magic);
            }
            int finalHealth = outcome.FinalHealth - components.magicDamage;
            long id = sequence.Allocate();
            long version = target.ApplyAuthorityNetworkHealth(finalHealth);
            var result = new DamageResultDto
            {
                roomSessionId = context.RoomSessionId, authorityTick = tick, resultId = id,
                sourcePlayerId = attack.PlayerId, sourceEntityId = attack.EntityId, targetEntityId = targetView.EntityId,
                attackInstanceId = attack.AttackInstanceId, hitSegmentId = segment, targetStateVersion = version,
                actualDamage = components.physicalDamage + components.magicDamage, components = components,
                finalHealth = finalHealth, maxHealth = target.GetMaxHealthValue(),
                critical = outcome.Critical, evaded = outcome.Evaded,
                impact = parry || outcome.Evaded || target.isInvincible ? null : NetworkHitFeedback.CreateAuthorityImpact(targetEntity, source)
            };
            ConfirmAuthorityResult(result, targetView);
            if (result.finalHealth == 0)
            {
                if (targetEntity is Player player) player.HoldNetworkAtZeroHealth();
                else if (targetEntity is Enemy enemy && targetEntity.GetComponent<NetworkHitFeedback>()?.IsAuthorityKnockbackActive != true)
                    enemy.HoldNetworkAtZeroHealth();
            }
            Debug.Log($"[DamageResult] A resultId={id} source={result.sourceEntityId} target={result.targetEntityId} damage={result.actualDamage} physical={components.physicalDamage} magic={components.magicDamage} fire/ice/lightning/int={components.rawFire}/{components.rawIce}/{components.rawLightning}/{components.rawIntelligence} hp={result.finalHealth}/{result.maxHealth} version={version} tick={result.authorityTick} crit={result.critical} evade={result.evaded}");
            publish?.Invoke(result);
            if (!result.evaded && !target.isInvincible && finalHealth > 0) TryApplyElement(source, targetEntity, attack, segment, tick, components);
            if (finalHealth == 0) EndElement(targetView.EntityId, tick);
        }

        public bool ApplyConfirmedResult(DamageResultDto result)
        {
            if (disposed || !context.IsGuest) return false;
            ProtocolCodec.ValidateMessage(result, true);
            if (result.roomSessionId != context.RoomSessionId || !context.TryGetEntity(result.sourceEntityId, out var source) ||
                source.PlayerId != result.sourcePlayerId || !context.TryGetEntity(result.targetEntityId, out var target) ||
                target.View.GetComponent<NetworkEntityIdentity>().Role != NetworkEntityRole.Display ||
                (source.PlayerId > 0) == (target.PlayerId > 0) || !receipts.TryAccept(result)) return false;
            bool applied = target.ApplyVersionedHealth(result.roomSessionId, result.authorityTick, result.targetStateVersion, result.finalHealth, result.maxHealth);
            Present(result, target.View);
            Debug.Log($"[DamageResult] B resultId={result.resultId} source={result.sourceEntityId} target={result.targetEntityId} kind={result.kind} effect={result.effectInstanceId}/{result.effectTick} chain={result.chainInstanceId} damage={result.actualDamage} physical={result.components.physicalDamage} magic={result.components.magicDamage} burn={result.components.burnDamage} shock={result.components.shockDamage} finalHealth={result.finalHealth} version={result.targetStateVersion} tick={result.authorityTick} healthApplied={applied}; no local rolls.");
            return true; // A new result may be valid even if its health snapshot has already been superseded.
        }

        private void PresentOnce(DamageResultDto result, Transform target)
        {
            if (receipts.TryAccept(result)) Present(result, target);
        }
        private void ConfirmAuthorityResult(DamageResultDto result, RoomEntityPresentation target)
        {
            ProtocolCodec.ValidateMessage(result, true);
            lastAuthorityResult = result;
            target.ApplyVersionedHealth(result.roomSessionId, result.authorityTick, result.targetStateVersion, result.finalHealth, result.maxHealth);
            PresentOnce(result, target.View);
        }
        private static void Present(DamageResultDto result, Transform target)
        {
            var feedback = target.GetComponent<NetworkHitFeedback>() ?? target.gameObject.AddComponent<NetworkHitFeedback>();
            feedback.PresentConfirmed(result);
        }

        public bool RepeatLastResultForVerification()
        {
            if (disposed || context.IsGuest || lastAuthorityResult == null) return false;
            if (context.TryGetEntity(lastAuthorityResult.targetEntityId, out var target)) PresentOnce(lastAuthorityResult, target.View);
            publish?.Invoke(lastAuthorityResult);
            Debug.Log($"[HitFeedback] REPEAT resultId={lastAuthorityResult.resultId}; existing receipts must suppress presentation and knockback on both peers.");
            return true;
        }

        public void Dispose() { ClearElements(); disposed = true; receipts.Clear(); lastAuthorityResult = null; }
    }
}
