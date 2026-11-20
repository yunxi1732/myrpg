using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CapsuleCollider2D))]
[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(EntityFX))]
[RequireComponent(typeof(ItemDrop))]
public class Enemy : Entity
{
    private long nextNetworkAttackId;
    private ulong counterOpenTick, counterCloseTick;
    public Rpg.Networking.Protocol.MonsterAttackDto NetworkAttack { get; private set; }
    public Rpg.Networking.MonsterAttackHitDetection NetworkHits { get; internal set; }
    public virtual string NetworkAttackClip => null;
    public virtual string NetworkMoveClip => null;
    public virtual string NetworkIdleClip => null;
    public virtual string NetworkStunClip => null;
    public virtual string NetworkStunParameter => null;
    public virtual EnemyState NetworkIdleState => null;
    public virtual EnemyState NetworkMoveState => null;
    public virtual EnemyState NetworkBattleState => null;
    public virtual EnemyState NetworkAttackState => null;
    public virtual EnemyState NetworkStunnedState => null;
    public virtual EnemyState NetworkDeadState => null;
    public virtual bool NetworkMovementReady => true;
    public virtual Transform NetworkTarget => null;
    public virtual void ForgetNetworkPlayer(int entityId) { }
    public virtual Rpg.Networking.Protocol.MonsterAiState NetworkAiState =>
        stateMachine?.currentState == null || stats != null && stats.IsRuntimeInitialized && stats.currentHealth <= 0 ?
        Rpg.Networking.Protocol.MonsterAiState.Idle : stateMachine.currentState == NetworkAttackState ?
        Rpg.Networking.Protocol.MonsterAiState.Attack : NetworkStunnedState != null && stateMachine.currentState == NetworkStunnedState ?
        Rpg.Networking.Protocol.MonsterAiState.Stunned : stateMachine.currentState == NetworkBattleState ?
        Rpg.Networking.Protocol.MonsterAiState.Chase : stateMachine.currentState == NetworkMoveState ?
        Rpg.Networking.Protocol.MonsterAiState.Patrol : Rpg.Networking.Protocol.MonsterAiState.Idle;

