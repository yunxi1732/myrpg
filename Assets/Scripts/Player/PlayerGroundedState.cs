using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerGroundedState : PlayerState
{
    public PlayerGroundedState(Player _player, PlayerStateMachine _stateMachine, string _animBoolName) : base(_player, _stateMachine, _animBoolName)
    {
    }

    public override void Enter()
    {
        base.Enter();
    }

    public override void Exit()
    {
        base.Exit();
    }

    public override void Update()
    {
        base.Update();
        if (Input.GetKeyDown(KeyCode.R) && player.skill.blackhole.blackholeUnlocked)
        {
            if (player.skill.blackhole.cooldownTimer > 0)
                return;

            stateMachine.ChangeState(player.blackHoleState);
        }

        if (Input.GetKeyDown(KeyCode.Q) && player.skill.parry.parryUnlocked) 
            stateMachine.ChangeState(player.counterAttackState);

        if (Input.GetKeyDown(KeyCode.Mouse0)) 
            stateMachine.ChangeState(player.primaryAttackState);

        if (!player.IsGroundDetected()) 
            stateMachine.ChangeState(player.airState);

        if (Input.GetKeyDown(KeyCode.Space) && player.IsGroundDetected()) 
            stateMachine.ChangeState(player.jumpState);

        if (Input.GetKeyDown(KeyCode.Mouse1) && player.skill.sword.swordUnlocked == false)
        {
            player.skill.sword.CheckUnlock();
        }

        if (Input.GetKeyDown(KeyCode.Mouse1) && HasNoSword() && player.skill.sword.swordUnlocked) 
            stateMachine.ChangeState(player.aimSwordState);
    }

    private bool HasNoSword()
    {
        if (!player.sword) return true;
        player.sword.GetComponent<SwordSkillController>().ReturnSword();
        return false;
    }
}
