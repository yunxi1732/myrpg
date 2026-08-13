using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Android;

public class PlayerDashState : PlayerState
{
    public PlayerDashState(Player _player, PlayerStateMachine _stateMachine, string _animBoolName) : base(_player, _stateMachine, _animBoolName)
    {
    }

    public override void Enter()
    {
        base.Enter();
        AudioManager.instance.PlaySFX(14, null);
        player.skill.dash.CloneOnDash();
        stateTimer = player.dashDuraiton;

        player.stats.MakeInvincible(true);
    }

    public override void Exit()
    {
        base.Exit();
        player.skill.dash.CloneOnArrival();
        player.SetVelocity(0, rb.velocity.y);

        player.stats.MakeInvincible(false);
    }

    public override void Update()
    {
        base.Update(); 
        if (!player.IsGroundDetected() && player.IsWallDetected()) stateMachine.ChangeState(player.wallSlideState);
        player.SetVelocity(player.dashSpeed * player.dashDir, 0);
        if (stateTimer < 0) stateMachine.ChangeState(player.idleState);

        player.fx.CreateAfterImage();
    }
}
