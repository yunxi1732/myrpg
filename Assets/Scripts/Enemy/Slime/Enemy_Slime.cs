using System;
using Unity.VisualScripting;
using UnityEngine;

public enum SlimeType { big, medium, small}
public class Enemy_slime : Enemy
{
    [Header("Slime spesific")]
    [SerializeField] private SlimeType slimeType;
    [SerializeField] private int slimesToCreate;
    [SerializeField] private GameObject slimePrefab;
    [SerializeField] private Vector2 minCreationVelocity;
    [SerializeField] private Vector2 maxCreationVelocity;

    public SlimeIdleState idleState { get; private set; }
    public SlimeMoveState moveState { get; private set; }
    public SlimeBattleState battleState { get; private set; }
    public SlimeAttackState attackState { get; private set; }
    public SlimeStunnedState stunnedState { get; private set; }
    public SlimeDeadState deadState { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        SetupDefaultFacingDir(-1);
        idleState = new SlimeIdleState(this, stateMachine, "Idle", this);
        moveState = new SlimeMoveState(this, stateMachine, "Move", this);
        battleState = new SlimeBattleState(this, stateMachine, "Move", this);
        attackState = new SlimeAttackState(this, stateMachine, "Attack", this);
        stunnedState = new SlimeStunnedState(this, stateMachine, "Stun", this);
        deadState = new SlimeDeadState(this, stateMachine, "Idle", this);
    }

    protected override void Start()
    {
        base.Start();
        if (IsAuthoritative) stateMachine.Initialize(idleState);
    }

    public override bool CanBeStunned()
    {
        if (!base.CanBeStunned()) return false;
        stateMachine.ChangeState(stunnedState);
        return true;
    }

    public override void Die()
    {
        if (!IsAuthoritative) return;
        base.Die();
        stateMachine.ChangeState(deadState);
        if (slimeType == SlimeType.small) return;
        CreateSlimes(slimesToCreate, slimePrefab);
    }

    private void CreateSlimes(int _amount, GameObject _prefab)
    {
        for(int i = 0; i < _amount; i++)
        {
            GameObject newSlime = Instantiate(_prefab, transform.position, Quaternion.identity);
            newSlime.GetComponent<Enemy_slime>().SetupSlime(facingDir);
        }
    }

    public void SetupSlime(int _facingDir)
    {
        if (_facingDir != facingDir) Flip();
        float vx = UnityEngine.Random.Range(minCreationVelocity.x, maxCreationVelocity.x);
        float vy = UnityEngine.Random.Range(minCreationVelocity.y, maxCreationVelocity.y);
        isKnocked = true;
        GetComponent<Rigidbody2D>().linearVelocity = new Vector2(vx * -facingDir, vy);
        Invoke("CancelKnockback", 1.5f);
    }

    private void CancelKnockback() => isKnocked = false;
}
