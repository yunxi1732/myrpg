using UnityEngine;

public class CloneSkillController : MonoBehaviour
{
    private Player player;
    private float cloneTimer;
    private SpriteRenderer sr;
    private Animator anim;
    [SerializeField] private float cloneLosingSpeed;
    private Transform closestEnemy;

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
        cloneTimer -= Time.deltaTime;
        if (cloneTimer < 0) sr.color = new Color(1, 1, 1, sr.color.a - cloneLosingSpeed * Time.deltaTime);
        if (sr.color.a < 0) Destroy(gameObject);
    }

    public void SetupClone(Transform _newPosition, float _cloneDuration, bool _canAttack, Vector3 _offset, Transform _closestEnemy, bool _canDuplicateClone, float _chanceToDuplicate, Player _player)
    {
        if (_canAttack) anim.SetInteger("AttackNumber", Random.Range(1, 4));

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
        Collider2D[] colliders = Physics2D.OverlapCircleAll(attackCheck.position, attackCheckRadius);
        foreach (var hit in colliders)
        {
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                player.stats.DoDamage(hit.GetComponent<CharaterStats>());
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
