using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BlackholeSkill : Skill
{
    [SerializeField] GameObject blackholePrefab;
    [SerializeField] int amountOfAttack;
    [SerializeField] float growSpeed;
    [SerializeField] float shrinkSpeed;
    [SerializeField] float maxSize;
    [SerializeField] float cloneAttackCooldown;
    [SerializeField] float blackholeDuration;

    BlackholeSkillController currentBlackhole;

    public override bool CanUseSkill()
    {
        return base.CanUseSkill();
    }

    protected override void Start()
    {
        base.Start();
    }

    protected override void Update()
    {
        base.Update();
    }

    protected override void UseSkill()
    {
        base.UseSkill();
        GameObject newBlackhole = Instantiate(blackholePrefab, player.transform.position, Quaternion.identity);
        currentBlackhole = newBlackhole.GetComponent<BlackholeSkillController>();
        currentBlackhole.SetUpBlackhole(amountOfAttack, maxSize, growSpeed, shrinkSpeed, cloneAttackCooldown, blackholeDuration);
    }

    public bool SkillCompleted()
    {
        if (!currentBlackhole) return false;
        if (currentBlackhole.playerCanExitState)
        {
            currentBlackhole = null;
            return true;
        }
        return false;
    }

    public float GetBlackholeRadius() => maxSize / 2;
}
