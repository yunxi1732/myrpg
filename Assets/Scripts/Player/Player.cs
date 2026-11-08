using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore;
using Rpg.Networking;

public class Player : Entity
{
    public bool CanReadSimulationInput => EntityRuntimePermissions.CanReadSimulationInput(this);
    public bool CanUseLegacyActions => EntityRuntimePermissions.CanUseLegacyActions(this);
    public PlayerInputData InputData { get; } = new PlayerInputData();
    public bool IsRuntimeInitialized { get; private set; }
    public bool isBusy { get; private set; }
    [Header("Attack Details")]
    public float[] attackMovement;
    public float CounterAttackDuration = .2f;
    
    [Header("Move Info")]
    public float moveSpeed = 8f;
    public float jumpForce = 12f;
    public float swordReturnImpact = 5f;
    private float defaultMoveSpeed;
    private float defaultJumpForce;
    private float defaultDashSpeed;

    [Header("Dash Info")]
    public float dashSpeed = 40f;
    public float dashDuraiton = 0.1f;
    public float dashDir { get; private set; }

    public SkillManager skill {  get; private set; }
    public GameObject sword { get; private set; }
    public PlayerFX fx { get; private set; }

    #region State
    public PlayerStateMachine stateMachine { get; private set; }
    public PlayerIdleState idleState { get; private set; }
    public PlayerMoveState moveState { get; private set; }
    public PlayerJumpState jumpState { get; private set; }
    public PlayerAirState airState { get; private set; }
    public PlayerDashState dashState { get; private set; }
    public PlayerWallSlideState wallSlideState { get; private set; }
    public PlayerWallJumpState wallJumpState { get; private set; }
    public PlayerPrimaryAttack primaryAttackState { get; private set; }
    public PlayerCounterAttackState counterAttackState { get; private set; }
    public PlayerAimSwordState aimSwordState { get; private set; }
    public PlayerCatchSwordState catchSwordState { get; private set; }
    public PlayerBlackholeState blackHoleState { get; private set; }
    public PlayerDeadState deadState { get; private set; }


    #endregion

    protected override void Awake()
    {
        base.Awake();
        skill = CanUseLegacyActions ? SkillManager.instance : null;
        stateMachine = new PlayerStateMachine();
        
        idleState = new PlayerIdleState(this, stateMachine, "Idle");
        moveState = new PlayerMoveState(this, stateMachine, "Move");
        jumpState = new PlayerJumpState(this, stateMachine, "Jump");
        airState = new PlayerAirState(this, stateMachine, "Jump");
        dashState = new PlayerDashState(this, stateMachine, "Dash");
        wallSlideState = new PlayerWallSlideState(this, stateMachine, "WallSlide");
        wallJumpState = new PlayerWallJumpState(this, stateMachine, "Jump");
        primaryAttackState = new PlayerPrimaryAttack(this, stateMachine, "Attack");
        counterAttackState = new PlayerCounterAttackState(this, stateMachine, "CounterAttack");
        aimSwordState = new PlayerAimSwordState(this, stateMachine, "AimSword");
        catchSwordState = new PlayerCatchSwordState(this, stateMachine, "CatchSword");
        blackHoleState = new PlayerBlackholeState(this, stateMachine, "Jump");
        deadState = new PlayerDeadState(this, stateMachine, "Die");
    }

    protected override void Start()
    {
        base.Start();
        fx = GetComponent <PlayerFX>();
        if (IsAuthoritative) stateMachine.Initialize(idleState);

        defaultMoveSpeed = moveSpeed;
        defaultJumpForce = jumpForce;
        defaultDashSpeed = dashSpeed;
        IsRuntimeInitialized = true;
    }

    protected override void Update()
    {
        var identity = EntityRuntimePermissions.Identity(this);
        if (IsLocalInputOwner)
        {
            // B collects its own input without running simulation. A never samples keys for B.
            bool ready = identity == null ? CanReadSimulationInput : identity.CanCollectMovementInput;
            if (ready && Application.isFocused && !GameplayControlLock.IsLocked)
                InputData.SetCurrentAxis(Input.GetAxisRaw("Horizontal"));
            else
                InputData.Clear();
        }
        else if (!IsAuthoritative || identity?.MovementHeld == true)
            InputData.Clear();

        if (!IsAuthoritative || EntityRuntimePermissions.Identity(this)?.MovementHeld == true) return;
        if (!GameSimulationClock.IsNetwork && Time.timeScale == 0)
            return;

        if (!GameSimulationClock.IsNetwork) SimulateMovement();
        CheckForDashInput();

        if (CanUseLegacyActions && !GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.F) && skill.crystal.crystalUnlocked)
            skill.crystal.CanUseSkill();

