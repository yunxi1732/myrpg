using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkillManager : MonoBehaviour
{
    public static SkillManager instance;
    public Player Owner { get; private set; }

    public void BindOwner(Player owner)
    {
        if (owner == null || Owner != null && Owner != owner)
            throw new System.InvalidOperationException("A SkillManager cannot change combat owner.");
        owner.Combat.BindLegacySkills(this);
        Owner = owner;
        dash = GetComponent<DashSkill>(); clone = GetComponent<CloneSkill>();
        sword = GetComponent<SwordSkill>(); blackhole = GetComponent<BlackholeSkill>();
        crystal = GetComponent<CrystalSkill>(); parry = GetComponent<ParrySkill>(); dodge = GetComponent<DodgeSkill>();
        foreach (Skill component in GetComponents<Skill>()) component.BindOwner(owner.Combat);
    }

    // Single-player composition only. Skills themselves never select a global player.
    public void BindStandaloneOwner()
    {
        if (Owner == null && Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this) && PlayerManager.instance != null)
            BindOwner(PlayerManager.instance.player);
    }

    public DashSkill dash {  get; private set; }
    public CloneSkill clone { get; private set; }
    public SwordSkill sword { get; private set; }
    public BlackholeSkill blackhole { get; private set; }
    public CrystalSkill crystal { get; private set; }
    public ParrySkill parry { get; private set; }
    public DodgeSkill dodge { get; private set; }
    private void Awake()
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this))
        {
            foreach (Skill component in GetComponents<Skill>()) component.enabled = false;
            enabled = false;
            return;
        }
        if (instance != null) Destroy(instance.gameObject);
        else instance = this;
    }

    private void Start()
    {
        BindStandaloneOwner();
        dash = GetComponent<DashSkill>();
        clone = GetComponent<CloneSkill>();
        sword = GetComponent<SwordSkill>();
        blackhole = GetComponent<BlackholeSkill>();
        crystal = GetComponent<CrystalSkill>();
        parry = GetComponent<ParrySkill>();
        dodge = GetComponent<DodgeSkill>();

        StartCoroutine(CheckSkillUnlock());
    }

    private IEnumerator CheckSkillUnlock()
    {
        yield return GameSimulationClock.Wait(GameSimulationClock.DeltaTime);
        dash.CheckUnlock();
        clone.CheckUnlock();
        sword.CheckUnlock();
        blackhole.CheckUnlock();
        crystal.CheckUnlock();
        parry.CheckUnlock(); 
        dodge.CheckUnlock();
    }
}
