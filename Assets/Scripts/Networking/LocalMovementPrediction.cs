using System.Collections.Generic;
using Rpg.Networking.Protocol;
using UnityEngine;

namespace Rpg.Networking
{
    [DisallowMultipleComponent, DefaultExecutionOrder(9100)]
    public sealed class LocalMovementPrediction : MonoBehaviour
    {
        [SerializeField] private bool logAuthorityComparisons = true;
        [SerializeField, Min(0)] private float settledPositionTolerance = 0.02f;
        [SerializeField, Min(0)] private float correctionDeadZone = 0.05f;
        [SerializeField, Min(0.01f)] private float snapDistance = 1.5f;
        [SerializeField, Min(0.01f)] private float smoothCorrectionSeconds = 0.2f;
        private struct InputRecord
        {
            public long Sequence;
            public float Axis;
            public Vector2 Position;
            public double SentAt;
        }

        private readonly Queue<InputRecord> recentInputs = new Queue<InputRecord>();
        private NetworkEntityIdentity identity;
        private RemotePlayerView display;
        private Player player;
        private CapsuleCollider2D body;
        private float predictedX;
        private bool started;
        private double nextComparisonLogTime;
        private double nextTestSummaryTime;
        private int comparedInputs, smoothCorrections, snapCorrections;
        private double totalAckMs, totalAbsoluteErrorX;
        private float maxAckMs, maxAbsoluteErrorX, maxCorrectionX;
        private float lastSentAxis;
        private long lastSentInputSeq, pendingDashActionSeq;
        private float dashRemaining, dashDirection;
        private long pendingJumpActionSeq;
        private float originalGravityScale, predictedY, jumpVelocityY, jumpHorizontalVelocity, wallJumpControlRemaining;
        private bool jumping, wallJumping, awaitingAuthorityLanding;
        private ulong acceptedJumpTick;
        private long releaseInputSeq;
        private int settledSnapshots;
        private float lastAuthorityX, lastAuthorityY, settledAuthorityX, settledAuthorityY;
        private bool hasSettledAuthority;
        private float correctionStartX, correctionTargetX, correctionElapsed;
        private float correctionStartY, correctionTargetY, correctionElapsedY;
        private bool smoothing, smoothingY, holdSettledY;
        public long LastComparedInputSeq { get; private set; }
        public float LastComparedAxis { get; private set; }
        public float LastComparedHorizontalError { get; private set; }

        public void RecordSentInput(long sequence, float axis)
        {
            recentInputs.Enqueue(new InputRecord { Sequence = sequence, Axis = axis,
                Position = new Vector2(started ? predictedX : transform.position.x, started ? predictedY : transform.position.y),
                SentAt = MonotonicClock.Now });
            while (recentInputs.Count > 64) recentInputs.Dequeue();
            lastSentInputSeq = sequence;
            if (axis != 0) releaseInputSeq = 0;
            else if (lastSentAxis != 0) releaseInputSeq = sequence;
            lastSentAxis = axis;
        }

