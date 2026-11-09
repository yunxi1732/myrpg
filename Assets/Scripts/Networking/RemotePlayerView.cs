using UnityEngine;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    /// <summary>Minimal 2D display view. A/standalone movement never runs through this component.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(9000)]
    [RequireComponent(typeof(NetworkEntityIdentity))]
    public sealed class RemotePlayerView : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float teleportDistance = 5f;
        [SerializeField, Range(0, 0.5f)] private float interpolationDelaySeconds = 0.1f;
        private NetworkEntityIdentity identity;
        private Animator animator;
        private float monsterAttackClipLength = 1.2f, savedAnimationSpeed;
        private bool holdingAttackAnimation;
        private readonly AuthorityPositionBuffer positions = new AuthorityPositionBuffer();
        public float TeleportDistance { get => teleportDistance; set => teleportDistance = Mathf.Max(0.01f, value); }
        public int BufferedStateCount => positions.Count;

        internal void Bind(NetworkEntityIdentity owner)
        {
            if (!ReferenceEquals(identity, owner)) positions.Clear();
            identity = owner;
            animator = GetComponentInChildren<Animator>(true);
            if (GetComponent<EnemySkeleton>() != null) monsterAttackClipLength = EnemySkeleton.AttackClipDuration(animator, out _);
        }

        public bool ApplyState(ulong tick, long sequence, Vector2 position, bool reset = false, int facingDir = 1,
            MovementAnimationState movementState = MovementAnimationState.Idle, float yVelocity = 0, MonsterAttackDto monsterAttack = null)
        {
            if (identity == null || identity.Role != NetworkEntityRole.Display) return false;
            float tickSeconds = GameSimulationClock.FixedDeltaTime; // Shared project's fixed simulation step.
            if (!positions.Add(tick, sequence, position, interpolationDelaySeconds / tickSeconds, teleportDistance, reset, facingDir, movementState, yVelocity, monsterAttack)) return false;
            if (positions.Snapped) Place(positions.Position);
            return true;
        }

        private void LateUpdate() => AdvanceDisplay(GameSimulationClock.DeltaTime);

        public void AdvanceDisplay(float unscaledDeltaTime)
        {
            if (identity == null || identity.Role != NetworkEntityRole.Display)
            {
                positions.Clear();
                return;
            }
            if (positions.Advance(unscaledDeltaTime, GameSimulationClock.FixedDeltaTime)) Place(positions.Position);
        }

        private void Place(Vector2 position)
        {
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            var entity = GetComponent<Entity>();
            if (entity != null) entity.FlipController(positions.FacingDirection);
            ApplyMovementAnimation();
        }

        private void ApplyMovementAnimation()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            // Parameter playback only. Never enter PlayerState or restart the clip on each received snapshot.
            animator.fireEvents = false;
            MovementAnimationState state = positions.MovementState;
            if (GetComponent<Enemy>() != null)
            {
                var stats = GetComponent<EnemyStats>();
                if (stats != null && stats.NetworkHealthVersion > 0 && stats.currentHealth <= 0)
                {
                    ReleaseAttackAnimation();
                    animator.SetBool("Idle", true); animator.SetBool("Move", false); animator.SetBool("Attack", false);
                    return;
                }
                var attack = positions.MonsterAttack;
                double elapsedTicks = attack == null ? 0 : System.Math.Max(0, positions.RenderTick - attack.startTick);
                bool attacking = attack != null && elapsedTicks < attack.durationTicks;
                animator.SetBool("Idle", !attacking && state == MovementAnimationState.Idle);
                animator.SetBool("Move", !attacking && state == MovementAnimationState.Move);
                animator.SetBool("Attack", attacking); animator.SetBool("Stunned", false);
                if (attacking)
                {
                    if (!holdingAttackAnimation) { savedAnimationSpeed = animator.speed; holdingAttackAnimation = true; }
                    animator.speed = 0;
                    float phase = Mathf.Clamp((float)(elapsedTicks * GameSimulationClock.FixedDeltaTime / monsterAttackClipLength), 0, .99999f);
                    // Evaluate this action at A's display tick. Repeated packets never restart it at frame zero.
                    animator.Play("Base Layer.SkeletonAttack", 0, phase);
                    animator.Update(0);
                }
                else ReleaseAttackAnimation();
                return; // Monster controllers have Idle/Move, not the player's Jump/Dash parameters.
            }
            animator.SetBool("Idle", state == MovementAnimationState.Idle);
            animator.SetBool("Move", state == MovementAnimationState.Move);
            animator.SetBool("Jump", state == MovementAnimationState.Jump || state == MovementAnimationState.Fall || state == MovementAnimationState.WallJump);
            animator.SetBool("WallSlide", state == MovementAnimationState.WallSlide);
            animator.SetBool("Dash", state == MovementAnimationState.Dash);
            animator.SetFloat("yVelocity", positions.YVelocity);
        }
        private void ReleaseAttackAnimation()
        {
            if (holdingAttackAnimation && animator != null) animator.speed = savedAnimationSpeed;
            holdingAttackAnimation = false;
        }
        private void OnDisable() { positions.Clear(); ReleaseAttackAnimation(); }
    }
}
