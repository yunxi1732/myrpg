using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAimSwordState : PlayerState
{
    private bool throwCommitted;
    public PlayerAimSwordState(Player _player, PlayerStateMachine _stateMachine, string _animBoolName) : base(_player, _stateMachine, _animBoolName)
    {
    }

    public override void Enter()
    {
        base.Enter();
        throwCommitted = false;
        if (GameplayControlLock.IsActionInputBlocked) { CancelForControlLock(); return; }
        player.skill.sword.BeginAim();
    }

    public override void Exit()
    {
        base.Exit();
        if (throwCommitted) player.StartCoroutine("BusyFor", 0.2f);
        else player.skill.sword.CancelAim();
    }

    public override void Update()
    {
        base.Update();
        if (GameplayControlLock.IsActionInputBlocked) { CancelForControlLock(); return; }

        player.SetVelocity(0, 0);
        if (Input.GetKeyUp(KeyCode.Mouse1))
        {
            throwCommitted = player.skill.sword.RequestThrow();
            stateMachine.ChangeState(player.idleState);
            return;
        }

        Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        if (player.transform.position.x > mousePosition.x && player.facingDir == 1) player.Flip();
        else if (player.transform.position.x < mousePosition.x && player.facingDir == -1) player.Flip();
    }

    public void CancelForControlLock()
    {
        throwCommitted = false;
        player.skill.sword.CancelAim();
        if (stateMachine.currentState == this)
        {
            player.SetVelocity(0, rb.velocity.y);
            stateMachine.ChangeState(player.IsGroundDetected() ? (PlayerState)player.idleState : player.airState);
        }
    }
}
