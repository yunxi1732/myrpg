using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Enemy : Entity
{
    // Adapted network AI/actions only; legacy damage, counter windows, stats and drops stay gated.
    public bool NetworkMovementOnly { get; private set; }
    private bool runtimeReady;
    private System.Func<IEnumerable<Player>> networkPlayers;
    public void ConfigureNetworkMovement(System.Func<IEnumerable<Player>> players)
    { networkPlayers = players; NetworkMovementOnly = true; }

    private IEnumerable<Player> TargetCandidates => networkPlayers != null ? networkPlayers() : Player.ActivePlayers;
    private static bool IsLivingPlayer(Player candidate) => candidate != null && candidate.isActiveAndEnabled && candidate.IsAuthoritative &&
        candidate.stats != null && !candidate.stats.isDead && candidate.stats.currentHealth > 0;

    private static int TargetOrderId(Player candidate)
    {
        var identity = Rpg.Networking.EntityRuntimePermissions.Identity(candidate);
        return identity != null && identity.PlayerId > 0 ? identity.PlayerId : candidate.GetInstanceID();
    }

    public bool IsValidLivingTarget(Transform target)
    {
        if (!IsAuthoritative || target == null) return false;
        foreach (Player candidate in TargetCandidates)
            if (IsLivingPlayer(candidate) && candidate.transform == target) return true;
        return false;
    }

    public bool DetectsLivingTarget(Transform target)
    {
        if (!IsValidLivingTarget(target)) return false;
        if (Vector2.Distance(transform.position, target.position) < 2) return true;
        // Retain the original forward ray/range/mask, but consider all hits: a corpse or physics hit ordering cannot pick the target.
        foreach (var hit in Physics2D.RaycastAll(wallCheck.position, Vector2.right * facingDir, 50, whatIsPlayer))
        {
            var player = hit.collider != null ? hit.collider.GetComponentInParent<Player>() : null;
            if (IsLivingPlayer(player) && player.transform == target) return true;
        }
        return false;
    }

    public Transform SelectLivingTarget(bool detectedOnly)
    {
        Transform nearest = null; float distance = float.PositiveInfinity; int orderId = int.MaxValue;
        if (!IsAuthoritative) return null;
        foreach (Player candidate in TargetCandidates)
        {
            if (!IsLivingPlayer(candidate) || detectedOnly && !DetectsLivingTarget(candidate.transform)) continue;
            float current = ((Vector2)candidate.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (float.IsNaN(current) || float.IsInfinity(current)) continue;
            int id = TargetOrderId(candidate);
            if (current < distance || current == distance && id < orderId)
            { distance = current; orderId = id; nearest = candidate.transform; }
        }
        return nearest;
    }
    [SerializeField] protected LayerMask whatIsPlayer;

    [Header("Move Info")]
    public float moveSpeed;
    public float idleTime;
    public float battleTime;
    private float defaultMoveSpeed;

    [Header("Attack Info")]
    public float attackDistance;
    public float attackCooldown;
    [HideInInspector] public float lastTimeAttacked;

    [Header("Stunned Info")]
    public float stunnedDuration;
    public Vector2 stunnedDir;
    protected bool canBeStunned;
    [SerializeField] protected GameObject counterImage;

    public EnemyStateMachine stateMachine { get; private set; }
    public EntityFX fx { get; private set; }
    public string lastAnimBoolName {  get; private set; }

    protected override void Awake()
    {
        base.Awake();
        stateMachine = new EnemyStateMachine();
        defaultMoveSpeed = moveSpeed;
    }

    protected override void Start()
    {
        base.Start();
        fx = GetComponentInChildren<EntityFX>();
        runtimeReady = true;
    }

    protected override void Update()
    {
        if (!IsAuthoritative || NetworkMovementOnly) return;
        base.Update();
        stateMachine.currentState?.Update();
    }

    private void FixedUpdate()
    {
        if (NetworkMovementOnly && IsAuthoritative && stats != null && stats.IsRuntimeInitialized && stats.currentHealth <= 0)
        { HoldNetworkAtZeroHealth(); return; }
        if (NetworkMovementOnly && IsAuthoritative && runtimeReady &&
            Rpg.Networking.EntityRuntimePermissions.Identity(this)?.MovementHeld != true)
            stateMachine.currentState?.Update();
    }

    public void HoldNetworkAtZeroHealth()
    {
        if (!NetworkMovementOnly || !IsAuthoritative) return;
        if (rb != null) { rb.velocity = Vector2.zero; rb.angularVelocity = 0; }
        if (anim != null) { anim.SetBool("Attack", false); anim.SetBool("Move", false); anim.SetBool("Idle", true); }
    }

    public virtual void AssignLastAnimName(string _animBoolName) => lastAnimBoolName = _animBoolName;

    public override void SlowEntityBy(float _slowPercentage, float _slowDuration)
    {
        if (!IsAuthoritative) return;
        moveSpeed *= (1 -  _slowPercentage);
        anim.speed *= (1 - _slowPercentage);

        GameSimulationClock.Schedule(this, ReturnDefaultSpeed, nameof(ReturnDefaultSpeed), _slowDuration);
    }

    protected override void ReturnDefaultSpeed()
    {
        if (!IsAuthoritative) return;
        base.ReturnDefaultSpeed();
        moveSpeed = defaultMoveSpeed;
    }

    public virtual void FreezeTime(bool _timeFrozen)
    {
        if (!IsAuthoritative) return;
        if (_timeFrozen)
        {
            moveSpeed = 0;
            anim.speed = 0;
        }
        else
        {
            anim.speed = 1;
            moveSpeed = defaultMoveSpeed;
        }
    }

    public virtual void FreezeTimeFor(float _duration) { if (IsAuthoritative) StartCoroutine(FreezeTimeCoroutine(_duration)); }

    protected virtual IEnumerator FreezeTimeCoroutine(float _seconds)
    {
        FreezeTime(true);
        yield return GameSimulationClock.Wait(_seconds);
        FreezeTime(false);
    }

    #region Counter Attack Window
    public virtual void OpenCounterAttackWindow()
    {
        if (!IsAuthoritative) return;
        canBeStunned = true;
        counterImage.SetActive(true);
    }

    public virtual void CloseCounterAttackWindow()
    {
        if (!IsAuthoritative) return;
        canBeStunned = false;
        counterImage.SetActive(false);
    }
    #endregion
    public virtual bool CanBeStunned()
    {
        if (!IsAuthoritative) return false;
        if (canBeStunned)
        {
            CloseCounterAttackWindow();
            return true;
        }
        return false;
    }
    public virtual void AnimationFinishTrigger() { if (IsAuthoritative) stateMachine?.currentState?.AnimationFinishTrigger(); }

    public virtual RaycastHit2D isPlayerDetected() => Physics2D.Raycast(wallCheck.position, Vector2.right * facingDir, 50, whatIsPlayer);

    protected override void OnDrawGizmos()
    {
        base.OnDrawGizmos();
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, new Vector3(transform.position.x + attackDistance * facingDir, transform.position.y));
    }
}
