using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CrystalSkillController : MonoBehaviour
{
    internal Rpg.Networking.RoomCrystalNetwork Room;
    private Player player;
    private Animator anim => GetComponent<Animator>();
    private CircleCollider2D cd => GetComponent<CircleCollider2D>();
    private float crystalExistTimer;

    private bool canMove;
    private bool canExplode;
    private float moveSpeed;

    private bool canGrow;
    private bool finished;
    private float growSpeed = 3.5f;
    private Transform closestTarget;
    [SerializeField] private LayerMask whatIsEnemy;

    public void SetUpCrystal(float _crystalDuration, bool _canExplode, bool _canMove, float _moveSpeed, Transform _closestTarget, Player _player)
    {
        crystalExistTimer = _crystalDuration;
        canExplode = _canExplode;
        canMove = _canMove;
        moveSpeed = _moveSpeed;
        closestTarget = _closestTarget;
        player = _player;
        finished = false;
    }

    public void ChooseRanodmEnemy()
    {
        float radius = SkillManager.instance.blackhole.GetBlackholeRadius();
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, radius, whatIsEnemy);
        if (colliders.Length > 0) closestTarget = colliders[Random.Range(0, colliders.Length)].transform;
    }

    private void Update()
    {
        crystalExistTimer -= GameSimulationClock.DeltaTime;
        if (crystalExistTimer < 0 && !finished) FinishCrystal();
        if (canMove)
        {
            if (closestTarget == null)
                return;

            transform.position = Vector2.MoveTowards(transform.position, closestTarget.position, moveSpeed * GameSimulationClock.DeltaTime);
            if (Vector2.Distance(transform.position, closestTarget.position) < 1)
            {
                FinishCrystal();
                canMove = false;
            }
        }
        if (canGrow) transform.localScale = Vector2.Lerp(transform.localScale, new Vector2(3, 3), growSpeed * GameSimulationClock.DeltaTime);
    }

    private void AnimationExplodeEvent()
    {
        if (Room != null) { if (Room.IsAuthoritative) Room.Explode(this, cd.radius); return; }
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, cd.radius);
        foreach (var hit in colliders)
        {
            if (hit.GetComponent<Enemy>() != null)
            {
                hit.GetComponent<Entity>().SetupKonckbackDir(transform);
                player.stats.DoMagicalDamage(hit.GetComponent<CharaterStats>());

                ItemData_Equipment equipedAmulet = Inventory.instance.GetEquipment(EquipmentType.Amulet);

                if (equipedAmulet != null)
                    equipedAmulet.Effect(hit.transform);
            }
        }
    }

    public void FinishCrystal()
    {
        if (finished) return;
        finished = true;
        if (canExplode)
        {
            canGrow = true;
            anim.SetBool("Explode", true);
        }
        else SelfDestroy();
        Room?.Publish();
    }

    public void SelfDestroy() { if (Room == null || Room.IsAuthoritative) Destroy(gameObject); }
}
