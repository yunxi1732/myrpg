using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyStat : CharaterStats
{
    public Enemy enemy;

    protected override void Start()
    {
        base.Start();
    }

    public override void TakeDamage(int _damage)
    {
        base.TakeDamage(_damage);
    }

    protected override void Die()
    {
        base.Die();
        enemy.Die();
    }
}
