using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlimeBattleState : SlimeGroundedState
{
    private Transform player;
    private int moveDir;

    public SlimeBattleState(Enemy _enemyBase, EnemyStateMachine _stateMachine, string _animBoolName, Enemy_slime _enemy) : base(_enemyBase, _stateMachine, _animBoolName, _enemy)
    {
    }

    public override void Enter()
    {
        base.Enter();
        Transform selected = enemy.SelectLivingTarget(true);
        if (selected != null && selected != player)
            enemy.FlipController(selected.position.x - enemy.transform.position.x);
        player = selected;
        if (player == null) { stateMachine.ChangeState(enemy.idleState); return; }
    }

    public override void Update()
    {
        Transform sensed = enemy.SelectLivingTarget(true);
        if (sensed != null) player = sensed;
        else if (!enemy.IsValidLivingTarget(player)) { stateMachine.ChangeState(enemy.idleState); return; }
        base.Update();
        if (stateMachine.currentState != this) return;

        float targetOffsetX = player.position.x - enemy.transform.position.x;
        if (Mathf.Abs(targetOffsetX) > Mathf.Max(0.5f, enemy.cd.bounds.extents.x))
            moveDir = targetOffsetX > 0 ? 1 : -1;
        else
            moveDir = enemy.facingDir;
        enemy.FlipController(moveDir);

        if (sensed != null)
        {
            stateTimer = enemy.battleTime;
            if (Vector2.Distance(player.position, enemy.transform.position) <= enemy.attackDistance)
            {
                enemy.SetVelocity(0, rb.linearVelocity.y);
                if (CanAttack()) stateMachine.ChangeState(enemy.attackState);
                return;
            }
        }
        else
        {
            if (stateTimer < 0 || Vector2.Distance(player.transform.position, enemy.transform.position) > 10)
            { stateMachine.ChangeState(enemy.idleState); return; }
        }

        enemy.SetVelocity(enemy.moveSpeed * moveDir, rb.linearVelocity.y);
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