    public static bool TryNetworkAttackTiming(Animator animator, string clipName, out Rpg.Networking.PrimaryAttackTiming timing)
    {
        timing = null;
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name != clipName) continue;
            float finish = clip.length;
            var hits = new List<float>();
            foreach (var marker in clip.events)
            {
                if (marker.functionName == "AnimationTrigger") finish = Mathf.Min(finish, marker.time);
                if (marker.functionName == "AttackTrigger") hits.Add(marker.time);
            }
            return Rpg.Networking.PrimaryAttackTiming.TryCreate(0, finish, clip.frameRate, GameSimulationClock.FixedDeltaTime, hits.ToArray(), out timing);
        }
        return false;
    }

    public static float AttackClipDuration(Animator animator, string clipName, out float finishTime)
    {
        float length = 1.2f;
        finishTime = length;
        if (animator == null || animator.runtimeAnimatorController == null) return length;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name != clipName) continue;
            length = clip.length; finishTime = length;
            foreach (var marker in clip.events)
                if (marker.functionName == "AnimationTrigger") { finishTime = marker.time; break; }
            return length;
        }
        return length;
    }

    public void BeginNetworkAttack()
    {
        if (!IsAuthoritative || !NetworkMovementOnly || string.IsNullOrEmpty(NetworkAttackClip)) return;
        AttackClipDuration(anim, NetworkAttackClip, out float finishTime);
        if (NetworkHits != null)
        {
            if (TryNetworkAttackTiming(anim, NetworkAttackClip, out var timing)) NetworkHits.Begin(GameSimulationClock.AuthorityTick, facingDir, timing);
            else { NetworkHits.Reset(); Debug.LogError($"[MonsterHit] Invalid {NetworkAttackClip} hit markers; damage window is closed."); }
        }
        NetworkAttack = new Rpg.Networking.Protocol.MonsterAttackDto { actionId = ++nextNetworkAttackId, actionNumber = 1,
            startTick = GameSimulationClock.AuthorityTick, facingDir = facingDir,
            durationTicks = Mathf.Max(1, Mathf.CeilToInt(finishTime / GameSimulationClock.FixedDeltaTime)) };
        counterOpenTick = counterCloseTick = 0;
        foreach (var clip in anim.runtimeAnimatorController.animationClips)
            if (clip.name == NetworkAttackClip)
                foreach (var marker in clip.events)
                {
                    if (marker.functionName == "OpenCounterWindow") counterOpenTick = NetworkAttack.startTick + (ulong)Mathf.CeilToInt(marker.time / GameSimulationClock.FixedDeltaTime);
                    if (marker.functionName == "CloseCounterWindow") counterCloseTick = NetworkAttack.startTick + (ulong)Mathf.CeilToInt(marker.time / GameSimulationClock.FixedDeltaTime);
                }
        anim.fireEvents = false;
        anim.speed = 1;
        anim.Play("Base Layer." + NetworkAttackClip, 0, 0);
    }

    public void AdvanceNetworkCounterWindow(ulong tick)
    {
        bool open = counterOpenTick > 0 && counterCloseTick >= counterOpenTick &&
            NetworkAiState == Rpg.Networking.Protocol.MonsterAiState.Attack && tick >= counterOpenTick && tick <= counterCloseTick;
        if (open && !CounterWindowOpen) OpenCounterAttackWindow();
        else if (!open && CounterWindowOpen) CloseCounterAttackWindow();
    }
    // Adapted network AI/actions only; legacy damage, counter windows, stats and drops stay gated.
    public bool NetworkMovementOnly { get; private set; }
    private bool runtimeReady;
    private System.Func<IEnumerable<Player>> networkPlayers;
    private readonly List<RaycastHit2D> targetRayHits = new List<RaycastHit2D>();
    private bool selectingLivingTarget, targetRayReady;
    public void ConfigureNetworkMovement(System.Func<IEnumerable<Player>> players)
    { networkPlayers = players; NetworkMovementOnly = true; }

    private IEnumerable<Player> TargetCandidates => NetworkMovementOnly ? networkPlayers() : Player.ActivePlayers;
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

    public virtual bool DetectsLivingTarget(Transform target)
    {
        if (!IsValidLivingTarget(target)) return false;
        if (((Vector2)transform.position - (Vector2)target.position).sqrMagnitude < 4f) return true;
        // Retain the original forward ray/range/mask, but consider all hits: a corpse or physics hit ordering cannot pick the target.
        if (!selectingLivingTarget || !targetRayReady)
        {
            var filter = new ContactFilter2D { useLayerMask = true, layerMask = whatIsPlayer,
                useTriggers = Physics2D.queriesHitTriggers };
            targetRayHits.Clear();
            Physics2D.Raycast(wallCheck.position, Vector2.right * facingDir, filter, targetRayHits, 50);
            targetRayReady = true;
        }
        foreach (var hit in targetRayHits)
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
        // Candidate players share the same ray during this selection pass.
        selectingLivingTarget = detectedOnly;
        targetRayReady = false;
        try
        {
            foreach (Player candidate in TargetCandidates)
            {
                if (!IsLivingPlayer(candidate) || detectedOnly && !DetectsLivingTarget(candidate.transform)) continue;
                float current = ((Vector2)candidate.transform.position - (Vector2)transform.position).sqrMagnitude;
                if (float.IsNaN(current) || float.IsInfinity(current)) continue;
                int id = TargetOrderId(candidate);
                if (current < distance || current == distance && id < orderId)
                { distance = current; orderId = id; nearest = candidate.transform; }
            }
        }
        finally { selectingLivingTarget = false; }
        return nearest;
    }
    [SerializeField] protected LayerMask whatIsPlayer;

    [Header("Move Info")]
    public float moveSpeed;
    public float idleTime;
    public float battleTime;
    private float defaultMoveSpeed;

    [Header("Attack Info")]
    public float agroDistance = 2;
    public float attackDistance;
    public float attackCooldown;
    [HideInInspector] public float lastTimeAttacked;

    [Header("Stunned Info")]
    public float stunnedDuration;
    public Vector2 stunnedDir;
    protected bool canBeStunned;
    public bool CounterWindowOpen => canBeStunned;
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
        if (NetworkMovementOnly && IsAuthoritative &&
            GetComponent<Rpg.Networking.NetworkHitFeedback>()?.AdvanceAuthorityKnockback(GameSimulationClock.AuthorityTick) == true)
        { NetworkHits?.Step(GameSimulationClock.AuthorityTick); return; }
        if (NetworkMovementOnly && IsAuthoritative && stats != null && stats.IsRuntimeInitialized && stats.currentHealth <= 0)
        {
            NetworkHits?.Reset();
            if (stateMachine.currentState == NetworkDeadState) stateMachine.currentState.Update();
            else HoldNetworkAtZeroHealth();
            return;
        }
        if (NetworkMovementOnly && IsAuthoritative && runtimeReady && NetworkMovementReady &&
            Rpg.Networking.EntityRuntimePermissions.Identity(this)?.MovementHeld != true)
        {
            stateMachine.currentState?.Update();
            AdvanceNetworkCounterWindow(GameSimulationClock.AuthorityTick);
            NetworkHits?.Step(GameSimulationClock.AuthorityTick);
        }
    }

    public void HoldNetworkAtZeroHealth()
    {
        if (!NetworkMovementOnly || !IsAuthoritative) return;
        if (rb != null) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0; }
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
    public void ShowCounterWindow(bool open) => counterImage.SetActive(open);
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
