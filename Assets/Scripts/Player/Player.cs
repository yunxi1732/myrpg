using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore;

public class Player : Entity
{
    public bool isBusy { get; private set; }
    [Header("Attack Details")]
    public float[] attackMovement;
    
    [Header("Move Info")]
    public float moveSpeed = 8f;
    public float jumpForce = 12f;

    [Header("Dash Info")]
    public float dashSpeed = 40f;
    public float dashDuraiton = 0.1f;
    public float dashCooldown = 1.5f;
    private float dashUsageTimer;
    public float dashDir { get; private set; }

    #region State
    public PlayerStateMachine stateMachine { get; private set; }
    public PlayerIdleState idleState { get; private set; }
    public PlayerMoveState moveState { get; private set; }
    public PlayerJumpState jumpState { get; private set; }
    public PlayerAirState airState { get; private set; }
    public PlayerDashState dashState { get; private set; }
    public PlayerWallSlideState wallSlideState { get; private set; }
    public PlayerWallJumpState wallJumpState { get; private set; }
    public PlayerPrimaryAttack primaryAttack { get; private set; }
    #endregion

    protected override void Awake()
    {
        base.Awake();
        stateMachine = new PlayerStateMachine();
        
        idleState = new PlayerIdleState(this, stateMachine, "Idle");
        moveState = new PlayerMoveState(this, stateMachine, "Move");
        jumpState = new PlayerJumpState(this, stateMachine, "Jump");
        airState = new PlayerAirState(this, stateMachine, "Jump");
        dashState = new PlayerDashState(this, stateMachine, "Dash");
        wallSlideState = new PlayerWallSlideState(this, stateMachine, "WallSlide");
        wallJumpState = new PlayerWallJumpState(this, stateMachine, "Jump");
        primaryAttack = new PlayerPrimaryAttack(this, stateMachine, "Attack");
    }

    protected override void Start()
    {
        base.Start();
        stateMachine.Initialize(idleState);
    }

    protected override void Update()
    {
        base.Update();
        stateMachine.currentState.Update();
        CheckForDashInput();
        if (Input.GetKeyDown(KeyCode.Mouse0)) stateMachine.ChangeState(primaryAttack);
    }


    public void CheckForDashInput()
    {
        dashUsageTimer -= Time.deltaTime;
        dashDir = Input.GetAxisRaw("Horizontal");
        if (dashDir == 0) dashDir = facingDir;
        if (Input.GetKeyDown(KeyCode.LeftShift) && dashUsageTimer < 0)
        {
            dashUsageTimer = dashCooldown;
            stateMachine.ChangeState(dashState);
        }
    }

    public void AnimationTrigger() => stateMachine.currentState.AnimationFinishTrigger();

    public IEnumerator BusyFor(float _seconds)
    {
        isBusy = true;

        yield return new WaitForSeconds(_seconds);

        isBusy = false;
    }

}
