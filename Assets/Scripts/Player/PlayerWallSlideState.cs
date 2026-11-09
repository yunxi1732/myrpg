using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.IK;

public class PlayerWallSlideState : PlayerState
{
    public PlayerWallSlideState(Player _player, PlayerStateMachine _stateMachine, string _animBoolName) : base(_player, _stateMachine, _animBoolName)
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
        if (player.IsGroundDetected())
        {
            stateMachine.ChangeState(player.idleState);
            return;
        }
        if (player.IsWallDetected() == false)
        {
            stateMachine.ChangeState(player.airState);
            return;
        }

        if (player.CanUseLegacyActions && !GameplayControlLock.IsLocked && Input.GetKeyDown(KeyCode.Space))
        {
            stateMachine.ChangeState(player.wallJumpState);
            return;
        }
        if (xInput != 0 && xInput * player.facingDir < 0)
        {
            // Leaving a wall is airborne movement; Idle.Enter would erase the falling velocity.
            stateMachine.ChangeState(player.airState);
            player.SetVelocity(0.8f * xInput * player.moveSpeed, rb.velocity.y);
            return;
        }
        if (yInput < 0) rb.velocity = new Vector2(0, rb.velocity.y);
        else rb.velocity = new Vector2(0, rb.velocity.y * 0.8f);
    }
}
