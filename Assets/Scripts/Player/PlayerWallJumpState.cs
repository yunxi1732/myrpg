using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerWallJumpState : PlayerState
{
    public PlayerWallJumpState(Player _player, PlayerStateMachine _stateMachine, string _animBoolName) : base(_player, _stateMachine, _animBoolName)
    {
    }

    public override void Enter()
    {
        base.Enter();
        stateTimer = 0.2f;
        // A's validated wall-facing direction determines the impulse; SetVelocity also performs the original root flip.
        player.SetVelocity(-2 * player.facingDir, player.jumpForce);
    }

    public override void Exit()
    {
        base.Exit();
    }

    public override void Update()
    {
        base.Update();
        // This state deliberately does not apply xInput/yInput: its original 0.2s movement restriction remains in force.
        if (player.IsGroundDetected())
        {
            stateMachine.ChangeState(player.idleState);
            return;
        }
        if (stateTimer < 0)
        {
            stateMachine.ChangeState(player.airState);
            return;
        }
    }
}