        if (CanUseLegacyActions && !GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.Alpha1))
            Inventory.instance.UseFlask();
    }

    private void FixedUpdate()
    {
        // Network authority advances once per clock/automatic Physics2D fixed step, never again in Update.
        if (!GameSimulationClock.IsNetwork || !IsRuntimeInitialized || !IsAuthoritative ||
            EntityRuntimePermissions.Identity(this)?.MovementHeld == true) return;
        SimulateMovement();
    }

    private void SimulateMovement()
    {
        if (IsLocalInputOwner && GameplayControlLock.IsLocked)
        {
            InputData.Clear();
            ClearLockedMovement();
        }
        base.Update();
        stateMachine.currentState?.Update();
    }

    private void OnDisable() => InputData.Clear();

    public override void SlowEntityBy(float _slowPercentage, float _slowDuration)
    {
        if (!IsAuthoritative) return;
        moveSpeed *= (1 - _slowPercentage);
        jumpForce *= (1 - _slowPercentage);
        dashSpeed *= (1 - _slowPercentage);
        anim.speed *= (1 - _slowPercentage);

        GameSimulationClock.Schedule(this, ReturnDefaultSpeed, nameof(ReturnDefaultSpeed), _slowDuration);
    }

    protected override void ReturnDefaultSpeed()
    {
        if (!IsAuthoritative) return;
        base.ReturnDefaultSpeed();
        moveSpeed = defaultMoveSpeed;
        jumpForce = defaultJumpForce;
        dashSpeed = defaultDashSpeed;
    }

    public void AssignNewSword(GameObject _newSword) { if (IsAuthoritative) sword = _newSword; }

    public void CatchTheSword()
    {
        if (!IsAuthoritative) return;
        stateMachine.ChangeState(catchSwordState);
        Destroy(sword);
    }    

    public void CheckForDashInput()
    {
        if (!CanUseLegacyActions) return;
        if (GameplayControlLock.IsActionInputBlocked)
        {
            dashDir = 0;
            return;
        }
        if (IsWallDetected()) return;

        if (skill.dash.dashUnlocked == false)
            return;
        
        dashDir = InputData.CurrentAxis;
        if (dashDir == 0) dashDir = facingDir;
        if (Input.GetKeyDown(KeyCode.LeftShift) && SkillManager.instance.dash.CanUseSkill())
        {
            stateMachine.ChangeState(dashState);
        }
    }

    private void ClearLockedMovement()
    {
        CancelSwordAimForControlLock();
        PlayerState current = stateMachine?.currentState;
        current?.ClearMovementInput();
        dashDir = 0;
        if (current == null || rb == null) return;
        // A dash must not keep forcing y=0 after the dialogue releases its lock.
        if (current == dashState)
            stateMachine.ChangeState(IsGroundDetected() ? (PlayerState)idleState : airState);
        if (current == moveState || current == airState || current == jumpState ||
            current == wallJumpState || current == wallSlideState || current == dashState)
            SetVelocity(0, rb.velocity.y);
    }

    public void CancelSwordAimForControlLock()
    {
        if (!CanUseLegacyActions) return;
        skill?.sword?.CancelAim();
        if (aimSwordState != null && stateMachine?.currentState == aimSwordState && rb != null)
            aimSwordState.CancelForControlLock();
    }

    public void AnimationTrigger() { if (IsAuthoritative) stateMachine?.currentState?.AnimationFinishTrigger(); }

    public IEnumerator BusyFor(float _seconds)
    {
        if (!IsAuthoritative) yield break;
        isBusy = true;

        yield return GameSimulationClock.Wait(_seconds);

        isBusy = false;
    }

    public override void Die()
    {
        if (!IsAuthoritative) return;
        base.Die();
        stateMachine.ChangeState(deadState);
    }

    protected override void SetupZeroKnockbackPlayer()
    {
        knockbackPower = new Vector2(0, 0);
    }
}
