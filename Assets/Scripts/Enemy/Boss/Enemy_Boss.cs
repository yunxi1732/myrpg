using System;
using Unity.VisualScripting;
using UnityEngine;

public class Enemy_Boss : Enemy
{
    public override string NetworkAttackClip => "BossAttack";
    public override string NetworkMoveClip => "BossMove";
    public override string NetworkIdleClip => "BossIdle";
    public override EnemyState NetworkIdleState => idleState;
    public override EnemyState NetworkMoveState => moveState;
    public override EnemyState NetworkBattleState => battleState;
    public override EnemyState NetworkAttackState => attackState;
    public override EnemyState NetworkDeadState => deadState;
    public override Transform NetworkTarget => battleState?.Target;
    public override void ForgetNetworkPlayer(int entityId) => battleState?.ForgetRoomPlayer(entityId);
    public override bool NetworkMovementReady => bossFightBegin;
    public override Rpg.Networking.Protocol.MonsterAiState NetworkAiState =>
        stateMachine?.currentState == teleportState ? Rpg.Networking.Protocol.MonsterAiState.Teleport :
        stateMachine?.currentState == castState ? Rpg.Networking.Protocol.MonsterAiState.Cast : base.NetworkAiState;
    private void OnDisable() => NetworkHits?.Reset();
    public bool bossFightBegin { get; private set; }
    [Header("Spell Cast details")]
    [SerializeField] private GameObject spellPrefab;
    public int amountOfSpells;
    public float spellCooldown;
    private float lastTimeCast;
    private long nextNetworkSpellId;
    public Rpg.Networking.Protocol.MonsterSpellDto NetworkSpell { get; private set; }
    public event Action<Enemy_Boss, Player> NetworkSpellHit;
    [SerializeField] private float spellStateCooldown;

    [Header("Teleport details")]
    [SerializeField] private BoxCollider2D arena;
    [SerializeField] private Vector2 surroundingCheckSize;
    public float chanceToTeleport;
    public float defaultChanceToTeleport = 25;

    public BossIdleState idleState { get; private set; }
    public BossMoveState moveState { get; private set; }
    public BossBattleState battleState { get; private set; }
    public BossAttackState attackState { get; private set; }
    public BossDeadState deadState { get; private set; }
    public BossCastState castState { get; private set; }
    public BossTeleportState teleportState { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        SetupDefaultFacingDir(-1);
        idleState = new BossIdleState(this, stateMachine, "Idle", this);
        moveState = new BossMoveState(this, stateMachine, "Move", this);
        battleState = new BossBattleState(this, stateMachine, "Move");
        attackState = new BossAttackState(this, stateMachine, "Attack", this);
        deadState = new BossDeadState(this, stateMachine, "Idle", this);
        castState = new BossCastState(this, stateMachine, "SpellCast", this);
        teleportState = new BossTeleportState(this, stateMachine, "Teleport", this);
    }

    protected override void Start()
    {
        base.Start();
        if (IsAuthoritative) stateMachine.Initialize(idleState);
    }

    protected override void Update()
    {
        if (!IsAuthoritative) return;
        if (!bossFightBegin && arena != null)
        {
            foreach (Player candidate in Player.ActivePlayers)
                if (candidate != null && IsValidLivingTarget(candidate.transform) && arena.OverlapPoint(candidate.transform.position))
                { bossFightBegin = true; break; }
        }
        if (bossFightBegin) base.Update();
    }

    public override bool DetectsLivingTarget(Transform target)
    {
        if (base.DetectsLivingTarget(target)) return true;
        if (!IsValidLivingTarget(target) || cd == null) return false;
        Collider2D playerCollider = target.GetComponent<Collider2D>();
        return playerCollider != null && cd.Distance(playerCollider).distance <= agroDistance;
    }

    public bool IsTargetInAttackRange(Transform target)
    {
        if (target == null || cd == null) return false;
        Collider2D playerCollider = target.GetComponent<Collider2D>();
        return playerCollider != null && cd.Distance(playerCollider).distance <= attackDistance;
    }

    public bool IsGroundAhead(int direction)
    {
        if (cd == null || direction == 0) return false;
        Bounds bounds = cd.bounds;
        Vector2 origin = new Vector2(
            direction > 0 ? bounds.max.x + 0.1f : bounds.min.x - 0.1f,
            bounds.min.y + 0.4f);
        return Physics2D.Raycast(origin, Vector2.down, 0.8f, whatIsGround);
    }

    public override bool CanBeStunned() => false;

    public override void Die()
    {
        if (!IsAuthoritative) return;
        base.Die();
        stateMachine.ChangeState(deadState);
    }

    public void CastSpell()
    {
        Transform target = NetworkMovementOnly ? SelectLivingTarget(false) : PlayerManager.instance.player?.transform;
        if (target == null) return;
        Player player = target.GetComponent<Player>();
        float xOffset = 0;
        if (player.rb.linearVelocity.x != 0) xOffset = player.facingDir * 3;
        Vector3 spellPos = new Vector3(target.position.x + xOffset, target.position.y + 1);
        GameObject newSpell = Instantiate(spellPrefab, spellPos, Quaternion.identity);
        var controller = newSpell.GetComponent<BossSpell_Controller>();
        if (NetworkMovementOnly)
        {
            NetworkSpell = new Rpg.Networking.Protocol.MonsterSpellDto { actionId = ++nextNetworkSpellId,
                startTick = GameSimulationClock.AuthorityTick, position = new[] { spellPos.x, spellPos.y } };
            controller.SetupNetworkSpell(victim => NetworkSpellHit?.Invoke(this, victim));
        }
        else controller.SetupSpell(stats);
    }

    public void SpawnNetworkSpellVisual(Vector2 position)
    {
        GameObject spell = Instantiate(spellPrefab, position, Quaternion.identity);
        spell.GetComponent<BossSpell_Controller>().SetupVisualSpell();
    }

    public void FindPosition()
    {
        if (arena == null || cd == null) return;
        Bounds area = arena.bounds;
        float feetOffset = transform.position.y - cd.bounds.min.y;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float x = UnityEngine.Random.Range(area.min.x + 3, area.max.x - 3);
            float y = UnityEngine.Random.Range(area.min.y + 3, area.max.y - 3);
            RaycastHit2D ground = Physics2D.Raycast(new Vector2(x, y), Vector2.down, area.size.y, whatIsGround);
            if (!ground) continue;

            Vector3 candidate = new Vector3(x, ground.point.y + feetOffset + 0.05f, transform.position.z);
            if (candidate.y + cd.bounds.extents.y > area.max.y) continue;
            transform.position = candidate;
            return;
        }
    }

    private RaycastHit2D GroundBelow() => Physics2D.Raycast(transform.position, Vector2.down, whatIsGround);

    protected override void OnDrawGizmos()
    {
        base.OnDrawGizmos();
        Gizmos.DrawLine(transform.position, new Vector3(transform.position.x, transform.position.y - GroundBelow().distance));
        Gizmos.DrawWireCube(transform.position, surroundingCheckSize);
    }

    public bool CanTeleport()
    {
        if (UnityEngine.Random.Range(0, 100) <= chanceToTeleport)
        {
            chanceToTeleport = defaultChanceToTeleport;
            return true;
        }
        return false;
    }

    public bool CanSpellCast()
    {
        if(Time.time > lastTimeCast + spellStateCooldown)
        {
            lastTimeCast = Time.time;
            return true;
        }
        return false;
    }
}
