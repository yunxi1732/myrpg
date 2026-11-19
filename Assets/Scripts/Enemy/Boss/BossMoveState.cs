using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossMoveState : BossGroundedState
{
    public BossMoveState(Enemy _enemyBase, EnemyStateMachine _stateMachine, string _animBoolName, Enemy_Boss _enemy) : base(_enemyBase, _stateMachine, _animBoolName, _enemy)
    {
    }

    public override void Enter()
    {
        base.Enter();

        AudioManager.instance.PlaySFX(6, enemy.transform);
    }

    public override void Exit()
    {
        base.Exit();

        AudioManager.instance.StopSFX(6);
    }

    public override void Update()
    {
        base.Update();
        if (stateMachine.currentState != this) return;
        if (enemy.IsWallDetected() || !enemy.IsGroundAhead(enemy.facingDir))
        {
            enemy.SetVelocity(0, rb.linearVelocity.y);
            enemy.Flip();
            stateMachine.ChangeState(enemy.idleState);
            return;
        }
        enemy.SetVelocity(enemy.moveSpeed * enemy.facingDir, rb.linearVelocity.y);
    }
}
