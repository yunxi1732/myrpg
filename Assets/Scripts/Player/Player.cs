using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore;
using Rpg.Networking;
using Rpg.Networking.Protocol;

public class Player : Entity
{
    private static readonly HashSet<Player> activePlayers = new HashSet<Player>();
    public static IEnumerable<Player> ActivePlayers => activePlayers;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ResetActivePlayers() => activePlayers.Clear();
    public bool CanReadSimulationInput => EntityRuntimePermissions.CanReadSimulationInput(this);
    public bool CanUseLegacyActions => EntityRuntimePermissions.CanUseLegacyActions(this);
    public PlayerInputData InputData { get; } = new PlayerInputData();
    public long LastSimulatedMovementInputSeq { get; private set; }
    private long nextActionSeq, lastActionSeq, localJumpPress;
    private enum NetworkJumpKind { None, Ground, Wall }
    private NetworkJumpKind pendingJump;
    private long pendingJumpActionSeq, resolvedJumpActionSeq;
    private bool resolvedJumpAccepted, resolvedWallJump;
    private ulong resolvedJumpTick;
    private int resolvedJumpFacing;
    private long localDashPress;
    private float localDashAxis, pendingDashAxis;
    private bool dashPending;
    private long pendingDashActionSeq, resolvedDashActionSeq;
    private bool resolvedDashAccepted;
    private ulong resolvedDashTick;
    private int resolvedDashFacing;
    private long nextPrimaryAttackRequestSeq, localPrimaryAttackPress;
    private long resolvedPrimaryAttackRequestSeq;
    private PrimaryAttackInstance resolvedPrimaryAttackInstance;
    private PrimaryAttackRejection resolvedPrimaryAttackRejection;
    private long nextFlaskRequestSeq, localFlaskPress;
    private long nextCrystalRequestSeq, localCrystalPress;
    [Header("Network Primary Attack (A configuration)")]
    [Min(0.01f)] public float networkPrimaryAttackInterval = 0.8f;
    public float NetworkDashCooldownRemaining => Combat.Skills.Get<DashSkill>().Remaining;
    public bool NetworkInputLocked { get; private set; }

    private void OnEnable()
    {
        activePlayers.Add(this);
        GameplayControlLock.Changed += OnLocalInputLockChanged;
        if (GameplayControlLock.IsLocked) OnLocalInputLockChanged(true);
    }

    private void OnLocalInputLockChanged(bool locked)
    {
        if (IsLocalInputOwner && GameSimulationClock.IsNetwork) SetNetworkInputLocked(locked);
    }

    public void SetNetworkInputLocked(bool locked)
    {
        NetworkInputLocked = locked;
        if (dashPending) ResolveNetworkDash(false);
        if (pendingJump != NetworkJumpKind.None) ResolveNetworkJump(false, false);
        InputData.Clear(); localJumpPress = 0; localDashPress = 0;
        pendingJump = NetworkJumpKind.None; dashPending = false;
        localPrimaryAttackPress = 0; combat?.PrimaryAttack?.CancelPending();
        localFlaskPress = 0;
        localCrystalPress = 0;
        if (locked && IsAuthoritative) ClearLockedMovement();
    }
    public MovementAnimationState MovementAnimation
    {
        get
        {
            var state = stateMachine.currentState;
            if (stats.NetworkHealthVersion > 0 && stats.currentHealth <= 0) return MovementAnimationState.Idle;
            if (state == moveState) return MovementAnimationState.Move;
            if (state == jumpState) return MovementAnimationState.Jump;
            if (state == airState) return MovementAnimationState.Fall;
            if (state == wallSlideState) return MovementAnimationState.WallSlide;
            if (state == wallJumpState) return MovementAnimationState.WallJump;
            if (state == dashState) return MovementAnimationState.Dash;
            if (state == counterAttackState) return counterAttackState.Succeeded ? MovementAnimationState.ParrySuccess : MovementAnimationState.Parry;
            return MovementAnimationState.Idle;
        }
    }
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

