using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossAttackState : EnemyState
{
    private Enemy_Boss enemy;

    public BossAttackState(Enemy _enemyBase, EnemyStateMachine _stateMachine, string _animBoolName, Enemy_Boss _enemy) : base(_enemyBase, _stateMachine, _animBoolName)
    {
        this.enemy = _enemy;
    }

    public override void Enter()
    {
        base.Enter();
        AudioManager.instance.PlaySFX(17, enemy.transform);
        enemy.chanceToTeleport += 5;
    }

    public override void Exit()
    {
        base.Exit();
        enemy.lastTimeAttacked = GameSimulationClock.Time;
    }

    public override void Update()
    {
        base.Update();
        if (triggerCalled)
        {
            if (enemy.CanTeleport()) stateMachine.ChangeState(enemy.teleportState);
            else stateMachine.ChangeState(enemy.battleState);
        }
    }
}
