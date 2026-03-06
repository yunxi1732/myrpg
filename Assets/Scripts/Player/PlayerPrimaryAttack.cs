using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerPrimaryAttack : PlayerState
{
    public PlayerPrimaryAttack(Player _player, PlayerStateMachine _stateMachine, string _animBoolName) : base(_player, _stateMachine, _animBoolName)
    {
    }

    private int comboCounter;
    private float lastTimeAttacked;
    private float comboWindow = 1.5f;

    public override void Enter()
    {
        base.Enter();
        if (Time.time > lastTimeAttacked + comboWindow || comboCounter > 2) comboCounter = 0;
        player.anim.SetInteger("comboCounter", comboCounter);
        stateTimer = 0.1f;
        player.SetVelocity(player.attackMovement[comboCounter] * player.facingDir, 2);
    }

    public override void Exit()
    {
        base.Exit();
        comboCounter++;
        lastTimeAttacked = Time.time;

    }

    public override void Update()
    {
        base.Update();
        if (triggerCalled) stateMachine.ChangeState(player.idleState);
        if (stateTimer < 0) player.SetVelocity(0, 0);
    }
}
