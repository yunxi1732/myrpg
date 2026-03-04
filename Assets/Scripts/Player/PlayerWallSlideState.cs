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
        if (Input.GetKeyDown(KeyCode.Space))
        {
            stateMachine.ChangeState(player.wallJumpState);
            return;
        }
        if (yInput < 0) rb.velocity = new Vector2(0, rb.velocity.y);
        else rb.velocity = new Vector2(0, rb.velocity.y * 0.8f);
        if ((xInput != 0 && player.facingDir != xInput) || player.IsGroundDetected()) stateMachine.ChangeState(player.idleState);
    }
}
