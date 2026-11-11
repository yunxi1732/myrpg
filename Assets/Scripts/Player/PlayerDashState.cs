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
            player.stats.MakeInvincible(true);
            if (player.IsAuthoritative && player.Combat.IsRoomNodeUnlocked("Dash - \"Here I am\""))
                player.RoomClones.Spawn(player);
            return;
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
            player.SetVelocity(0, rb.linearVelocity.y);
            player.stats.MakeInvincible(false);
            if (player.IsAuthoritative && player.Combat.IsRoomNodeUnlocked("Dash - \"Actually here I am\""))
                player.RoomClones.Spawn(player);
            return;
        }
        player.skill.dash.CloneOnArrival();
        player.SetVelocity(0, rb.linearVelocity.y);

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
            player.fx.CreateAfterImage();
            return;
        }
        if (GameplayControlLock.IsLocked)
        {
            player.SetVelocity(0, rb.linearVelocity.y);
            stateMachine.ChangeState(player.IsGroundDetected() ? (PlayerState)player.idleState : player.airState);
            return;
        }
        if (!player.IsGroundDetected() && player.IsWallDetected()) stateMachine.ChangeState(player.wallSlideState);
        player.SetVelocity(player.dashSpeed * player.dashDir, 0);
        if (stateTimer < 0) stateMachine.ChangeState(player.idleState);

        player.fx.CreateAfterImage();
    }
}
