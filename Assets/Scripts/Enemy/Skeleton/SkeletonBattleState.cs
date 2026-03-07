using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkeletonBattleState : SkeletonGroundedState
{
    private EnemySkeleton enemy;
    private Transform player;
    private int moveDir;

    public SkeletonBattleState(Enemy _enemyBase, EnemyStateMachine _stateMachine, string _animBoolName, EnemySkeleton _enemy) : base(_enemyBase, _stateMachine, _animBoolName, _enemy)
    {
        this.enemy = _enemy;    
    }

    public override void Enter()
    {
        base.Enter();
        player = PlayerManager.instance.player.transform;
    }

    public override void Update()
    {
        base.Update();
        if (enemy.isPlayerDetected())
        {
            stateTimer = enemy.battleTime;
            if (enemy.isPlayerDetected().distance < enemy.attackDistance && CanAttack()) stateMachine.ChangeState(enemy.attackState);
        }
        else
        {
            if (stateTimer < 0 || Vector2.Distance(player.transform.position, enemy.transform.position) > 10) stateMachine.ChangeState(enemy.idleState);
        }

        if (player.position.x > enemy.transform.position.x) moveDir = 1;
        else if (player.position.x < enemy.transform.position.x) moveDir = -1;

        enemy.SetVelocity(enemy.moveSpeed * moveDir, rb.velocity.y);
    }

    public override void Exit()
    {
        base.Exit();
    }

    private bool CanAttack()
    {
        if (Time.time > enemy.lastTimeAttacked + enemy.attackCooldown)
        {
            enemy.lastTimeAttacked = Time.time;
            return true;
        }
        return false;
    }
}