        public void CompareAuthority(ulong authorityTick, long processedInputSeq, Vector2 authorityPosition, MovementAnimationState movementState)
        {
            if (awaitingAuthorityLanding && acceptedJumpTick > 0 && authorityTick > acceptedJumpTick &&
                (movementState == MovementAnimationState.Idle || movementState == MovementAnimationState.Move))
            {
                jumping = awaitingAuthorityLanding = false;
                jumpVelocityY = 0;
                display.EndPredictedJump();
            }
            if (processedInputSeq > LastComparedInputSeq)
            {
                while (recentInputs.Count > 0 && recentInputs.Peek().Sequence < processedInputSeq) recentInputs.Dequeue();
                if (recentInputs.Count > 0 && recentInputs.Peek().Sequence == processedInputSeq)
                {
                    InputRecord record = recentInputs.Dequeue();
                    LastComparedInputSeq = record.Sequence;
                    LastComparedAxis = record.Axis;
                    LastComparedHorizontalError = authorityPosition.x - record.Position.x;
                    float ackMs = (float)((MonotonicClock.Now - record.SentAt) * 1000);
                    float absoluteErrorX = Mathf.Abs(LastComparedHorizontalError);
                    comparedInputs++;
                    totalAckMs += ackMs; totalAbsoluteErrorX += absoluteErrorX;
                    maxAckMs = Mathf.Max(maxAckMs, ackMs);
                    maxAbsoluteErrorX = Mathf.Max(maxAbsoluteErrorX, absoluteErrorX);
                    if (logAuthorityComparisons && MonotonicClock.Now >= nextComparisonLogTime)
                    {
                        Debug.Log($"[RoomPrediction] ackInputSeq={record.Sequence} axis={record.Axis:0.##} " +
                            $"sentPredictedX={record.Position.x:0.###} authorityX={authorityPosition.x:0.###} " +
                            $"historicalErrorX={LastComparedHorizontalError:0.###} currentPredictedX={predictedX:0.###} inputAckMs={ackMs:0}");
                        nextComparisonLogTime = MonotonicClock.Now + 1;
                    }
                }
            }
            if (!started || jumping || awaitingAuthorityLanding || pendingJumpActionSeq != 0 || dashRemaining > 0 || pendingDashActionSeq != 0 ||
                player.InputData.CurrentAxis != 0 || releaseInputSeq == 0 ||
                processedInputSeq < releaseInputSeq || movementState != MovementAnimationState.Idle)
            { settledSnapshots = 0; hasSettledAuthority = holdSettledY = false; smoothing = smoothingY = false; return; }
            settledSnapshots = settledSnapshots > 0 &&
                Mathf.Abs(authorityPosition.x - lastAuthorityX) <= settledPositionTolerance &&
                Mathf.Abs(authorityPosition.y - lastAuthorityY) <= settledPositionTolerance
                ? settledSnapshots + 1 : 1;
            lastAuthorityX = authorityPosition.x; lastAuthorityY = authorityPosition.y;
            if (settledSnapshots >= 2)
            {
                settledAuthorityX = authorityPosition.x; settledAuthorityY = authorityPosition.y;
                hasSettledAuthority = holdSettledY = true;
            }
        }

        private void Awake()
        {
            identity = GetComponent<NetworkEntityIdentity>();
            display = GetComponent<RemotePlayerView>();
            player = GetComponent<Player>();
            body = GetComponent<CapsuleCollider2D>();
        }

        public void ConfigureGravity(float gravityScale) => originalGravityScale = gravityScale;

        public void BeginJump(long actionSeq)
        {
            if (jumping || pendingJumpActionSeq != 0 || dashRemaining > 0) return;
            bool groundJump = player.IsGroundDetected();
            bool wallJump = !groundJump && display.AuthorityMovementState == MovementAnimationState.WallSlide && player.IsWallDetected();
            if (!groundJump && !wallJump) return;
            if (!started) { predictedX = transform.position.x; predictedY = transform.position.y; started = true; }
            pendingJumpActionSeq = actionSeq;
            jumping = awaitingAuthorityLanding = true; wallJumping = wallJump;
            acceptedJumpTick = 0;
            predictedY = transform.position.y;
            jumpVelocityY = player.jumpForce;
            jumpHorizontalVelocity = wallJump ? -2 * player.facingDir : player.InputData.CurrentAxis * player.moveSpeed;
            wallJumpControlRemaining = wallJump ? 0.2f : 0;
            smoothing = smoothingY = holdSettledY = false; settledSnapshots = 0; hasSettledAuthority = false;
            display.ApplyPredictedPosition(new Vector2(predictedX, predictedY));
            display.BeginPredictedJump(wallJump ? -player.facingDir : player.facingDir);
        }

        public void ConfirmJump(JumpExecutionDto result)
        {
            Debug.Log($"[RoomJump] actionSeq={result.actionSeq} accepted={result.accepted} wall={result.wallJump} tick={result.authorityTick} facing={result.facingDir}");
            if (result.actionSeq != pendingJumpActionSeq) return;
            pendingJumpActionSeq = 0;
            if (result.accepted)
            {
                acceptedJumpTick = result.authorityTick;
                wallJumping = result.wallJump;
                if (wallJumping) jumpHorizontalVelocity = 2 * result.facingDir;
                display.ConfirmPredictedJump(result.authorityTick, result.facingDir);
            }
            else
            {
                jumping = awaitingAuthorityLanding = false;
                jumpVelocityY = 0;
                display.RejectPredictedJump();
            }
            if (player.InputData.CurrentAxis == 0 && releaseInputSeq == 0) releaseInputSeq = lastSentInputSeq;
        }

