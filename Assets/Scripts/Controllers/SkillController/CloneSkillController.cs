using UnityEngine;

public class CloneSkillController : MonoBehaviour
{
    internal Rpg.Networking.RoomCloneNetwork Room;
    internal int AttackNumber { get; private set; }
    private Player player;
    private float cloneTimer;
    private SpriteRenderer sr;
    private Animator anim;
    [SerializeField] private float cloneLosingSpeed;
    private Transform closestEnemy;

    private float attackMultiplier;
    private bool canAttack;
    [SerializeField] private Transform attackCheck;
    [SerializeField] private float attackCheckRadius = 0.85f;
    [SerializeField] private bool canDuplicateClone;
    private float chanceToDuplicate;

    private int facingDir = -1;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        anim = GetComponent<Animator>();
    }

    private void Update()
    {
        cloneTimer -= GameSimulationClock.DeltaTime;
        if (cloneTimer < 0) sr.color = new Color(1, 1, 1, sr.color.a - cloneLosingSpeed * GameSimulationClock.DeltaTime);
        if (sr.color.a < 0) Destroy(gameObject);
    }

    public void SetupClone(Transform _newPosition, float _cloneDuration, bool _canAttack, Vector3 _offset, Transform _closestEnemy, bool _canDuplicateClone, float _chanceToDuplicate, Player _player, float _attackMultiplier)
    {
        canAttack = _canAttack;
        if (_canAttack) { AttackNumber = Random.Range(1, 4); anim.SetInteger("AttackNumber", AttackNumber); }

        attackMultiplier = _attackMultiplier;
        transform.position = _newPosition.position + _offset;
        cloneTimer = _cloneDuration;
        closestEnemy = _closestEnemy;
        canDuplicateClone = _canDuplicateClone;
        chanceToDuplicate = _chanceToDuplicate;
        player = _player;

        FaceClosestTarget();
    }

    private void AnimationTrigger()
    {
        cloneTimer = -1f;
    }

    private void AttackTrigger()
    {
        if (!canAttack) return;
        if (Room != null)
        {
            foreach (var hit in Physics2D.OverlapCircleAll(attackCheck.position, attackCheckRadius))
                if (hit.GetComponent<Enemy>() is Enemy enemy) Room.Hit(this, enemy);
            return;
        }
        Collider2D[] colliders = Physics2D.OverlapCircleAll(attackCheck.position, attackCheckRadius);
        foreach (var hit in colliders)
        {
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                //player.stats.DoDamage(hit.GetComponent<CharaterStats>());

                hit.GetComponent<Entity>().SetupKonckbackDir(transform);

                PlayerStats playerStats = player.GetComponent<PlayerStats>();
                EnemyStats enemyStats = enemy.GetComponent<EnemyStats>();
                playerStats.CloneDoDamage(enemyStats, attackMultiplier);

                if (player.skill.clone.canApplyOnHitEffect)
                {
                    ItemData_Equipment weaponData = Inventory.instance.GetEquipment(EquipmentType.Weapon);

                    if (weaponData != null)
                        weaponData.Effect(hit.transform);
                }

                if (canDuplicateClone)
                {
                    if (Random.Range(0, 100) < chanceToDuplicate)
                    {
                        SkillManager.instance.clone.CreateClone(hit.transform, new Vector3(1.5f * facingDir, 0));
                    }
                }
            }
        }
    }

    private void FaceClosestTarget()
    {
        if (closestEnemy != null)
        {
            if (transform.position.x > closestEnemy.position.x)
            {
                facingDir = -1;
                transform.Rotate(0, 180, 0);
            }
        }
    }
}
