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
        player = enemy.SelectLivingTarget(true);
        if (player == null) { stateMachine.ChangeState(enemy.idleState); return; }
        if (enemy.NetworkMovementOnly)
        {
            stateTimer = enemy.battleTime;
            return;
        }
    }

    public override void Update()
    {
        Transform sensed = enemy.SelectLivingTarget(true);
        if (sensed != null) player = sensed;
        else if (!enemy.IsValidLivingTarget(player)) { stateMachine.ChangeState(enemy.idleState); return; }
        if (enemy.NetworkMovementOnly)
        {
            stateTimer -= GameSimulationClock.DeltaTime;
            bool detected = sensed != null;
            if (detected) stateTimer = enemy.battleTime;
            else if (stateTimer < 0 || Vector2.Distance(player.position, enemy.transform.position) > 10)
            { stateMachine.ChangeState(enemy.idleState); return; }
            float gap = player.position.x - enemy.transform.position.x;
            enemy.FlipController(gap);
            if (detected && Vector2.Distance(player.position, enemy.transform.position) < enemy.attackDistance && CanAttack())
            { enemy.SetVelocity(0, 0); stateMachine.ChangeState(enemy.attackState); return; }
            float direction = Mathf.Abs(gap) <= enemy.attackDistance ? 0 : Mathf.Sign(gap);
            enemy.SetVelocity(enemy.moveSpeed * direction, rb.velocity.y);
            enemy.anim.SetBool("Move", direction != 0);
            enemy.anim.SetBool("Idle", direction == 0);
            return; // Animation events stay disabled; A's fixed-tick attack window resolves hits separately.
        }
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

    public override void Exit()
    {
        if (enemy.NetworkMovementOnly) enemy.anim.SetBool("Idle", false);
        base.Exit();
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
