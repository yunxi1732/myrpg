using UnityEngine;

public class BossBattleState : BossGroundedState
{

    private Transform player;
    private int moveDir;
    public Transform Target => player;

    public void ForgetRoomPlayer(int entityId)
    {
        if (player == null || player.GetComponent<Rpg.Networking.NetworkEntityIdentity>()?.EntityId != entityId) return;
        player = null;
        if (stateMachine.currentState == this || stateMachine.currentState == enemy.attackState)
            stateMachine.ChangeState(enemy.idleState);
    }
    public BossBattleState(Enemy_Boss enemy, EnemyStateMachine stateMachine, string animBoolName) : base(enemy, stateMachine, animBoolName, enemy) { }


    public override void Enter()
    {
        base.Enter();
        Transform selected = enemy.SelectLivingTarget(false);
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

        if (enemy.IsTargetInAttackRange(player))
        {
            stateTimer = enemy.battleTime;
            enemy.SetVelocity(0, rb.linearVelocity.y);
            if (CanAttack()) stateMachine.ChangeState(enemy.attackState);
            return;
        }

        if (sensed != null)
        {
            stateTimer = enemy.battleTime;
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
