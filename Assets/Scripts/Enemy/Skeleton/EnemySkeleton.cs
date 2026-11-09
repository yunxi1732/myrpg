using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemySkeleton : Enemy
{
    private long nextNetworkAttackId;
    public Rpg.Networking.Protocol.MonsterAttackDto NetworkAttack { get; private set; }
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
        NetworkAttack = new Rpg.Networking.Protocol.MonsterAttackDto { actionId = ++nextNetworkAttackId, actionNumber = 1,
            startTick = GameSimulationClock.AuthorityTick, facingDir = facingDir,
            durationTicks = Mathf.Max(1, Mathf.CeilToInt(finishTime / GameSimulationClock.FixedDeltaTime)) };
        anim.fireEvents = false;
        anim.Play("Base Layer.SkeletonAttack", 0, 0);
    }

    public Rpg.Networking.Protocol.MonsterAiState NetworkAiState => stateMachine?.currentState == null || stats != null && stats.IsRuntimeInitialized && stats.currentHealth <= 0 ?
        Rpg.Networking.Protocol.MonsterAiState.Idle : stateMachine.currentState == attackState ?
        Rpg.Networking.Protocol.MonsterAiState.Attack : stateMachine.currentState == battleState ?
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
