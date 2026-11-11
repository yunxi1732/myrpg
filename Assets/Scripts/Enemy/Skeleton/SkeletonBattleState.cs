using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkeletonBattleState : SkeletonGroundedState
{
    private EnemySkeleton enemy;
    private Transform player;
    private int moveDir;
    public Transform Target => player;

    public SkeletonBattleState(Enemy _enemyBase, EnemyStateMachine _stateMachine, string _animBoolName, EnemySkeleton _enemy) : base(_enemyBase, _stateMachine, _animBoolName, _enemy)
    {
        this.enemy = _enemy;    
    }

    public override void Enter()
    {
        base.Enter();
        player = enemy.SelectLivingTarget(true);
        if (player == null) { stateMachine.ChangeState(enemy.idleState); return; }
    }

    public override void Update()
    {
        Transform sensed = enemy.SelectLivingTarget(true);
        if (sensed != null) player = sensed;
        else if (!enemy.IsValidLivingTarget(player)) { stateMachine.ChangeState(enemy.idleState); return; }
        base.Update();
        if (sensed != null)
        {
            stateTimer = enemy.battleTime;
            if (Vector2.Distance(player.position, enemy.transform.position) < enemy.attackDistance && CanAttack())
            { stateMachine.ChangeState(enemy.attackState); return; }
        }
        else
        {
            if (stateTimer < 0 || Vector2.Distance(player.transform.position, enemy.transform.position) > 10)
            { stateMachine.ChangeState(enemy.idleState); return; }
        }

        if (player.position.x > enemy.transform.position.x) moveDir = 1;
        else if (player.position.x < enemy.transform.position.x) moveDir = -1;

        enemy.SetVelocity(enemy.moveSpeed * moveDir, rb.velocity.y);
    }

    private bool CanAttack()
    {
        if (GameSimulationClock.Time > enemy.lastTimeAttacked + enemy.attackCooldown)
        {
            enemy.lastTimeAttacked = GameSimulationClock.Time;
            return true;
        }
        return false;
    }
}
