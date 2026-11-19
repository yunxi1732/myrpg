using UnityEngine;

public class BossTeleportState : EnemyState
{
    private Enemy_Boss enemy;

    public BossTeleportState(Enemy _enemyBase, EnemyStateMachine _stateMachine, string _animBoolName, Enemy_Boss _enemy) : base(_enemyBase, _stateMachine, _animBoolName)
    {
        this.enemy = _enemy;
    }

    public override void Enter()
    {
        base.Enter();
        enemy.stats.MakeInvincible(true);
        stateTimer = 1.1f;
    }

    public override void Update()
    {
        base.Update();
        if (stateTimer <= 0)
        {
            if(enemy.CanSpellCast()) stateMachine.ChangeState(enemy.castState);
            else stateMachine.ChangeState(enemy.battleState);
        }
    }

    public override void Exit()
    {
        base.Exit();
        enemy.stats.MakeInvincible(false);
    }
}
