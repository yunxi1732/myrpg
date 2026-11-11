using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkeletonMoveState : SkeletonGroundedState
{
    public SkeletonMoveState(Enemy _enemyBase, EnemyStateMachine _stateMachine, string _animBoolName, EnemySkeleton _enemy) : base(_enemyBase, _stateMachine, _animBoolName, _enemy)
    {
    }

    public override void Enter()
    {
        base.Enter();

        if (!enemy.NetworkMovementOnly) AudioManager.instance.PlaySFX(6, enemy.transform);
    }

    public override void Exit()
    {
        base.Exit();

        if (!enemy.NetworkMovementOnly) AudioManager.instance.StopSFX(6);
    }

    public override void Update()
    {
        base.Update();
        if (stateMachine.currentState != this) return;
        enemy.SetVelocity(enemy.moveSpeed * enemy.facingDir, rb.linearVelocity.y);
        if (enemy.IsWallDetected() || !enemy.IsGroundDetected())
        {
            enemy.Flip();
            stateMachine.ChangeState(enemy.idleState);
        }
    }
}
