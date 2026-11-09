using System;
using Rpg.Networking.Protocol;
using UnityEngine;

namespace Rpg.Networking
{
    /// <summary>Room-scoped physical-damage authority and result receiver. Does not execute legacy effects/death/drops.</summary>
    public sealed class RoomCombatDamage : IDisposable
    {
        private readonly RoomPresentationContext context;
        private readonly RoomSceneMonsters monsters;
        private readonly AuthorityResultSequence sequence;
        private readonly AuthorityResultReceipts receipts;
        private readonly Action<DamageResultDto> publish;
        private bool disposed;
        public AuthorityResultReceipts Receipts => receipts; // Future result handlers share exact receipts, never a maximum-ID watermark.

        public RoomCombatDamage(RoomPresentationContext context, RoomSceneMonsters monsters,
            AuthorityResultSequence sequence = null, Action<DamageResultDto> publish = null)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.monsters = monsters;
            if (!context.IsGuest && (sequence == null || sequence.RoomSessionId != context.RoomSessionId))
                throw new ArgumentException("A must use its room's shared result sequence.");
            this.sequence = sequence; this.publish = publish;
            receipts = new AuthorityResultReceipts(context.RoomSessionId);
        }

        public void OnPrimaryHit(PrimaryAttackConfirmedHit hit)
        {
            if (disposed || context.IsGuest || hit == null || hit.Attack == null || hit.Target == null || hit.Attack.RoomSessionId != context.RoomSessionId ||
                !context.TryGetEntity(hit.Attack.EntityId, out var sourceView) || sourceView.PlayerId != hit.Attack.PlayerId ||
                !context.TryGetEntity(hit.TargetEntityId, out var targetView) || targetView.PlayerId != 0 || targetView.View != hit.Target.transform ||
                monsters == null || !monsters.IsValidPrimaryAttackTarget(hit.Target)) return;
            var source = sourceView.View.GetComponent<Player>();
            var target = targetView.View.GetComponent<EnemyStats>();
            if (source == null || !source.IsAuthoritative || !source.isActiveAndEnabled || !source.IsRuntimeInitialized ||
                source.stats == null || !source.stats.IsRuntimeInitialized || source.stats.currentHealth <= 0 || source.stats.isDead ||
                target == null || !target.IsAuthoritative || !ReferenceEquals(source.Combat.PrimaryAttack.Current, hit.Attack) ||
                hit.Attack.SegmentAt(hit.AuthorityTick)?.SegmentId != hit.SegmentId ||
                !hit.Attack.TryConsumeDamageHit(hit.SegmentId, hit.TargetEntityId)) return;
            var stats = source.stats;
            var outcome = PhysicalDamageRules.Calculate(new PhysicalDamageInput
            {
                damage = stats.damage.GetValue(), strength = stats.strength.GetValue(), critChance = stats.critChance.GetValue(),
                critPower = stats.critPower.GetValue(), agility = stats.agility.GetValue(), shocked = stats.isShocked,
                armor = target.armor.GetValue(), evasion = target.evasion.GetValue(), targetAgility = target.agility.GetValue(),
                chilled = target.isChilled, vulnerable = target.isVulnerable, invincible = target.isInvincible, health = target.currentHealth
            }, () => UnityEngine.Random.Range(0, 100));
            long id = sequence.Allocate();
            long version = target.ApplyAuthorityNetworkHealth(outcome.FinalHealth);
            var result = new DamageResultDto
            {
                roomSessionId = context.RoomSessionId, authorityTick = hit.AuthorityTick, resultId = id,
                sourcePlayerId = hit.Attack.PlayerId, sourceEntityId = hit.Attack.EntityId, targetEntityId = hit.TargetEntityId,
                attackInstanceId = hit.Attack.AttackInstanceId, hitSegmentId = hit.SegmentId, targetStateVersion = version,
                actualDamage = outcome.ActualDamage, finalHealth = outcome.FinalHealth, maxHealth = target.GetMaxHealthValue(),
                critical = outcome.Critical, evaded = outcome.Evaded
            };
            ProtocolCodec.ValidateMessage(result, true);
            targetView.ApplyVersionedHealth(result.roomSessionId, result.authorityTick, version, result.finalHealth, result.maxHealth);
            if (result.finalHealth == 0) hit.Target.HoldNetworkAtZeroHealth();
            Debug.Log($"[DamageResult] A resultId={id} source={result.sourceEntityId} target={result.targetEntityId} damage={result.actualDamage} hp={result.finalHealth}/{result.maxHealth} version={version} tick={result.authorityTick} crit={result.critical} evade={result.evaded}");
            publish?.Invoke(result);
        }

        public bool ApplyConfirmedResult(DamageResultDto result)
        {
            if (disposed || !context.IsGuest) return false;
            ProtocolCodec.ValidateMessage(result, true);
            if (result.roomSessionId != context.RoomSessionId || !context.TryGetEntity(result.sourceEntityId, out var source) ||
                source.PlayerId != result.sourcePlayerId || !context.TryGetEntity(result.targetEntityId, out var target) ||
                target.View == null || target.View.GetComponent<CharaterStats>() == null || target.View.GetComponent<NetworkEntityIdentity>()?.Role != NetworkEntityRole.Display ||
                source.PlayerId > 0 && target.PlayerId > 0 || !receipts.TryAccept(result)) return false;
            bool applied = target.ApplyVersionedHealth(result.roomSessionId, result.authorityTick, result.targetStateVersion, result.finalHealth, result.maxHealth);
            Debug.Log($"[DamageResult] B resultId={result.resultId} source={result.sourceEntityId} target={result.targetEntityId} damage={result.actualDamage} finalHealth={result.finalHealth} version={result.targetStateVersion} tick={result.authorityTick} healthApplied={applied}; no local rolls.");
            return true; // A new result may be valid even if its health snapshot has already been superseded.
        }

        public void Dispose() { disposed = true; receipts.Clear(); }
    }
}
