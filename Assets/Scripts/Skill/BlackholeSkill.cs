using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BlackholeSkill : Skill
{
    [SerializeField] private UI_SkillTreeSlot blackholeUnlockButton;
    public bool blackholeUnlocked {  get; private set; }

    [SerializeField] GameObject blackholePrefab;
    [SerializeField] int amountOfAttack;
    [SerializeField] float growSpeed;
    [SerializeField] float shrinkSpeed;
    [SerializeField] float maxSize;
    [SerializeField] float cloneAttackCooldown;
    [SerializeField] float blackholeDuration;

    BlackholeSkillController currentBlackhole;

    private void UnlockBlackhole()
    {
        if (blackholeUnlockButton.unlocked == true)
            blackholeUnlocked = true;
    }

    public override bool CanUseSkill()
    {
        return base.CanUseSkill();
    }

    protected override void Start()
    {
        base.Start();
        blackholeUnlockButton.GetComponent<Button>().onClick.AddListener(UnlockBlackhole);
    }

    protected override void Update()
    {
        base.Update();
    }

    public override void UseSkill()
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

    protected override void CheckUnlock()
    {
        base.CheckUnlock();
        UnlockBlackhole();
    }
}
