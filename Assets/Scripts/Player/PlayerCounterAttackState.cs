using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerCounterAttackState : PlayerState
{
    private bool canCreateClone;
    internal Rpg.Networking.RoomCombatDamage RoomDamage;
    public bool Succeeded { get; private set; }
    public void ConfirmNetworkSuccess()
    {
        Succeeded = true;
        stateTimer = 10f;
        player.anim.SetBool("SuccessfulCounterAttack", true);
    }

    public PlayerCounterAttackState(Player _player, PlayerStateMachine _stateMachine, string _animBoolName) : base(_player, _stateMachine, _animBoolName)
    {
    }

    public override void Enter()
    {
        base.Enter();
        canCreateClone = true;
        Succeeded = false;
        AudioManager.instance.PlaySFX(10, null);
        stateTimer = player.CounterAttackDuration;
        player.anim.SetBool("SuccessfulCounterAttack", false);
    }

    public override void Exit()
    {
        base.Exit();
    }

    public override void Update()
    {
        base.Update();
        player.SetVelocity(0, 0);
        if (GameSimulationClock.IsNetwork)
        {
            if (!Succeeded)
                foreach (var hit in Physics2D.OverlapCircleAll(player.attackCheck.position, player.attackCheckRadius))
                {
                    var enemy = hit.GetComponent<EnemySkeleton>();
                    if (enemy != null && RoomDamage.TryParry(player, enemy)) break;
                }
            if (stateTimer < 0 || triggerCalled) stateMachine.ChangeState(player.idleState);
            return;
        }
        Collider2D[] colliders = Physics2D.OverlapCircleAll(player.attackCheck.position, player.attackCheckRadius);
        foreach (var hit in colliders)
        {
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                if (enemy.CanBeStunned())
                {
                    stateTimer = 10f;
                    player.anim.SetBool("SuccessfulCounterAttack", true);

                    player.skill.parry.UseSkill();
                    if (canCreateClone)
                    {
                        canCreateClone = false;
                        player.skill.parry.MakeMirageOnParry(hit.transform);
                    }
                }
            }
        }

        if (stateTimer < 0 || triggerCalled) stateMachine.ChangeState(player.idleState);
    }
}
