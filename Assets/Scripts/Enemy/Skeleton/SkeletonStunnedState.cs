using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkeletonStunnedState : EnemyState
{
    private EnemySkeleton enemy;
    public SkeletonStunnedState(Enemy _enemyBase, EnemyStateMachine _stateMachine, string _animBoolName, EnemySkeleton _enemy) : base(_enemyBase, _stateMachine, _animBoolName)
    {
        this.enemy = _enemy;
    }

    public override void Enter()
    {
        base.Enter();
        stateTimer = enemy.stunnedDuration;
        rb.velocity = new Vector2(-enemy.facingDir * enemy.stunnedDir.x, enemy.stunnedDir.y);
        enemy.fx.InvokeRepeating("RedColorBlink", 0, 0.1f);
    }

    public override void Exit()
    {
        base.Exit();
        enemy.fx.Invoke("CancelColorChange", 0);
    }

    public override void Update()
    {
        base.Update();
        if (stateTimer < 0) stateMachine.ChangeState(enemy.idleState);
    }
}
