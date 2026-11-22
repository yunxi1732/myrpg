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
        if (enemy.NetworkMovementOnly) enemy.BeginNetworkAttack();
        else AudioManager.TryPlaySFX(17, enemy.transform);
        enemy.chanceToTeleport += 5;
    }

    public override void Exit()
    {
        if (enemy.NetworkMovementOnly)
        {
            enemy.NetworkHits?.Reset();
            enemy.anim.speed = enemy.GetComponent<Rpg.Networking.NetworkElementState>()?.NormalAnimationSpeed ?? 1;
        }
        base.Exit();
        enemy.lastTimeAttacked = GameSimulationClock.Time;
    }

    public override void Update()
    {
        base.Update();
        enemy.SetVelocity(0, rb.linearVelocity.y);
        bool finished = triggerCalled;
        if (enemy.NetworkMovementOnly)
        {
            var attack = enemy.NetworkAttack;
            enemy.FlipController(attack.facingDir);
            finished = GameSimulationClock.AuthorityTick - attack.startTick >= (ulong)attack.durationTicks;
        }
        if (finished)
        {
            if (enemy.CanTeleport()) stateMachine.ChangeState(enemy.teleportState);
            else stateMachine.ChangeState(enemy.battleState);
        }
    }
}
