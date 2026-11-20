using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class EnemyState
{
    public bool CanSimulate => enemyBase.IsAuthoritative && Rpg.Networking.EntityRuntimePermissions.Identity(enemyBase)?.MovementHeld != true &&
        (!enemyBase.NetworkMovementOnly || this == enemyBase.NetworkIdleState || this == enemyBase.NetworkMoveState ||
            this == enemyBase.NetworkBattleState || this == enemyBase.NetworkAttackState ||
            this == enemyBase.NetworkStunnedState || this == enemyBase.NetworkDeadState ||
            enemyBase is Enemy_Boss boss && (this == boss.teleportState || this == boss.castState));
    protected Enemy enemyBase;
    protected EnemyStateMachine stateMachine;
    protected Rigidbody2D rb;

    protected bool triggerCalled;
    private string animBoolName;
    protected float stateTimer;

    public EnemyState(Enemy _enemyBase, EnemyStateMachine _stateMachine, string _animBoolName)
    {
        this.enemyBase = _enemyBase;
        this.stateMachine = _stateMachine;
        this.animBoolName = _animBoolName;
    }

    public virtual void Enter()
    {
        triggerCalled = false;
        rb = enemyBase.rb;
        enemyBase.anim.SetBool(animBoolName, true);
    }
    public virtual void Update()
    {
        stateTimer -= GameSimulationClock.DeltaTime;
    }

    public virtual void Exit()
    {
        enemyBase.anim.SetBool(animBoolName, false);
        enemyBase.AssignLastAnimName(animBoolName);
    }

    public virtual void AnimationFinishTrigger()
    {
        triggerCalled = true;
    }
}