        public void BeginDash(long actionSeq, float axis, bool confirmedCooldownReady)
        {
            if (!confirmedCooldownReady || pendingDashActionSeq != 0 || !player.Combat.Skills.Get<DashSkill>().Unlocked) return;
            if (awaitingAuthorityLanding) { jumping = awaitingAuthorityLanding = false; pendingJumpActionSeq = 0; display.RejectPredictedJump(); }
            if (!started) { predictedX = transform.position.x; predictedY = transform.position.y; started = true; }
            pendingDashActionSeq = actionSeq;
            dashDirection = axis == 0 ? player.facingDir : Mathf.Sign(axis);
            dashRemaining = player.dashDuraiton;
            smoothing = smoothingY = holdSettledY = false; settledSnapshots = 0; hasSettledAuthority = false;
            display.ApplyPredictedPosition(new Vector2(predictedX, predictedY));
            display.BeginPredictedDash((int)dashDirection);
        }

        public void ConfirmDash(DashExecutionDto result)
        {
            Debug.Log($"[RoomDash] actionSeq={result.actionSeq} accepted={result.accepted} tick={result.authorityTick} facing={result.facingDir}");
            if (result.actionSeq != pendingDashActionSeq) return;
            pendingDashActionSeq = 0;
            if (result.accepted)
            {
                dashDirection = result.facingDir;
                display.ConfirmPredictedDash(result.authorityTick,
                    Mathf.CeilToInt(player.dashDuraiton / GameSimulationClock.FixedDeltaTime), result.facingDir);
            }
            else
            {
                dashRemaining = 0;
                display.RejectPredictedDash();
            }
            if (player.InputData.CurrentAxis == 0 && releaseInputSeq == 0) releaseInputSeq = lastSentInputSeq;
        }