    private PlayerCombatContext combat;
    public PlayerCombatContext Combat => combat ?? (combat = new PlayerCombatContext(this));
    public SkillManager skill => Combat.LegacySkills;
    public SwordSkill RoomSword { get; internal set; }
    public Rpg.Networking.RoomCloneNetwork RoomClones { get; internal set; }
    public Rpg.Networking.RoomBlackholeNetwork RoomBlackhole { get; internal set; }
    public SwordSkill SwordAbility => RoomSword != null ? RoomSword : skill.sword;
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
        _ = Combat;
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
        if (CanUseLegacyActions)
        {
            SkillManager.instance?.BindOwner(this);
            Inventory.instance?.BindOwner(this);
        }
        fx = GetComponent <PlayerFX>();
        if (IsAuthoritative) stateMachine.Initialize(idleState);

        defaultMoveSpeed = moveSpeed;
        defaultJumpForce = jumpForce;
        defaultDashSpeed = dashSpeed;
        IsRuntimeInitialized = true;
    }

    protected override void Update()
    {
        localJumpPress = 0; // A display-side press exists for this frame only, never a held/repeated input state.
        localDashPress = 0;
        localPrimaryAttackPress = 0;
        localFlaskPress = 0;
        localCrystalPress = 0;
        var identity = EntityRuntimePermissions.Identity(this);
        if (GameSimulationClock.IsNetwork && stats != null && stats.currentHealth <= 0)
        {
            InputData.Clear();
            return;
        }
        if (IsLocalInputOwner)
        {
            // B collects its own input without running simulation. A never samples keys for B.
            bool ready = identity == null ? CanReadSimulationInput : identity.CanCollectMovementInput;
            if (ready && Application.isFocused && !GameplayControlLock.IsLocked &&
                !(GameSimulationClock.IsNetwork && GameplayControlLock.IsActionInputBlocked))
                InputData.SetCurrentAxis(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical") < 0);
            else
                InputData.Clear();
        }
        else if (!IsAuthoritative || identity?.MovementHeld == true)
            InputData.Clear();

        if (GameSimulationClock.IsNetwork && identity != null && identity.CanCollectMovementInput &&
            Application.isFocused && !GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.Alpha1))
            localFlaskPress = checked(++nextFlaskRequestSeq);

        if (GameSimulationClock.IsNetwork && identity != null && identity.CanCollectMovementInput &&
            Application.isFocused && !GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.Mouse0))
            localPrimaryAttackPress = checked(++nextPrimaryAttackRequestSeq);

        if (GameSimulationClock.IsNetwork && identity != null && identity.CanCollectMovementInput &&
            Application.isFocused && !GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.F))
            localCrystalPress = checked(++nextCrystalRequestSeq);

        if (GameSimulationClock.IsNetwork && identity != null && identity.CanCollectMovementInput &&
            Application.isFocused && !GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.Space))
        {
            long sequence = checked(++nextActionSeq);
            if (IsAuthoritative) TryQueueNetworkJump(sequence);
            else localJumpPress = sequence;
        }
        if (GameSimulationClock.IsNetwork && identity != null && identity.CanCollectMovementInput &&
            Application.isFocused && !GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.LeftShift))
        {
            long sequence = checked(++nextActionSeq);
            float axis = InputData.CurrentAxis;
            if (IsAuthoritative) TryQueueNetworkDash(sequence, axis);
            else { localDashPress = sequence; localDashAxis = axis; }
        }

        if (!IsAuthoritative || EntityRuntimePermissions.Identity(this)?.MovementHeld == true) return;
        if (!GameSimulationClock.IsNetwork && Time.timeScale == 0)
            return;

        if (!GameSimulationClock.IsNetwork) SimulateMovement();
        CheckForDashInput();

        if (CanUseLegacyActions && !GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.F) && skill.crystal.crystalUnlocked)
            skill.crystal.CanUseSkill();

        if (CanUseLegacyActions && !GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.Alpha1))
            Combat.LegacyInventory?.UseFlask();
    }

    private void FixedUpdate()
    {
        // Network authority advances once per clock/automatic Physics2D fixed step, never again in Update.
        if (!GameSimulationClock.IsNetwork || !IsRuntimeInitialized || !IsAuthoritative) return;
        Combat.Skills.Advance(GameSimulationClock.FixedDeltaTime);
        if (EntityRuntimePermissions.Identity(this)?.MovementHeld == true) return;
        GetComponent<NetworkHitFeedback>()?.AdvanceAuthorityKnockback(GameSimulationClock.AuthorityTick);
        if (stats != null && stats.currentHealth <= 0) { HoldNetworkAtZeroHealth(); return; }
        SimulateMovement();
        LastSimulatedMovementInputSeq = InputData.InputSeq;
        Combat.PrimaryAttackHits?.Step(GameSimulationClock.AuthorityTick);
    }

    private void SimulateMovement()
    {
        if (NetworkInputLocked || IsLocalInputOwner && GameplayControlLock.IsLocked)
        {
            if (dashPending) ResolveNetworkDash(false);
            if (pendingJump != NetworkJumpKind.None) ResolveNetworkJump(false, false);
            if (!IsLocalInputOwner && Combat.PrimaryAttack?.HasPending == true)
            {
                resolvedPrimaryAttackRequestSeq = Combat.PrimaryAttack.PendingRequestSeq;
                resolvedPrimaryAttackInstance = null;
                resolvedPrimaryAttackRejection = PrimaryAttackRejection.Locked;
            }
            InputData.Clear();
            localJumpPress = 0; localDashPress = 0; pendingJump = NetworkJumpKind.None; dashPending = false;
            localPrimaryAttackPress = 0; combat?.PrimaryAttack?.CancelPending();
            ClearLockedMovement();
        }
        base.Update();
        if (GameSimulationClock.IsNetwork && Combat.PrimaryAttack?.HasPending == true)
        {
            var runtime = Combat.PrimaryAttack;
            var condition = CheckNetworkPrimaryAttack();
            int combo = runtime.PeekComboIndex(GameSimulationClock.AuthorityTick, PlayerPrimaryAttack.NetworkComboWindowTicks);
            PlayerPrimaryAttack.TryNetworkTiming(anim, combo, out var timing);
            int interval = condition == PrimaryAttackRejection.None ? Mathf.CeilToInt(networkPrimaryAttackInterval / GameSimulationClock.FixedDeltaTime) : 0;
            long requestSeq = runtime.PendingRequestSeq;
            bool began = runtime.TryBegin(GameSimulationClock.AuthorityTick, condition, timing, interval, PlayerPrimaryAttack.NetworkComboWindowTicks);
            if (!IsLocalInputOwner)
            {
                resolvedPrimaryAttackRequestSeq = requestSeq;
                resolvedPrimaryAttackInstance = began ? runtime.Current : null;
                resolvedPrimaryAttackRejection = began ? PrimaryAttackRejection.None : runtime.LastRejection;
            }
            if (began)
            {
                runtime.Current.SetFacingAtStart(facingDir);
                stateMachine.ChangeState(primaryAttackState);
                var attack = runtime.Current;
                Debug.Log($"[PrimaryAttack] START player={attack.PlayerId} entity={attack.EntityId} requestSeq={attack.RequestSeq} attackInstanceId={attack.AttackInstanceId} combo={attack.ComboIndex + 1} segments={attack.Segments.Count} tick={attack.StartTick} endTick={attack.EndTick}");
                return;
            }
            Debug.Log($"[PrimaryAttack] REJECT player={Combat.PlayerId} phase=execute reason={runtime.LastRejection}");
        }
        if (GameSimulationClock.IsNetwork && dashPending)
        {
            dashPending = false;
            if (CanPerformNetworkDash())
            {
                dashDir = pendingDashAxis == 0 ? facingDir : Mathf.Sign(pendingDashAxis);
                stateMachine.ChangeState(dashState);
                Combat.Skills.Commit<DashSkill>();
                ResolveNetworkDash(true);
                return;
            }
            ResolveNetworkDash(false);
        }
        // Preserve WallSlide's original Space-before-reverse-input priority. A rechecks its wall before taking off.
        if (GameSimulationClock.IsNetwork && pendingJump == NetworkJumpKind.Wall)
        {
            pendingJump = NetworkJumpKind.None;
            if (GetNetworkJumpKind() == NetworkJumpKind.Wall)
            {
                stateMachine.ChangeState(wallJumpState);
                ResolveNetworkJump(true, true);
                return; // Do not consume the 0.2s control window in the entry step.
            }
            ResolveNetworkJump(false, false);
        }
        stateMachine.currentState.Update();
        if (GameSimulationClock.IsNetwork && pendingJump == NetworkJumpKind.Ground)
        {
            pendingJump = NetworkJumpKind.None; // Consume once; never turn an airborne request into a later jump.
            if (GetNetworkJumpKind() == NetworkJumpKind.Ground)
            {
                stateMachine.ChangeState(jumpState);
                ResolveNetworkJump(true, false);
            }
            else ResolveNetworkJump(false, false);
        }
    }

    public bool TryConsumeJumpPress(out long actionSeq)
    {
        actionSeq = localJumpPress;
        localJumpPress = 0;
        return actionSeq > 0;
    }

    public bool TryConsumePrimaryAttackPress(out long requestSeq)
    {
        requestSeq = localPrimaryAttackPress; localPrimaryAttackPress = 0;
        return requestSeq > 0;
    }

    public bool TryConsumeNetworkPrimaryAttackResolution(out long requestSeq, out PrimaryAttackInstance instance,
        out PrimaryAttackRejection rejection)
    {
        requestSeq = resolvedPrimaryAttackRequestSeq;
        instance = resolvedPrimaryAttackInstance;
        rejection = resolvedPrimaryAttackRejection;
        resolvedPrimaryAttackRequestSeq = 0;
        resolvedPrimaryAttackInstance = null;
        resolvedPrimaryAttackRejection = PrimaryAttackRejection.None;
        return requestSeq > 0;
    }
    public bool TryConsumeFlaskPress(out long requestSeq)
    {
        requestSeq = localFlaskPress; localFlaskPress = 0; return requestSeq > 0;
    }
    public bool TryConsumeCrystalPress(out long requestSeq)
    {
        requestSeq = localCrystalPress; localCrystalPress = 0; return requestSeq > 0;
    }

    internal PrimaryAttackRejection CheckNetworkPrimaryAttack()
    {
        var identity = EntityRuntimePermissions.Identity(this);
        if (!GameSimulationClock.IsNetwork || !IsAuthoritative || identity == null || !identity.IsBound || identity.MovementHeld ||
            !IsRuntimeInitialized || !isActiveAndEnabled || rb == null || stats == null || !stats.IsRuntimeInitialized ||
            Combat.RoomConfigurationId == null || Combat.PrimaryAttack == null) return PrimaryAttackRejection.NotReady;
        if (NetworkInputLocked || IsLocalInputOwner && GameplayControlLock.IsActionInputBlocked) return PrimaryAttackRejection.Locked;
        if (stats.isDead || stats.currentHealth <= 0) return PrimaryAttackRejection.Dead;
        if (isBusy || isKnocked || GetComponent<NetworkHitFeedback>()?.IsAuthorityKnockbackActive == true || dashPending || pendingJump != NetworkJumpKind.None) return PrimaryAttackRejection.Busy;
        if ((stateMachine?.currentState != idleState && stateMachine?.currentState != moveState) || !IsGroundDetected())
            return PrimaryAttackRejection.ActionState;
        int combo = Combat.PrimaryAttack.PeekComboIndex(GameSimulationClock.AuthorityTick, PlayerPrimaryAttack.NetworkComboWindowTicks);
        if (!(networkPrimaryAttackInterval > 0) || float.IsInfinity(networkPrimaryAttackInterval) || networkPrimaryAttackInterval > 60 ||
            attackMovement == null || attackMovement.Length <= combo || float.IsNaN(attackMovement[combo]) || float.IsInfinity(attackMovement[combo]) ||
            !PlayerPrimaryAttack.TryNetworkTiming(anim, combo, out _) || attackCheck == null || !attackCheck.IsChildOf(transform) ||
            !(attackCheckRadius > 0) || float.IsInfinity(attackCheckRadius)) return PrimaryAttackRejection.Configuration;
        return PrimaryAttackRejection.None;
    }

    public bool TryQueueNetworkJump(long actionSeq)
    {
        return TryQueueNetworkMovementAction(actionSeq, false, 0);
    }

    public bool TryConsumeNetworkJumpResolution(out long actionSeq, out bool accepted, out bool wallJump, out ulong tick, out int facing)
    {
        actionSeq = resolvedJumpActionSeq; accepted = resolvedJumpAccepted; wallJump = resolvedWallJump;
        tick = resolvedJumpTick; facing = resolvedJumpFacing;
        resolvedJumpActionSeq = 0;
        return actionSeq > 0;
    }

    private void ResolveNetworkJump(bool accepted, bool wallJump)
    {
        resolvedJumpActionSeq = pendingJumpActionSeq;
        resolvedJumpAccepted = accepted;
        resolvedWallJump = wallJump;
        resolvedJumpTick = GameSimulationClock.AuthorityTick;
        resolvedJumpFacing = accepted ? facingDir : 0;
        pendingJumpActionSeq = 0;
        pendingJump = NetworkJumpKind.None;
    }

    private NetworkJumpKind GetNetworkJumpKind()
    {
        if (NetworkInputLocked || stats.isDead || stats.currentHealth <= 0 || isBusy || isKnocked ||
            GetComponent<NetworkHitFeedback>()?.IsAuthorityKnockbackActive == true ||
            GetComponent<NetworkEntityIdentity>().MovementHeld ||
            IsLocalInputOwner && GameplayControlLock.IsActionInputBlocked) return NetworkJumpKind.None;
        var current = stateMachine.currentState;
        bool grounded = IsGroundDetected();
        if ((current == idleState || current == moveState) && grounded) return NetworkJumpKind.Ground;
        if (current == wallSlideState && !grounded && IsWallDetected()) return NetworkJumpKind.Wall;
        return NetworkJumpKind.None;
    }

    internal void ConfigureNetworkDash(float cooldown)
    {
        Combat.Skills.Configure<DashSkill>(cooldown);
    }

    public bool TryConsumeDashPress(out long actionSeq, out float axis)
    {
        actionSeq = localDashPress; axis = localDashAxis; localDashPress = 0;
        return actionSeq > 0;
    }

    public bool TryQueueNetworkDash(long actionSeq, float axis)
    {
        return TryQueueNetworkMovementAction(actionSeq, true, axis);
    }

    public bool TryConsumeNetworkDashResolution(out long actionSeq, out bool accepted, out ulong tick, out int facing)
    {
        actionSeq = resolvedDashActionSeq; accepted = resolvedDashAccepted;
        tick = resolvedDashTick; facing = resolvedDashFacing;
        resolvedDashActionSeq = 0;
        return actionSeq > 0;
    }

    private void ResolveNetworkDash(bool accepted)
    {
        resolvedDashActionSeq = pendingDashActionSeq;
        resolvedDashAccepted = accepted;
        resolvedDashTick = GameSimulationClock.AuthorityTick;
        resolvedDashFacing = accepted ? facingDir : 0;
        pendingDashActionSeq = 0;
        dashPending = false;
    }

    private bool TryQueueNetworkMovementAction(long actionSeq, bool dash, float axis)
    {
        if (!GameSimulationClock.IsNetwork || !IsAuthoritative || actionSeq <= 0 || actionSeq <= lastActionSeq) return false;
        lastActionSeq = actionSeq; // Rejected presses are consumed too; they cannot execute after the state changes.
        if (dashPending || pendingJump != NetworkJumpKind.None) return false;
        if (dash)
        {
            if (!CanPerformNetworkDash()) return false;
            pendingDashAxis = axis; pendingDashActionSeq = actionSeq; dashPending = true;
            return true;
        }
        NetworkJumpKind kind = GetNetworkJumpKind();
        if (kind == NetworkJumpKind.None) return false;
        pendingJump = kind; pendingJumpActionSeq = actionSeq;
        return true;
    }

    private bool CanPerformNetworkDash()
    {
        if (NetworkInputLocked || !Combat.Skills.Get<DashSkill>().Unlocked ||
            stats.isDead || stats.currentHealth <= 0 || isBusy || isKnocked || GetComponent<NetworkHitFeedback>()?.IsAuthorityKnockbackActive == true || NetworkDashCooldownRemaining > 0.000001f ||
            GetComponent<NetworkEntityIdentity>().MovementHeld ||
            IsLocalInputOwner && GameplayControlLock.IsActionInputBlocked) return false;
        var current = stateMachine.currentState;
        return (current == idleState || current == moveState || current == jumpState || current == airState) && !IsWallDetected();
    }

    private void OnDisable()
    {
        combat?.PrimaryAttackHits?.Reset();
        combat?.PrimaryAttack?.InterruptCurrent();
        activePlayers.Remove(this);
        GameplayControlLock.Changed -= OnLocalInputLockChanged;
        InputData.Clear(); localJumpPress = 0; pendingJump = NetworkJumpKind.None;
        localDashPress = 0; dashPending = false;
        localPrimaryAttackPress = 0; combat?.PrimaryAttack?.CancelPending();
        localFlaskPress = 0; localCrystalPress = 0;
    }

    private void OnDestroy() => combat?.ReleaseRoomEquipment();

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
        if (Input.GetKeyDown(KeyCode.LeftShift) && skill.dash.CanUseSkill())
        {
            stateMachine.ChangeState(dashState);
        }
    }

    private void ClearLockedMovement()
    {
        CancelSwordAimForControlLock();
        PlayerState current = stateMachine?.currentState;
        current?.ClearMovementInput();
        if (GameSimulationClock.IsNetwork && current == dashState) return; // Do not cancel a committed authority dash with local UI.
        dashDir = 0;
        if (current == null || rb == null) return;
        // A dash must not keep forcing y=0 after the dialogue releases its lock.
        if (current == dashState)
            stateMachine.ChangeState(IsGroundDetected() ? (PlayerState)idleState : airState);
        // A network wall jump is an already-authorized impulse; local UI locks must not cancel its control window.
        if (current == moveState || current == airState || current == jumpState ||
            (current == wallJumpState && !GameSimulationClock.IsNetwork) || current == wallSlideState || current == dashState)
            SetVelocity(0, rb.linearVelocity.y);
    }

    public void CancelSwordAimForControlLock()
    {
        if (!CanUseLegacyActions) return;
        skill?.sword?.CancelAim();
        if (aimSwordState != null && stateMachine?.currentState == aimSwordState && rb != null)
            aimSwordState.CancelForControlLock();
    }

    public void AnimationTrigger()
    {
        if (!IsAuthoritative || GameSimulationClock.IsNetwork && stateMachine?.currentState == primaryAttackState) return;
        stateMachine?.currentState?.AnimationFinishTrigger();
    }

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
        combat?.PrimaryAttack?.InterruptCurrent();
        combat?.PrimaryAttackHits?.Reset();
        base.Die();
        stateMachine.ChangeState(deadState);
    }

    public void HoldNetworkAtZeroHealth()
    {
        if (!GameSimulationClock.IsNetwork || !IsAuthoritative) return;
        if (dashPending) ResolveNetworkDash(false);
        if (pendingJump != NetworkJumpKind.None) ResolveNetworkJump(false, false);
        if (!IsLocalInputOwner && combat?.PrimaryAttack?.HasPending == true)
        {
            resolvedPrimaryAttackRequestSeq = combat.PrimaryAttack.PendingRequestSeq;
            resolvedPrimaryAttackInstance = null;
            resolvedPrimaryAttackRejection = PrimaryAttackRejection.Dead;
        }
        InputData.Clear(); localJumpPress = 0; localDashPress = 0; localPrimaryAttackPress = 0; localFlaskPress = 0; localCrystalPress = 0;
        pendingJump = NetworkJumpKind.None; dashPending = false;
        combat?.PrimaryAttack?.InterruptCurrent(); combat?.PrimaryAttackHits?.Reset();
        if (rb != null && GetComponent<NetworkHitFeedback>()?.IsAuthorityKnockbackActive != true)
        { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0; }
        if (anim != null)
        {
            anim.SetBool("Attack", false); anim.SetBool("Move", false); anim.SetBool("Jump", false);
            anim.SetBool("Dash", false); anim.SetBool("WallSlide", false); anim.SetBool("Idle", true);
        }
    }

    protected override void SetupZeroKnockbackPlayer()
    {
        knockbackPower = new Vector2(0, 0);
    }
}
