using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMoveState : PlayerGroundedState
{
    public PlayerMoveState(Player _player, PlayerStateMachine _stateMachine, string _animBoolName) : base(_player, _stateMachine, _animBoolName)
    {

    }

    public override void Enter()
    {
        base.Enter();

        AudioManager.TryPlaySFX(16, null);
    }

    public override void Exit()
    {
        base.Exit();

        AudioManager.TryStopSFX(16);
    }

    public override void Update()
    {
        base.Update();
        if (stateMachine.currentState != this) return;
        player.SetVelocity(xInput * player.moveSpeed, rb.linearVelocity.y);
        if (xInput == 0) stateMachine.ChangeState(player.idleState);
        
    }
}
