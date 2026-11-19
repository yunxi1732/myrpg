using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class BossIdleState : EnemyState
{
    private Enemy_Boss enemy;
    public BossIdleState(Enemy _enemyBase, EnemyStateMachine _enemyStateMachine, string _animBoolName, Enemy_Boss _enemy) : base(_enemy, _enemyStateMachine, _animBoolName)
    {
        this.enemy = _enemy;
    }

    public override void Enter()
    {
        base.Enter();
        stateTimer = enemy.idleTime;
    }

    public override void Exit()
    {
        base.Exit();
    }

    public override void Update()
    {
        base.Update();
        if (enemy.SelectLivingTarget(true) != null)
        {
            stateMachine.ChangeState(enemy.battleState);
            return;
        }
        if (stateTimer < 0) stateMachine.ChangeState(enemy.moveState);
    }
}
