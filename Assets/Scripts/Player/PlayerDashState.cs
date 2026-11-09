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
        if (GameSimulationClock.IsNetwork)
        {
            stateTimer = player.dashDuraiton;
            player.SetVelocity(player.dashSpeed * player.dashDir, 0);
            return; // Movement only: no clones, invincibility, afterimages or skill-manager access.
        }
        AudioManager.instance.PlaySFX(14, null);
        player.skill.dash.CloneOnDash();
        stateTimer = player.dashDuraiton;

        player.stats.MakeInvincible(true);
    }

    public override void Exit()
    {
        base.Exit();
        if (GameSimulationClock.IsNetwork)
        {
            player.SetVelocity(0, rb.velocity.y);
            return;
        }
        player.skill.dash.CloneOnArrival();
        player.SetVelocity(0, rb.velocity.y);

        player.stats.MakeInvincible(false);
    }

    public override void Update()
    {
        base.Update(); 
        if (GameSimulationClock.IsNetwork)
        {
            if (stateTimer <= 0.000001f)
            {
                stateMachine.ChangeState(player.IsGroundDetected() ? (PlayerState)player.idleState : player.airState);
                return;
            }
            if (!player.IsGroundDetected() && player.IsWallDetected())
            {
                stateMachine.ChangeState(player.wallSlideState);
                return;
            }
            player.SetVelocity(player.dashSpeed * player.dashDir, 0);
            return;
        }
        if (GameplayControlLock.IsLocked)
        {
            player.SetVelocity(0, rb.velocity.y);
            stateMachine.ChangeState(player.IsGroundDetected() ? (PlayerState)player.idleState : player.airState);
            return;
        }
        if (!player.IsGroundDetected() && player.IsWallDetected()) stateMachine.ChangeState(player.wallSlideState);
        player.SetVelocity(player.dashSpeed * player.dashDir, 0);
        if (stateTimer < 0) stateMachine.ChangeState(player.idleState);

        player.fx.CreateAfterImage();
    }
}
