using UnityEngine;

public class BossBattleState : BossGroundedState
{

    private Transform player;
    private int moveDir;
    public BossBattleState(Enemy_Boss enemy, EnemyStateMachine stateMachine, string animBoolName) : base(enemy, stateMachine, animBoolName, enemy) { }


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
            if (enemy.IsTargetInAttackRange(player))
            {
                enemy.SetVelocity(0, rb.linearVelocity.y);
                if (CanAttack()) stateMachine.ChangeState(enemy.attackState);
                else stateMachine.ChangeState(enemy.idleState);
                return;
            }
        }

        if (!enemy.IsGroundAhead(moveDir))
        {
            enemy.SetVelocity(0, rb.linearVelocity.y);
            return;
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
