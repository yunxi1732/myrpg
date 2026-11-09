using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerState 
{
    public bool CanSimulate => player.IsAuthoritative;
    protected PlayerStateMachine stateMachine;
    protected Player player;
    protected Rigidbody2D rb;

    protected float xInput;
    protected float yInput;
    private string animBoolName;
    protected float stateTimer;
    protected bool triggerCalled;

    public PlayerState(
        Player _player,
        PlayerStateMachine _stateMachine,
        string _animBoolName)
    {
        this.player = _player;
        this.stateMachine = _stateMachine;
        this.animBoolName = _animBoolName;
    }

    public virtual void Enter()
    {
        ClearMovementInput();
        player.anim.SetBool(animBoolName, true);
        rb = player.rb;
        triggerCalled = false;
    }

    public virtual void Update()
    {
        // Simulation reads this actor's current data; local keyboard ownership only gates collection.
        xInput = player.InputData.CurrentAxis;
        yInput = player.InputData.WallSlideDown ? -1 : 0;
        if (!CanSimulate || player.NetworkInputLocked || (player.IsLocalInputOwner && GameplayControlLock.IsLocked)) ClearMovementInput();
        player.anim.SetFloat("yVelocity", rb.velocity.y);
        stateTimer -= GameSimulationClock.DeltaTime;
    }

    public void ClearMovementInput()
    {
        xInput = 0;
        yInput = 0;
    }

    public virtual void Exit()
    {
        player.anim.SetBool(animBoolName, false);
    }

    public virtual void AnimationFinishTrigger()
    {
        triggerCalled = true;
    }
}
