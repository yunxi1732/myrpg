using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class SwordSkillController : MonoBehaviour
{
    private Player player;
    private Animator anim;
    private Rigidbody2D rb;
    private CircleCollider2D cd;

    private bool canRotate = true;
    private bool isReturning;
    private float returnSpeed = 12f;
    private float freezeDuration = 0.7f;

    [Header("Pierce Info")]
    private int pierceAmount;

    [Header("Bounce Info")]
    private float bounceSpeed;
    private bool isBouncing;
    public int bounceAmount = 4;
    private List<Transform> enemyTarget;
    private int targetIndex;

    [Header("Spin Info")]
    private float maxTravelDistance;
    private float spinDuration;
    private float spinTimer;
    private bool isStopped;
    private bool isSpinning;
    private float hitTimer;
    private float hitCooldown;
    private float spinDirection;

    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody2D>();
        cd = GetComponent<CircleCollider2D>();
        if (rb == null)
            Debug.LogError("Rigidbody2D ¶ªÁË£¡");
    }

    private void DestroyMe() => Destroy(gameObject);

    private void FixedUpdate()
    {
        if (canRotate) transform.right = rb.velocity;
        if (isReturning)
        {
            transform.position = Vector2.MoveTowards(rb.transform.position, player.transform.position, returnSpeed * Time.fixedDeltaTime);
            if (Vector2.Distance(transform.position, player.transform.position) < 1) player.CatchTheSword();
        }
        BounceLogic();
        SpinLogic();
    }

    private void SpinLogic()
    {
        if (isSpinning)
        {
            if (Vector2.Distance(player.transform.position, transform.position) >= maxTravelDistance && !isStopped)
            {
                StopWhenSpinning();
            }
            if (isStopped)
            {
                spinTimer -= Time.fixedDeltaTime;
                transform.position = Vector2.MoveTowards(transform.position, new Vector2(transform.position.x + spinDirection, transform.position.y), 1.5f * Time.fixedDeltaTime);
                if (spinTimer < 0)
                {
                    isSpinning = false;
                    isReturning = true;
                }

                hitTimer -= Time.fixedDeltaTime;
                if (hitTimer < 0)
                {
                    hitTimer = hitCooldown;
                    Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, 1);
                    foreach (var hit in colliders)
                    {
                        Enemy enemy = hit.GetComponent<Enemy>();
                        if (enemy) SwordSkillDamage(enemy);
                    }
                }
            }
        }
    }

    private void StopWhenSpinning()
    {
        isStopped = true;
        rb.constraints = RigidbodyConstraints2D.FreezePosition;
        spinTimer = spinDuration;
    }

    private void BounceLogic()
    {
        if (isBouncing && enemyTarget.Count > 0)
        {
            transform.position = Vector2.MoveTowards(transform.position, enemyTarget[targetIndex].position, bounceSpeed * Time.fixedDeltaTime);
            if (Vector2.Distance(transform.position, enemyTarget[targetIndex].position) < 0.1f)
            {
                SwordSkillDamage(enemyTarget[targetIndex].GetComponent<Enemy>());
                targetIndex++;
                bounceAmount--;
                if (bounceAmount <= 0)
                {
                    isBouncing = false;
                    isReturning = true;
                }
                if (targetIndex >= enemyTarget.Count) targetIndex = 0;
            }
        }
    }

    public void SetUpSword(Vector2 _dir, float _gravityScale, Player _player, float _freezeDuration, float _returnSpeed)
    {
        player = _player;
        rb.velocity = _dir;
        rb.gravityScale = _gravityScale;
        freezeDuration = _freezeDuration;   
        returnSpeed = _returnSpeed;
        if (pierceAmount <= 0) anim.SetBool("Rotate", true);
        spinDirection = Mathf.Clamp(rb.velocity.x, -1, 1);
        Invoke("DestroyMe", 7);
    }

    public void SetUpBounce(bool _isBouncing, int _bounceAmount, float _bounceSpeed)
    {
        isBouncing = _isBouncing;
        bounceAmount = _bounceAmount;
        bounceSpeed = _bounceSpeed;
        enemyTarget = new List<Transform>();
    }

    public void SetUpPierce(int _pierceAmount)
    {
        pierceAmount = _pierceAmount;
    }

    public void SetUpSpin(bool _isSpinning, float _maxTravelDistance, float _spinDuration, float _hitCooldown)
    {
        isSpinning= _isSpinning;
        maxTravelDistance = _maxTravelDistance;
        spinDuration = _spinDuration;
        hitCooldown = _hitCooldown;
    }

    public void ReturnSword()
    {
        rb.constraints = RigidbodyConstraints2D.FreezeAll;
        transform.parent = null;
        isReturning = true;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (isReturning) return;

        Enemy enemy = collision.GetComponent<Enemy>();
        if (enemy)
        {
            SwordSkillDamage(enemy);
        }
        SetUpTargetForBounce(collision);

        StuckInto(collision);
    }

    private void SwordSkillDamage(Enemy enemy)
    {
        player.stats.DoDamage(enemy.GetComponent<CharaterStats>());
        enemy.StartCoroutine("FreezeTimeFor", freezeDuration);
    }

    private void SetUpTargetForBounce(Collider2D collision)
    {
        if (collision.GetComponent<Enemy>() != null)
        {
            if (isBouncing && enemyTarget.Count <= 0)
            {
                Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, 10);
                foreach (var hit in colliders)
                {
                    if (hit.GetComponent<Enemy>() != null) enemyTarget.Add(hit.transform);
                }

            }
        }
    }

    private void StuckInto(Collider2D collision)
    {
        if (isSpinning)
        {
            StopWhenSpinning();
            return;
        }
        if (pierceAmount > 0 && collision.GetComponent<Enemy>() != null)
        {
            pierceAmount--;
            return;
        }

        canRotate = false;
        cd.enabled = false;
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints2D.FreezeAll;
        if (isBouncing && enemyTarget.Count > 0) return;

        anim.SetBool("Rotate", false);
        transform.parent = collision.transform;
    }
}