        private void LateUpdate()
        {
            if (!identity.CanCollectMovementInput || identity.Role != NetworkEntityRole.Display || player.stats.currentHealth <= 0)
            {
                if (started || recentInputs.Count > 0 || lastSentInputSeq != 0) ResetPrediction();
                return;
            }
            float axis = player.InputData.CurrentAxis;
            if (axis != 0) { settledSnapshots = 0; hasSettledAuthority = false; smoothing = smoothingY = holdSettledY = false; }
            if (!started)
            {
                if (axis == 0) return;
                predictedX = transform.position.x; predictedY = transform.position.y;
                started = true;
            }
            bool dashing = dashRemaining > 0;
            float dashStep = dashing ? Mathf.Min(dashRemaining, GameSimulationClock.DeltaTime) : 0;
            if (dashing) dashRemaining -= dashStep;
            if (jumping && !dashing)
            {
                if (wallJumpControlRemaining > 0) wallJumpControlRemaining = Mathf.Max(0, wallJumpControlRemaining - GameSimulationClock.DeltaTime);
                else if (axis != 0) jumpHorizontalVelocity = 0.8f * axis * player.moveSpeed;
            }
            float displacement = dashing ? dashDirection * player.dashSpeed * dashStep :
                (jumping ? jumpHorizontalVelocity : axis * player.moveSpeed) * GameSimulationClock.DeltaTime;
            if (displacement != 0)
            {
                float requestedDistance = Mathf.Abs(displacement);
                float distance = requestedDistance;
                Vector2 direction = displacement > 0 ? Vector2.right : Vector2.left;
                Vector2 scale = transform.lossyScale;
                Vector2 size = new Vector2(body.size.x * Mathf.Abs(scale.x), body.size.y * Mathf.Abs(scale.y));
                Vector2 origin = new Vector2(predictedX, predictedY) +
                    (Vector2)transform.TransformVector(body.offset);
                RaycastHit2D hit = Physics2D.CapsuleCast(origin, size, body.direction, transform.eulerAngles.z,
                    direction, distance, player.AttackBlockingLayers);
                if (hit.collider != null) distance = Mathf.Max(0, hit.distance - 0.01f);
                predictedX += direction.x * distance;
                if (dashing && distance < requestedDistance) dashRemaining = 0;
            }
            if (dashing && dashRemaining <= 0) display.EndPredictedDash();
            if (jumping)
            {
                float step = jumpVelocityY * GameSimulationClock.DeltaTime;
                if (step != 0)
                {
                    Vector2 scale = transform.lossyScale;
                    Vector2 size = new Vector2(body.size.x * Mathf.Abs(scale.x), body.size.y * Mathf.Abs(scale.y));
                    Vector2 origin = new Vector2(predictedX, predictedY) + (Vector2)transform.TransformVector(body.offset);
                    Vector2 direction = step > 0 ? Vector2.up : Vector2.down;
                    RaycastHit2D hit = Physics2D.CapsuleCast(origin, size, body.direction, transform.eulerAngles.z,
                        direction, Mathf.Abs(step), player.AttackBlockingLayers);
                    bool blocked = hit.collider != null && Vector2.Dot(hit.normal, direction) < -0.5f;
                    predictedY += direction.y * (blocked ? Mathf.Max(0, hit.distance - 0.01f) : Mathf.Abs(step));
                    if (blocked)
                    {
                        jumpVelocityY = 0;
                        if (step < 0) { jumping = awaitingAuthorityLanding = false; display.EndPredictedJump(); }
                    }
                }
                if (jumping) jumpVelocityY += Physics2D.gravity.y * originalGravityScale * GameSimulationClock.DeltaTime;
                display.SetPredictedJumpVelocity(jumpVelocityY);
            }
            else if (!dashing && !holdSettledY)
            {
                jumpVelocityY += Physics2D.gravity.y * originalGravityScale * GameSimulationClock.DeltaTime;
                float fall = Mathf.Max(0, -jumpVelocityY * GameSimulationClock.DeltaTime);
                float lift = player.GroundCheckDistance;
                Vector2 scale = transform.lossyScale;
                Vector2 size = new Vector2(body.size.x * Mathf.Abs(scale.x), body.size.y * Mathf.Abs(scale.y));
                Vector2 origin = new Vector2(predictedX, predictedY + lift) + (Vector2)transform.TransformVector(body.offset);
                RaycastHit2D ground = Physics2D.CapsuleCast(origin, size, body.direction, transform.eulerAngles.z,
                    Vector2.down, lift + fall + 0.01f, player.AttackBlockingLayers);
                if (ground.collider != null && ground.normal.y > 0.5f)
                {
                    predictedY += lift - ground.distance + 0.01f;
                    jumpVelocityY = 0;
                }
                else predictedY -= fall;
            }
            if (axis == 0 && hasSettledAuthority)
            {
                float error = settledAuthorityX - predictedX;
                if (!smoothing && Mathf.Abs(error) > correctionDeadZone)
                {
                    if (Mathf.Abs(error) >= snapDistance)
                    {
                        predictedX = settledAuthorityX;
                        snapCorrections++;
                        maxCorrectionX = Mathf.Max(maxCorrectionX, Mathf.Abs(error));
                        if (logAuthorityComparisons)
                            Debug.Log($"[RoomPrediction] correction=Snap errorX={error:0.###} correctedX={predictedX:0.###}");
                    }
                    else
                    {
                        correctionStartX = predictedX;
                        correctionTargetX = settledAuthorityX;
                        correctionElapsed = 0;
                        smoothing = true;
                        smoothCorrections++;
                        maxCorrectionX = Mathf.Max(maxCorrectionX, Mathf.Abs(error));
                        if (logAuthorityComparisons)
                            Debug.Log($"[RoomPrediction] correction=Smooth errorX={error:0.###} duration={smoothCorrectionSeconds:0.###}s");
                    }
                }
                float errorY = settledAuthorityY - predictedY;
                if (!smoothingY && Mathf.Abs(errorY) > correctionDeadZone)
                {
                    if (Mathf.Abs(errorY) >= snapDistance)
                    {
                        predictedY = settledAuthorityY;
                        if (logAuthorityComparisons)
                            Debug.Log($"[RoomPrediction] correction=Snap errorY={errorY:0.###} correctedY={predictedY:0.###}");
                    }
                    else
                    {
                        correctionStartY = predictedY;
                        correctionTargetY = settledAuthorityY;
                        correctionElapsedY = 0;
                        smoothingY = true;
                        if (logAuthorityComparisons)
                            Debug.Log($"[RoomPrediction] correction=Smooth errorY={errorY:0.###} duration={smoothCorrectionSeconds:0.###}s");
                    }
                }
                hasSettledAuthority = false;
            }
            if (smoothing)
            {
                correctionElapsed = Mathf.Min(smoothCorrectionSeconds, correctionElapsed + GameSimulationClock.DeltaTime);
                predictedX = Mathf.Lerp(correctionStartX, correctionTargetX, correctionElapsed / smoothCorrectionSeconds);
                if (correctionElapsed >= smoothCorrectionSeconds) smoothing = false;
            }
            if (smoothingY)
            {
                correctionElapsedY = Mathf.Min(smoothCorrectionSeconds, correctionElapsedY + GameSimulationClock.DeltaTime);
                predictedY = Mathf.Lerp(correctionStartY, correctionTargetY, correctionElapsedY / smoothCorrectionSeconds);
                if (correctionElapsedY >= smoothCorrectionSeconds) smoothingY = false;
            }
            display.ApplyPredictedPosition(new Vector2(predictedX, predictedY));
            if (logAuthorityComparisons && comparedInputs > 0 && MonotonicClock.Now >= nextTestSummaryTime)
            {
                Debug.Log($"[RoomPredictionSummary] samples={comparedInputs} inputAckMs(avg/max)={totalAckMs / comparedInputs:0}/{maxAckMs:0} " +
                    $"absErrorX(avg/max)={totalAbsoluteErrorX / comparedInputs:0.###}/{maxAbsoluteErrorX:0.###} " +
                    $"correctionX(smooth/snap)={smoothCorrections}/{snapCorrections} maxCorrectionX={maxCorrectionX:0.###}");
                comparedInputs = smoothCorrections = snapCorrections = 0;
                totalAckMs = totalAbsoluteErrorX = 0;
                maxAckMs = maxAbsoluteErrorX = maxCorrectionX = 0;
                nextTestSummaryTime = MonotonicClock.Now + 10;
            }
        }

