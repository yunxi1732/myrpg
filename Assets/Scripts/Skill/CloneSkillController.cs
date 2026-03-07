using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CloneSkillController : MonoBehaviour
{
    private float cloneTimer;
    private SpriteRenderer sr;
    private Animator anim;
    [SerializeField] private float cloneLosingSpeed;
    private Transform closestEnemy;

    [SerializeField] private Transform attackCheck;
    [SerializeField] private float attackCheckRadius = 0.85f;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        anim = GetComponent<Animator>();
    }

    private void Update()
    {
        cloneTimer -= Time.deltaTime;
        if (cloneTimer < 0) sr.color = new Color (1, 1, 1, sr.color.a - cloneLosingSpeed * Time.deltaTime);
        if (sr.color.a < 0) Destroy(gameObject);
    }

    public void SetupClone(Transform _newPosition, float _cloneDuration, bool _canAttack)
    {
        if (_canAttack) anim.SetInteger("AttackNumber", Random.Range(1, 4));

        transform.position = _newPosition.position;
        cloneTimer = _cloneDuration;

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
            if (enemy != null) enemy.Damage();
        }
    }

    private void FaceClosestTarget()
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, 25);
        float closestDistance = Mathf.Infinity;
        foreach (var hit in colliders)
        {
            if (hit.GetComponent<Enemy>() != null)
            {
                float distanceToEnemy = Vector2.Distance(transform.position, hit.transform.position);
                if (distanceToEnemy < closestDistance)
                {
                    closestDistance = distanceToEnemy;
                    closestEnemy = hit.transform;
                }
            }
        }

        if (closestEnemy != null)
        {
            if (transform.position.x > closestEnemy.position.x) transform.Rotate(0, 180, 0);
        }
    }
}
