using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Unity.VisualScripting.Antlr3.Runtime;
using UnityEngine;

public class ShockStrikeController : MonoBehaviour
{
    [SerializeField] private CharaterStats targetStats;
    [SerializeField] private float speed;
    private int damage;

    private Animator anim;
    private bool isTriggered = false;

    private void Start()
    {
        anim = GetComponentInChildren<Animator>();
    }

    public void SetUp(int _damage, CharaterStats _targetStats)
    {
        damage = _damage;
        targetStats = _targetStats;
    }

    private void Update()
    {
        if (!targetStats || isTriggered)
            return;

        transform.position = Vector2.MoveTowards(transform.position, targetStats.transform.position, speed * Time.deltaTime);
        transform.right = transform.position - targetStats.transform.position;

        if (Vector2.Distance(transform.position, targetStats.transform.position) < 0.1f)
        {
            anim.transform.localPosition = new Vector3(0, 0.5f);
            anim.transform.localRotation = Quaternion.identity;
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(3, 3);

            isTriggered = true;
            anim.SetTrigger("Hit");
            Invoke("DamageAndSelfDestroy", 0.2f);
        }
    }

    private void DamageAndSelfDestroy()
    {
        targetStats.ApplyShock(true);
        targetStats.TakeDamage(damage);
        Destroy(gameObject, 0.4f);
    }
}
