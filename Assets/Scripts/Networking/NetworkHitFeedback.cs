using Rpg.Networking.Protocol;
using UnityEngine;

namespace Rpg.Networking
{
    /// <summary>Confirmed presentation only. Physics is written once on A; B follows existing position snapshots.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(9500)]
    public sealed class NetworkHitFeedback : MonoBehaviour
    {
        private NetworkEntityIdentity identity;
        private Animator animator;
        private Rigidbody2D body;
        private ulong startTick, knockbackEndTick;
        private long reactionVersion;
        private int reactionTicks;
        private bool knockbackActive, holdingAnimation;
        private float savedSpeed;
        public int PresentationCount { get; private set; }
        public long LastResultId { get; private set; }
        public ulong ReactionStartTick => startTick;
        public long ReactionVersion => reactionVersion;
        public bool IsAuthorityKnockbackActive => knockbackActive && identity != null && identity.IsAuthoritative;

        public static DamageImpactDto CreateAuthorityImpact(Entity target, Entity source)
        {
            if (target == null || source == null || !target.IsAuthoritative || !source.IsAuthoritative) return null;
            float dx = target.transform.position.x - source.transform.position.x;
            int direction = dx == 0 ? source.facingDir : dx > 0 ? 1 : -1;
            Vector2 power = target.HitKnockbackPower;
            Vector2 position = target.rb != null ? target.rb.position : (Vector2)target.transform.position;
            return new DamageImpactDto { position = new[] { position.x, position.y }, facingDir = direction,
                knockbackVelocity = new[] { direction * Mathf.Abs(power.x), power.y },
                knockbackTicks = Mathf.Clamp(Mathf.CeilToInt(target.HitKnockbackDuration / GameSimulationClock.FixedDeltaTime), 1, 3000),
                reactionTicks = Mathf.Clamp(Mathf.CeilToInt(.2f / GameSimulationClock.FixedDeltaTime), 1, 3000) };
        }

        // Called only after the room's exact result-ID receipt gate, including on A.
        internal void PresentConfirmed(DamageResultDto result)
        {
            identity = GetComponent<NetworkEntityIdentity>();
            if (identity == null || !identity.IsBound || identity.RoomSessionId != result.roomSessionId || identity.EntityId != result.targetEntityId) return;
            PresentationCount++; LastResultId = result.resultId;
            GetComponentInChildren<EntityFX>(true)?.PlayConfirmedDamage(result);
            var impact = result.impact;
            // A late, previously unseen result can show its number, but cannot rewind the current reaction or physics.
            if (impact == null || result.authorityTick < startTick || result.targetStateVersion <= reactionVersion) return;
            startTick = result.authorityTick; reactionVersion = result.targetStateVersion; reactionTicks = impact.reactionTicks;
            animator = GetComponentInChildren<Animator>(true);
            if (!identity.IsAuthoritative) return;
            body = GetComponent<Rigidbody2D>();
            knockbackEndTick = startTick + (ulong)impact.knockbackTicks;
            knockbackActive = body != null;
            if (body != null) body.velocity = new Vector2(impact.knockbackVelocity[0], impact.knockbackVelocity[1]);
        }

        public bool AdvanceAuthorityKnockback(ulong tick)
        {
            if (!IsAuthorityKnockbackActive) return false;
            if (tick < knockbackEndTick) return true;
            knockbackActive = false;
            if (body != null) body.velocity = new Vector2(0, body.velocity.y);
            return false;
        }

        public bool IsReactingAt(double tick) => reactionTicks > 0 && tick >= startTick && tick - startTick < reactionTicks;

        // B calls this after releasing attack animation speed ownership, at the same tick as its interpolated position.
        internal bool ApplyReactionAnimation(double tick)
        {
            if (!IsReactingAt(tick)) { ReleaseReactionAnimation(); return false; }
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null || GetComponent<EnemySkeleton>() == null) return false;
            if (!holdingAnimation) { savedSpeed = animator.speed; holdingAnimation = true; }
            animator.fireEvents = false;
            animator.SetBool("Stunned", true);
            animator.speed = 0;
            // Reuse the existing hurt pose, without entering the counter/stun gameplay state or invoking its velocity/FX.
            animator.Play("Base Layer.SkeletonStunned", 0, Mathf.Clamp01((float)((tick - startTick) / reactionTicks)));
            animator.Update(0);
            return true;
        }

        internal void ReleaseReactionAnimation()
        {
            if (!holdingAnimation) return;
            if (animator != null)
            {
                animator.speed = GetComponent<NetworkElementState>()?.PlaybackAnimationSpeed ?? savedSpeed; animator.SetBool("Stunned", false);
                if (gameObject.activeInHierarchy && animator.isActiveAndEnabled && identity != null && identity.IsAuthoritative &&
                    GetComponent<EnemySkeleton>() is EnemySkeleton skeleton)
                {
                    var attack = skeleton.NetworkAttack;
                    bool attacking = skeleton.NetworkAiState == MonsterAiState.Attack && attack != null &&
                        GameSimulationClock.AuthorityTick >= attack.startTick && GameSimulationClock.AuthorityTick - attack.startTick < (ulong)attack.durationTicks;
                    float phase = attacking ? Mathf.Clamp((GameSimulationClock.AuthorityTick - attack.startTick) * GameSimulationClock.FixedDeltaTime /
                        EnemySkeleton.AttackClipDuration(animator, out _), 0, .99999f) : 0;
                    animator.Play("Base Layer." + (attacking ? "SkeletonAttack" : animator.GetBool("Move") ? "SkeletonMove" : "SkeletonIdle"), 0, phase);
                    animator.Update(0);
                }
            }
            holdingAnimation = false;
        }

        private void LateUpdate()
        {
            if (identity == null || !identity.IsBound) { ResetFeedback(); return; }
            if (identity.IsAuthoritative) ApplyReactionAnimation(GameSimulationClock.AuthorityTick);
        }
        private void ResetFeedback()
        {
            ReleaseReactionAnimation(); knockbackActive = false; reactionTicks = 0;
            startTick = 0; reactionVersion = 0; LastResultId = 0; PresentationCount = 0;
        }
        private void OnDisable() => ResetFeedback();
    }
}
