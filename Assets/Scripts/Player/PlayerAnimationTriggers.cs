using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAnimationTriggers : MonoBehaviour
{
    private Player player => GetComponentInParent<Player>();

    private void AnimationTrigger()
    {
        player.AnimationTrigger();
    }
    
    private void AttackTrigger()
    {
        if (player == null || !player.IsAuthoritative) return;
        Collider2D[] colliders = Physics2D.OverlapCircleAll(player.attackCheck.position, player.attackCheckRadius);
        foreach (var hit in  colliders)
        {
            if (hit.GetComponent<Enemy>() != null)
            {
                EnemyStats _target = hit.GetComponent<EnemyStats>();

                if (_target != null)
                    player.stats.DoDamage(_target);

                ItemData_Equipment weaponData = player.CanUseLegacyActions ? Inventory.instance.GetEquipment(EquipmentType.Weapon) : null;
                if (weaponData != null)
                    weaponData.Effect(_target.transform);
            }
        }
    }

    private void ThrowSword()
    {
        if (player == null || !player.CanUseLegacyActions) return;
        SkillManager.instance.sword.TryCreateSwordFromInput();
    }
}
