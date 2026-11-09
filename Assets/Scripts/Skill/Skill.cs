using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class Skill : MonoBehaviour
{
    public float cooldown;
    public float cooldownTimer;

    protected Player player;
    public PlayerCombatContext Combat { get; private set; }

    public void BindOwner(PlayerCombatContext context)
    {
        if (context == null || Combat != null && !ReferenceEquals(Combat, context))
            throw new System.InvalidOperationException("A Skill cannot change combat owner.");
        if (!context.Owner.CanUseLegacyActions)
            throw new System.InvalidOperationException("Legacy skills are not network adapted yet.");
        context.Skills.Bind(this);
        Combat = context;
        player = context.Owner;
    }

    protected virtual void Start()
    {
        var manager = GetComponent<SkillManager>();
        manager?.BindStandaloneOwner();
        if (Combat == null) throw new System.InvalidOperationException("Bind a combat owner before starting a skill.");

        CheckUnlock();
    }

    protected virtual void Update()
    {
        cooldownTimer -= GameSimulationClock.DeltaTime;
    }

    public virtual void CheckUnlock()
    {

    }

    public virtual bool CanUseSkill()
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyActions(player)) return false;
        if (cooldownTimer < 0)
        {
            UseSkill();
            cooldownTimer = cooldown;
            return true;
        }
        player.fx.CreatePopUpText("Cooldown");
        return false;
    }

    public virtual void UseSkill()
    {

    }

    protected virtual Transform FindClosestEnemy(Transform _checkTransform)
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(_checkTransform.position, 25);
        float closestDistance = Mathf.Infinity;
        Transform closestEnemy = null;
        foreach (var hit in colliders)
        {
            if (hit.GetComponent<Enemy>() != null)
            {
                float distanceToEnemy = Vector2.Distance(_checkTransform.position, hit.transform.position);
                if (distanceToEnemy < closestDistance)
                {
                    closestDistance = distanceToEnemy;
                    closestEnemy = hit.transform;
                }
            }
        }
        return closestEnemy;
    }
}
