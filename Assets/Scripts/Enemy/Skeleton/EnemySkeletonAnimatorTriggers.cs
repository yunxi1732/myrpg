using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemySkeletonAnimatorTriggers : MonoBehaviour
{
    private EnemySkeleton enemy => GetComponentInParent<EnemySkeleton>();

    private void AnimationTrigger()
    {
        enemy.AnimationFinishTrigger();
    }

    private void AttackTrigger()
    {
        Collider2D[] collider = Physics2D.OverlapCircleAll(enemy.attackCheck.position, enemy.attackCheckRadius);
        foreach (var hit in collider)
        {
            Player player = hit.GetComponent<Player>();
            if (player != null) player.Damage();
        }
    }

    private void OpenCounterWindow() =>enemy.OpenCounterAttackWindow();
    private void CloseCounterWindow() =>enemy.CloseCounterAttackWindow();
}
