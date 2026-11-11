using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemySkeleton : Enemy
{
    private long nextNetworkAttackId;
    private ulong counterOpenTick, counterCloseTick;
    public Rpg.Networking.Protocol.MonsterAttackDto NetworkAttack { get; private set; }
    public Rpg.Networking.MonsterAttackHitDetection NetworkHits { get; internal set; }
    public static bool TryNetworkAttackTiming(Animator animator, out Rpg.Networking.PrimaryAttackTiming timing)
    {
        timing = null;
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name != "SkeletonAttack") continue;
            float finish = clip.length;
            var hits = new List<float>();
            foreach (var marker in clip.events)
            {
                if (marker.functionName == "AnimationTrigger") finish = Mathf.Min(finish, marker.time);
                if (marker.functionName == "AttackTrigger") hits.Add(marker.time);
            }
            return Rpg.Networking.PrimaryAttackTiming.TryCreate(0, finish, clip.frameRate, GameSimulationClock.FixedDeltaTime, hits.ToArray(), out timing);
        }
        return false;
    }
    public static float AttackClipDuration(Animator animator, out float finishTime)
    {
        float length = 1.2f;
        finishTime = length;
        if (animator == null || animator.runtimeAnimatorController == null) return length;
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name != "SkeletonAttack") continue;
            length = clip.length; finishTime = length;
            foreach (AnimationEvent marker in clip.events)
                if (marker.functionName == "AnimationTrigger") { finishTime = marker.time; break; }
            return length;
        }
        return length;
    }

    public void BeginNetworkAttack()
    {
        if (!IsAuthoritative || !NetworkMovementOnly) return;
        AttackClipDuration(anim, out float finishTime);
        if (NetworkHits != null)
        {
            if (TryNetworkAttackTiming(anim, out var timing)) NetworkHits.Begin(GameSimulationClock.AuthorityTick, facingDir, timing);
            else { NetworkHits.Reset(); Debug.LogError("[MonsterHit] Invalid SkeletonAttack hit markers; damage window is closed."); }
        }
        NetworkAttack = new Rpg.Networking.Protocol.MonsterAttackDto { actionId = ++nextNetworkAttackId, actionNumber = 1,
            startTick = GameSimulationClock.AuthorityTick, facingDir = facingDir,
            durationTicks = Mathf.Max(1, Mathf.CeilToInt(finishTime / GameSimulationClock.FixedDeltaTime)) };
        foreach (var clip in anim.runtimeAnimatorController.animationClips)
            if (clip.name == "SkeletonAttack")
                foreach (var marker in clip.events)
                {
                    if (marker.functionName == "OpenCounterWindow")
                        counterOpenTick = NetworkAttack.startTick + (ulong)Mathf.CeilToInt(marker.time / GameSimulationClock.FixedDeltaTime);
                    if (marker.functionName == "CloseCounterWindow")
                        counterCloseTick = NetworkAttack.startTick + (ulong)Mathf.CeilToInt(marker.time / GameSimulationClock.FixedDeltaTime);
                }
        anim.fireEvents = false;
        anim.speed = 1;
        anim.Play("Base Layer.SkeletonAttack", 0, 0);
    }
    public void AdvanceNetworkCounterWindow(ulong tick)
    {
        bool open = NetworkAiState == Rpg.Networking.Protocol.MonsterAiState.Attack && tick >= counterOpenTick && tick <= counterCloseTick;
        if (open && !CounterWindowOpen) OpenCounterAttackWindow();
        else if (!open && CounterWindowOpen) CloseCounterAttackWindow();
    }

    public Rpg.Networking.Protocol.MonsterAiState NetworkAiState => stateMachine?.currentState == null || stats != null && stats.IsRuntimeInitialized && stats.currentHealth <= 0 ?
        Rpg.Networking.Protocol.MonsterAiState.Idle : stateMachine.currentState == attackState ?
        Rpg.Networking.Protocol.MonsterAiState.Attack : stateMachine.currentState == stunnedState ?
        Rpg.Networking.Protocol.MonsterAiState.Stunned : stateMachine.currentState == battleState ?
        Rpg.Networking.Protocol.MonsterAiState.Chase : stateMachine?.currentState == moveState ?
        Rpg.Networking.Protocol.MonsterAiState.Patrol : Rpg.Networking.Protocol.MonsterAiState.Idle;
    #region States
    public SkeletonIdleState idleState { get; private set; }
    public SkeletonMoveState moveState { get; private set; }
    public SkeletonBattleState battleState { get; private set; }
    public SkeletonAttackState attackState { get; private set; }
    public SkeletonStunnedState stunnedState { get; private set; }
    public SkeletonDeadState deadState { get; private set; }
    #endregion

    protected override void Awake()
    {
        base.Awake();
        idleState = new SkeletonIdleState(this, stateMachine, "Idle", this);
        moveState = new SkeletonMoveState(this, stateMachine, "Move", this);
        battleState = new SkeletonBattleState(this, stateMachine, "Move", this);
        attackState = new SkeletonAttackState(this, stateMachine, "Attack", this);
        stunnedState = new SkeletonStunnedState(this, stateMachine, "Stunned", this);
        deadState = new SkeletonDeadState(this, stateMachine, "Idle", this);
    }

    protected override void Start()
    {
        base.Start();
        if (!IsAuthoritative) return;
        stateMachine.Initialize(idleState);
    }

    protected override void Update()
    {
        base.Update();
    }
    private void OnDisable() => NetworkHits?.Reset();

    public override bool CanBeStunned()
    {
       if (base.CanBeStunned())
        {
            stateMachine.ChangeState(stunnedState);
            return true;
        }
        return false;
    }

    public override void Die()
    {
        if (!IsAuthoritative) return;
        base.Die();
        stateMachine.ChangeState(deadState);
    }
}
