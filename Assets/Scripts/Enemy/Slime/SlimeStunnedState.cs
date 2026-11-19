using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlimeStunnedState : EnemyState
{
    private Enemy_slime enemy;
    public SlimeStunnedState(Enemy _enemyBase, EnemyStateMachine _stateMachine, string _animBoolName, Enemy_slime _enemy) : base(_enemyBase, _stateMachine, _animBoolName)
    {
        this.enemy = _enemy;
    }

    public override void Enter()
    {
        base.Enter();
        stateTimer = enemy.stunnedDuration;
        rb.linearVelocity = new Vector2(-enemy.facingDir * enemy.stunnedDir.x, enemy.stunnedDir.y);
        enemy.fx.BeginStunBlink();
    }

    public override void Exit()
    {
        base.Exit();
        enemy.fx.EndStunBlink();
    }

    public override void Update()
    {
        base.Update();
        if (stateTimer < 0) stateMachine.ChangeState(enemy.idleState);
    }
}
