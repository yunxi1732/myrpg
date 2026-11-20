using UnityEngine;

public class EnemySkeleton : Enemy
{
    public override string NetworkAttackClip => "SkeletonAttack";
    public override string NetworkMoveClip => "SkeletonMove";
    public override string NetworkIdleClip => "SkeletonIdle";
    public override string NetworkStunClip => "SkeletonStunned";
    public override string NetworkStunParameter => "Stunned";
    public override EnemyState NetworkIdleState => idleState;
    public override EnemyState NetworkMoveState => moveState;
    public override EnemyState NetworkBattleState => battleState;
    public override EnemyState NetworkAttackState => attackState;
    public override EnemyState NetworkStunnedState => stunnedState;
    public override EnemyState NetworkDeadState => deadState;
    public override Transform NetworkTarget => battleState?.Target;
    public override void ForgetNetworkPlayer(int entityId) => battleState?.ForgetRoomPlayer(entityId);
    public static bool TryNetworkAttackTiming(Animator animator, out Rpg.Networking.PrimaryAttackTiming timing) =>
        Enemy.TryNetworkAttackTiming(animator, "SkeletonAttack", out timing);
    public static float AttackClipDuration(Animator animator, out float finishTime) =>
        Enemy.AttackClipDuration(animator, "SkeletonAttack", out finishTime);
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
