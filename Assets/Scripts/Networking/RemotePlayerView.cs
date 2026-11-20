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
        private bool holdingPlayerAttack;
        private float savedPlayerSpeed;
        private readonly float[] playerClipLengths = new float[3];
        private long localAttackRequestSeq;
        private float localAttackStartTime;
        private float localAttackPreviewPhase;
        private bool localAttackAnimating;
        private PrimaryAttackExecutionDto localAttackExecution;
        private float localAttackConfirmationTime;
        private long finishedLocalAttackInstanceId;
        public long PresentedPlayerAttackId { get; private set; }
        public int PlayerAttackStartCount { get; private set; }
        public float PlayerAttackPhase { get; private set; }
        internal bool SwordActionActive;
        private readonly AuthorityPositionBuffer positions = new AuthorityPositionBuffer();
        private bool hasPredictedPosition;
        private Vector2 predictedPosition;
        private bool predictedDash, suppressAuthorityDash;
        private int predictedDashFacing;
        private ulong suppressDashEndTick;
        private bool predictedJump, suppressAuthorityJump;
        private int predictedJumpFacing;
        private float predictedJumpVelocity;
        private ulong jumpStartTick;
        private long localSwordActionSeq;
        private bool localSwordAiming, localSwordThrowing;
        private float localSwordThrowEndTime;
        private float swordThrowClipLength;
        private int localSwordFacing;
        internal bool LocalSwordPreviewActive => localSwordAiming || localSwordThrowing;
        public float TeleportDistance { get => teleportDistance; set => teleportDistance = Mathf.Max(0.01f, value); }
        public int BufferedStateCount => positions.Count;
        internal Vector2 AuthorityDisplayPosition => positions.Position;
        internal MovementAnimationState AuthorityMovementState => positions.MovementState;
        public int MonsterTargetEntityId => positions.MonsterTargetEntityId;
        public bool ParryActive { get; internal set; }
        public bool ParrySucceeded { get; internal set; }
        public bool MonsterStunned { get; internal set; }
        private MonsterAiState monsterAiState;

        internal void SetMonsterAiState(MonsterAiState state)
        {
            if (monsterAiState == state) return;
            bool wasTeleporting = monsterAiState == MonsterAiState.Teleport;
            monsterAiState = state;
            if (GetComponent<Enemy_Boss>() != null && (wasTeleporting || state == MonsterAiState.Teleport))
                GetComponent<EntityFX>()?.MakeTransparent(state == MonsterAiState.Teleport);
        }

        internal void Bind(NetworkEntityIdentity owner)
        {
            if (!ReferenceEquals(identity, owner))
            {
                positions.Clear(); ReleasePlayerAttack();
                monsterAiState = MonsterAiState.Idle;
                localAttackRequestSeq = 0; localAttackAnimating = false; localAttackExecution = null; finishedLocalAttackInstanceId = 0;
                localSwordActionSeq = 0; localSwordAiming = localSwordThrowing = false;
                SwordActionActive = false;
                hasPredictedPosition = false;
                predictedDash = suppressAuthorityDash = false;
                predictedJump = suppressAuthorityJump = false;
                PresentedPlayerAttackId = 0; PlayerAttackStartCount = 0; PlayerAttackPhase = 0;
            }
            identity = owner;
            animator = GetComponentInChildren<Animator>(true);
            if (animator != null && animator.runtimeAnimatorController != null)
                foreach (var clip in animator.runtimeAnimatorController.animationClips)
                {
                    for (int i = 0; i < 3; i++) if (clip.name == "PlayerAttack" + (i + 1)) playerClipLengths[i] = clip.length;
                    if (clip.name == "PlayerThrowSword") swordThrowClipLength = clip.length;
                }
            var monster = GetComponent<Enemy>();
            if (monster != null) monsterAttackClipLength = Enemy.AttackClipDuration(animator, monster.NetworkAttackClip, out _);
        }

        public bool ApplyState(ulong tick, long sequence, Vector2 position, bool reset = false, int facingDir = 1,
            MovementAnimationState movementState = MovementAnimationState.Idle, float yVelocity = 0, MonsterAttackDto monsterAttack = null, PlayerAttackDto playerAttack = null,
            int monsterTargetEntityId = 0)
        {
            if (identity == null || identity.Role != NetworkEntityRole.Display) return false;
            if (!ProtocolCodec.ValidPlayerAttack(playerAttack, identity.EntityId, facingDir, tick)) return false;
            float tickSeconds = GameSimulationClock.FixedDeltaTime; // Shared project's fixed simulation step.
            if (!positions.Add(tick, sequence, position, interpolationDelaySeconds / tickSeconds, teleportDistance, reset, facingDir, movementState, yVelocity,
                monsterAttack, playerAttack, monsterTargetEntityId)) return false;
            if (positions.Snapped && !hasPredictedPosition) Place(positions.Position);
            return true;
        }

        public bool ApplyPredictedPosition(Vector2 position)
        {
            if (identity == null || identity.Role != NetworkEntityRole.Display || !identity.IsLocalInputOwner) return false;
            predictedPosition = position;
            hasPredictedPosition = true;
            Place(position);
            return true;
        }

        public void ClearPredictedPosition()
        {
            hasPredictedPosition = false;
            if (positions.Count > 0) Place(positions.Position);
        }

        public void BeginPredictedDash(int facing)
        {
            predictedDash = suppressAuthorityDash = true;
            predictedDashFacing = facing;
            suppressDashEndTick = 0;
            Place(predictedPosition);
        }

        public void EndPredictedDash()
        {
            predictedDash = false;
        }

        public void ConfirmPredictedDash(ulong startTick, int durationTicks, int facing)
        {
            suppressDashEndTick = startTick + (ulong)durationTicks;
            predictedDashFacing = facing;
        }

        public void RejectPredictedDash()
        {
            predictedDash = suppressAuthorityDash = false;
            suppressDashEndTick = 0;
        }

        public void BeginPredictedJump(int facing)
        {
            predictedJump = suppressAuthorityJump = true;
            predictedJumpFacing = facing;
            predictedJumpVelocity = GetComponent<Player>().jumpForce;
            jumpStartTick = 0;
            Place(predictedPosition);
        }

        public void SetPredictedJumpVelocity(float velocity) => predictedJumpVelocity = velocity;

        public void ConfirmPredictedJump(ulong startTick, int facing)
        {
            jumpStartTick = startTick;
            predictedJumpFacing = facing;
        }

        public void EndPredictedJump() => predictedJump = false;

        public void RejectPredictedJump()
        {
            predictedJump = suppressAuthorityJump = false;
            jumpStartTick = 0;
        }

        public void BeginLocalSwordAim(long actionSeq, float directionX)
        {
            if (identity.Role != NetworkEntityRole.Display || !identity.IsLocalInputOwner) return;
            localSwordActionSeq = actionSeq;
            localSwordAiming = true; localSwordThrowing = false;
            if (directionX != 0) localSwordFacing = directionX > 0 ? 1 : -1;
            if (localSwordFacing != 0) GetComponent<Entity>().FlipController(localSwordFacing);
            SwordActionActive = true;
            animator.fireEvents = false;
            animator.SetBool("AimSword", true);
            animator.Play("Base Layer.PlayerAimSword", 0, 0);
            animator.Update(0);
        }

        public void UpdateLocalSwordAim(long actionSeq, float directionX)
        {
            if (!localSwordAiming || actionSeq <= localSwordActionSeq) return;
            localSwordActionSeq = actionSeq;
            if (directionX != 0) localSwordFacing = directionX > 0 ? 1 : -1;
            if (localSwordFacing != 0) GetComponent<Entity>().FlipController(localSwordFacing);
        }

        public void BeginLocalSwordThrow(long actionSeq, float directionX)
        {
            if (!localSwordAiming || actionSeq <= localSwordActionSeq) return;
            localSwordActionSeq = actionSeq;
            localSwordAiming = false; localSwordThrowing = true;
            if (directionX != 0) localSwordFacing = directionX > 0 ? 1 : -1;
            if (localSwordFacing != 0) GetComponent<Entity>().FlipController(localSwordFacing);
            localSwordThrowEndTime = GameSimulationClock.Time + swordThrowClipLength;
            animator.fireEvents = false;
            animator.SetBool("AimSword", false);
            animator.Play("Base Layer.PlayerThrowSword", 0, 0);
            animator.Update(0);
        }

        public void CancelLocalSwordPreview(long actionSeq)
        {
            if (actionSeq < localSwordActionSeq) return;
            localSwordActionSeq = 0;
            localSwordAiming = localSwordThrowing = false;
            SwordActionActive = false;
            animator.SetBool("AimSword", false);
            animator.Play("Base Layer.PlayerIdle", 0, 0);
            animator.Update(0);
            ApplyMovementAnimation();
        }

        public void BeginLocalPrimaryAttack(long requestSeq)
        {
            if (identity.Role != NetworkEntityRole.Display || !identity.IsLocalInputOwner) return;
            localAttackRequestSeq = requestSeq;
            localAttackStartTime = GameSimulationClock.Time;
            localAttackPreviewPhase = 0;
            localAttackAnimating = true;
            localAttackExecution = null;
            ApplyMovementAnimation();
        }

        public void ConfirmLocalPrimaryAttack(PrimaryAttackExecutionDto execution)
        {
            if (execution.requestSeq != localAttackRequestSeq) return;
            if (!execution.accepted) { CancelLocalPrimaryAttack(execution.requestSeq); return; }
            localAttackExecution = execution;
            localAttackConfirmationTime = GameSimulationClock.Time;
            localAttackAnimating = false;
            if (PresentedPlayerAttackId != execution.attackInstanceId)
            { PresentedPlayerAttackId = execution.attackInstanceId; PlayerAttackStartCount++; PlayerAttackPhase = localAttackPreviewPhase; }
            GetComponent<Entity>().FlipController(execution.facingDir);
            ApplyMovementAnimation();
        }

        public void CancelLocalPrimaryAttack(long requestSeq)
        {
            if (requestSeq != localAttackRequestSeq) return;
            localAttackRequestSeq = 0;
            localAttackAnimating = false;
            localAttackExecution = null;
            ApplyMovementAnimation();
        }

        private void LateUpdate() => AdvanceDisplay(GameSimulationClock.DeltaTime);

        public void AdvanceDisplay(float unscaledDeltaTime)
        {
            if (identity == null || identity.Role != NetworkEntityRole.Display)
            {
                positions.Clear();
                localAttackRequestSeq = 0; localAttackAnimating = false; localAttackExecution = null;
                localSwordActionSeq = 0; localSwordAiming = localSwordThrowing = false;
                ReleasePlayerAttack(); ReleaseAttackAnimation();
                return;
            }
            if (positions.Advance(unscaledDeltaTime, GameSimulationClock.FixedDeltaTime))
            {
                if (suppressAuthorityDash && suppressDashEndTick > 0 && positions.LatestTick > suppressDashEndTick &&
                    positions.MovementState != MovementAnimationState.Dash) suppressAuthorityDash = false;
                if (suppressAuthorityJump && jumpStartTick > 0 && positions.RenderTick > jumpStartTick &&
                    positions.MovementState != MovementAnimationState.Jump && positions.MovementState != MovementAnimationState.WallJump &&
                    positions.MovementState != MovementAnimationState.Fall)
                    suppressAuthorityJump = false;
                if (!predictedDash) Place(hasPredictedPosition ? predictedPosition : positions.Position);
            }
        }

        private void Place(Vector2 position)
        {
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            var monster = GetComponent<EnemyStats>();
            if (monster != null && monster.NetworkHealthVersion > 0 && monster.currentHealth == 0) return;
            var entity = GetComponent<Entity>();
            if (entity != null) entity.FlipController(localAttackExecution != null ? localAttackExecution.facingDir :
                LocalSwordPreviewActive && localSwordFacing != 0 ? localSwordFacing :
                predictedDash ? predictedDashFacing : predictedJump ? predictedJumpFacing : positions.FacingDirection);
            ApplyMovementAnimation();
            if (predictedDash || !suppressAuthorityDash && positions.MovementState == MovementAnimationState.Dash)
                GetComponent<PlayerFX>().CreateAfterImage();
        }

        private void ApplyMovementAnimation()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            // Parameter playback only. Never enter PlayerState or restart the clip on each received snapshot.
            animator.fireEvents = false;
            var playerStats = GetComponent<PlayerStats>();
            if (playerStats != null && playerStats.NetworkHealthVersion > 0 && playerStats.currentHealth == 0)
            {
                localAttackRequestSeq = 0; localAttackAnimating = false; localAttackExecution = null;
                localSwordActionSeq = 0; localSwordAiming = localSwordThrowing = false;
                animator.SetBool("AimSword", false);
                ReleasePlayerAttack(); ReleaseAttackAnimation();
                animator.SetBool("Attack", false); animator.SetBool("Move", false); animator.SetBool("Jump", false);
                animator.SetBool("Dash", false); animator.SetBool("WallSlide", false); animator.SetBool("Idle", false);
                animator.SetBool("CounterAttack", false); animator.SetBool("SuccessfulCounterAttack", false);
                animator.SetBool("Die", true);
                return;
            }
            var feedback = GetComponent<NetworkHitFeedback>();
            if (feedback != null && feedback.IsReactingAt(positions.RenderTick))
            {
                localAttackRequestSeq = 0; localAttackAnimating = false; localAttackExecution = null;
                localSwordActionSeq = 0; localSwordAiming = localSwordThrowing = false;
                animator.SetBool("AimSword", false);
                ReleaseAttackAnimation(); ReleasePlayerAttack();
                if (feedback.ApplyReactionAnimation(positions.RenderTick)) return;
            }
            feedback?.ReleaseReactionAnimation();
            if (localSwordThrowing && GameSimulationClock.Time >= localSwordThrowEndTime)
            {
                localSwordActionSeq = 0;
                localSwordThrowing = false;
                SwordActionActive = false;
            }
            MovementAnimationState state = predictedDash ? MovementAnimationState.Dash :
                predictedJump ? MovementAnimationState.Jump :
                suppressAuthorityDash && positions.MovementState == MovementAnimationState.Dash ||
                suppressAuthorityJump && (positions.MovementState == MovementAnimationState.Jump || positions.MovementState == MovementAnimationState.WallJump ||
                    positions.MovementState == MovementAnimationState.Fall)
                    ? MovementAnimationState.Idle : positions.MovementState;
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
                bool attacking = monsterAiState == MonsterAiState.Attack;
                bool special = monsterAiState == MonsterAiState.Teleport || monsterAiState == MonsterAiState.Cast;
                animator.SetBool("Idle", !attacking && !special && !MonsterStunned && state == MovementAnimationState.Idle);
                animator.SetBool("Move", !attacking && !special && !MonsterStunned && state == MovementAnimationState.Move);
                animator.SetBool("Attack", attacking);
                var monster = GetComponent<Enemy>();
                if (!string.IsNullOrEmpty(monster.NetworkStunParameter)) animator.SetBool(monster.NetworkStunParameter, MonsterStunned);
                if (monster is Enemy_Boss)
                {
                    animator.SetBool("Teleport", monsterAiState == MonsterAiState.Teleport);
                    animator.SetBool("SpellCast", monsterAiState == MonsterAiState.Cast);
                }
                if (attacking && attack != null && elapsedTicks < attack.durationTicks)
                {
                    if (!holdingAttackAnimation) { savedAnimationSpeed = animator.speed; holdingAttackAnimation = true; }
                    animator.speed = 0;
                    float phase = Mathf.Clamp((float)(elapsedTicks * GameSimulationClock.FixedDeltaTime / monsterAttackClipLength), 0, .99999f);
                    // Evaluate this action at A's display tick. Repeated packets never restart it at frame zero.
                    animator.Play("Base Layer." + monster.NetworkAttackClip, 0, phase);
                    animator.Update(0);
                }
                else ReleaseAttackAnimation();
                return; // Monster controllers have Idle/Move, not the player's Jump/Dash parameters.
            }
            var playerAttack = positions.PlayerAttack;
            bool alive = GetComponent<PlayerStats>() == null || GetComponent<PlayerStats>().currentHealth > 0;
            bool playing = alive && playerAttack != null && positions.RenderTick >= playerAttack.startTick &&
                positions.RenderTick - playerAttack.startTick < playerAttack.durationTicks;
            if (localAttackExecution != null)
            {
                var execution = localAttackExecution;
                float elapsedTicks = execution.authorityTick - execution.startTick +
                    (GameSimulationClock.Time - localAttackConfirmationTime) / GameSimulationClock.FixedDeltaTime;
                if (elapsedTicks < execution.durationTicks)
                {
                    if (!holdingPlayerAttack) { savedPlayerSpeed = animator.speed; holdingPlayerAttack = true; }
                    float length = playerClipLengths[execution.comboIndex];
                    PlayerAttackPhase = Mathf.Max(PlayerAttackPhase,
                        Mathf.Clamp(elapsedTicks * GameSimulationClock.FixedDeltaTime / length, 0, .99999f));
                    animator.SetBool("Idle", false); animator.SetBool("Move", false); animator.SetBool("Jump", false);
                    animator.SetBool("WallSlide", false); animator.SetBool("Dash", false); animator.SetBool("Attack", true);
                    animator.SetInteger("comboCounter", execution.comboIndex);
                    animator.speed = 0;
                    animator.Play("Base Layer.PrimaryAttack.PlayerAttack" + (execution.comboIndex + 1), 0, PlayerAttackPhase);
                    animator.Update(0);
                    return;
                }
                finishedLocalAttackInstanceId = execution.attackInstanceId;
                localAttackExecution = null; localAttackRequestSeq = 0;
            }
            if (localAttackAnimating)
            {
                float length = playerClipLengths[0];
                float elapsed = GameSimulationClock.Time - localAttackStartTime;
                if (elapsed < length)
                {
                    if (!holdingPlayerAttack) { savedPlayerSpeed = animator.speed; holdingPlayerAttack = true; }
                    localAttackPreviewPhase = Mathf.Max(localAttackPreviewPhase, Mathf.Clamp01(elapsed / length));
                    animator.SetBool("Idle", false); animator.SetBool("Move", false); animator.SetBool("Jump", false);
                    animator.SetBool("WallSlide", false); animator.SetBool("Dash", false); animator.SetBool("Attack", true);
                    animator.SetInteger("comboCounter", 0);
                    animator.speed = 0;
                    animator.Play("Base Layer.PrimaryAttack.PlayerAttack1", 0, localAttackPreviewPhase);
                    animator.Update(0);
                    return;
                }
                localAttackAnimating = false;
            }
            if (localAttackRequestSeq == 0 && playing && playerAttack.attackInstanceId != finishedLocalAttackInstanceId)
            {
                if (!holdingPlayerAttack) { savedPlayerSpeed = animator.speed; holdingPlayerAttack = true; }
                if (PresentedPlayerAttackId != playerAttack.attackInstanceId)
                { PresentedPlayerAttackId = playerAttack.attackInstanceId; PlayerAttackStartCount++; PlayerAttackPhase = 0; }
                float length = playerClipLengths[playerAttack.comboIndex];
                if (!(length > 0)) return;
                PlayerAttackPhase = Mathf.Max(PlayerAttackPhase, Mathf.Clamp((float)((positions.RenderTick - playerAttack.startTick) * GameSimulationClock.FixedDeltaTime / length), 0, .99999f));
                animator.SetBool("Idle", false); animator.SetBool("Move", false); animator.SetBool("Jump", false);
                animator.SetBool("WallSlide", false); animator.SetBool("Dash", false); animator.SetBool("Attack", true);
                animator.SetInteger("comboCounter", playerAttack.comboIndex);
                animator.speed = 0;
                animator.Play("Base Layer.PrimaryAttack.PlayerAttack" + (playerAttack.comboIndex + 1), 0, PlayerAttackPhase);
                animator.Update(0);
                return;
            }
            bool exitedAttack = holdingPlayerAttack;
            ReleasePlayerAttack();
            animator.SetBool("Attack", false);
            animator.SetBool("Idle", !SwordActionActive && !ParryActive && state == MovementAnimationState.Idle);
            animator.SetBool("Move", !SwordActionActive && state == MovementAnimationState.Move);
            animator.SetBool("Jump", state == MovementAnimationState.Jump || state == MovementAnimationState.Fall || state == MovementAnimationState.WallJump);
            animator.SetBool("WallSlide", state == MovementAnimationState.WallSlide);
            animator.SetBool("Dash", state == MovementAnimationState.Dash);
            animator.SetBool("CounterAttack", ParryActive);
            animator.SetBool("SuccessfulCounterAttack", ParrySucceeded);
            animator.SetFloat("yVelocity", predictedJump ? predictedJumpVelocity : positions.YVelocity);
            if (exitedAttack)
            {
                string name = state == MovementAnimationState.Move ? "PlayerMove" : state == MovementAnimationState.Dash ? "PlayerDash" :
                    state == MovementAnimationState.WallSlide ? "PlayerWallSlide" : state == MovementAnimationState.Idle ? "PlayerIdle" : "Jmup/Fall";
                animator.Play("Base Layer." + name, 0, 0); animator.Update(0);
            }
        }
        private void ReleasePlayerAttack()
        {
            if (holdingPlayerAttack && animator != null) animator.speed = GetComponent<NetworkElementState>()?.NormalAnimationSpeed ?? savedPlayerSpeed;
            holdingPlayerAttack = false;
        }
        private void ReleaseAttackAnimation()
        {
            if (holdingAttackAnimation && animator != null) animator.speed = GetComponent<NetworkElementState>()?.NormalAnimationSpeed ?? savedAnimationSpeed;
            holdingAttackAnimation = false;
        }
        private void OnDisable() { positions.Clear(); hasPredictedPosition = false; predictedDash = suppressAuthorityDash = false; predictedJump = suppressAuthorityJump = false; localAttackRequestSeq = 0; localAttackAnimating = false; localAttackExecution = null; finishedLocalAttackInstanceId = 0; localSwordActionSeq = 0; localSwordAiming = localSwordThrowing = false; SwordActionActive = false; ReleaseAttackAnimation(); ReleasePlayerAttack(); PresentedPlayerAttackId = 0; PlayerAttackStartCount = 0; PlayerAttackPhase = 0; }
    }
}
