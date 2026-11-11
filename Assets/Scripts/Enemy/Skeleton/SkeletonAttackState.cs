using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkeletonAttackState : EnemyState
{
    private EnemySkeleton enemy;

    public SkeletonAttackState(Enemy _enemyBase, EnemyStateMachine _stateMachine, string _animBoolName, EnemySkeleton _enemy) : base(_enemyBase, _stateMachine, _animBoolName)
    {
        this.enemy = _enemy;
    }

    public override void Enter()
    {
        base.Enter();
        if (enemy.NetworkMovementOnly)
        {
            enemy.anim.SetBool("Idle", false);
            enemy.BeginNetworkAttack();
            return;
        }
        AudioManager.instance.PlaySFX(17, enemy.transform);
    }

    public override void Exit()
    {
        if (enemy.NetworkMovementOnly)
        {
            enemy.NetworkHits?.Reset();
            enemy.CloseCounterAttackWindow();
            enemy.anim.speed = enemy.GetComponent<Rpg.Networking.NetworkElementState>()?.NormalAnimationSpeed ?? 1;
        }
        base.Exit();
        enemy.lastTimeAttacked = GameSimulationClock.Time;
    }

    public override void Update()
    {
        base.Update();
        enemy.SetVelocity(0, 0);
        if (enemy.NetworkMovementOnly)
        {
            var attack = enemy.NetworkAttack;
            enemy.FlipController(attack.facingDir);
            if (GameSimulationClock.AuthorityTick - attack.startTick >= (ulong)attack.durationTicks)
                stateMachine.ChangeState(enemy.battleState);
            return;
        }
        if (triggerCalled) stateMachine.ChangeState(enemy.battleState);
    }
}