        public void ResetPrediction()
        {
            started = false;
            recentInputs.Clear();
            LastComparedInputSeq = 0;
            LastComparedAxis = LastComparedHorizontalError = 0;
            nextComparisonLogTime = 0;
            nextTestSummaryTime = 0;
            comparedInputs = smoothCorrections = snapCorrections = 0;
            totalAckMs = totalAbsoluteErrorX = 0;
            maxAckMs = maxAbsoluteErrorX = maxCorrectionX = 0;
            lastSentAxis = 0; lastSentInputSeq = 0; releaseInputSeq = 0;
            predictedX = predictedY = 0;
            pendingDashActionSeq = 0; dashRemaining = dashDirection = 0;
            pendingJumpActionSeq = 0; acceptedJumpTick = 0;
            jumping = wallJumping = awaitingAuthorityLanding = false;
            jumpVelocityY = jumpHorizontalVelocity = wallJumpControlRemaining = 0;
            settledSnapshots = 0; hasSettledAuthority = smoothing = smoothingY = holdSettledY = false;
            lastAuthorityX = lastAuthorityY = settledAuthorityX = settledAuthorityY = 0;
            correctionStartX = correctionTargetX = correctionElapsed = correctionStartY = correctionTargetY = correctionElapsedY = 0;
            if (player != null) player.InputData.Clear();
            if (display != null) display.RejectPredictedDash();
            if (display != null) display.RejectPredictedJump();
            if (display != null) display.ClearPredictedPosition();
        }

        private void OnDisable() => ResetPrediction();
    }
}
