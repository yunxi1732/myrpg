using System.Collections;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEngine;
using UnityEngine.Rendering;
using Rpg.Networking;
public enum StatType
{
    strength,
    agility,
    intelligence,
    vitality,
    damage,
    critChance,
    critPower,
    health,
    armor,
    evasion,
    magicRes,
    fireDamage,
    iceDamage,
    lightingDamage
}

[RequireComponent(typeof(EntityFX))]
public class CharaterStats : MonoBehaviour
{
    public bool IsAuthoritative => EntityRuntimePermissions.CanSimulate(this);
    public bool IsRuntimeInitialized { get; private set; }
    private EntityFX fx;

    [Header("Major stats")]
    public Stat strength; // 1 point increase damage by 1 and crit.power by 1%
    public Stat agility; // 1 point increase evasion by 1 and crit.chance by 1%
    public Stat intelligence; // 1 point increase magic damage by 1 and magic resistance by 3%
    public Stat vitality; // 1 point increase health by 3 or 5 points

    [Header("Offensive stats")]
    public Stat damage;
    public Stat critChance;
    public Stat critPower; // default value 150%

    [Header("Defensive stats")]
    public Stat maxHealth;
    public Stat armor;
    public Stat evasion;
    public Stat magicResistance;

    [Header("Magic stats")]
    public Stat fireDamage;
    public Stat iceDamage;
    public Stat lightningDamage;

    public bool isIgnited; // does damage over time
    public bool isChilled; // reduce armor by 20%
    public bool isShocked; // reduce accuracy by 20%

    [SerializeField] private float alimentDuration = 4f;
    private float ignitedTimer;
    private float chillTimer;
    private float shockedTimer;

    private float igniteDamageCooldown = 0.3f;
    private float igniteDamageTimer;
    [SerializeField] private GameObject shockStrikePrefab;
    private int shockDamage;
    private int igniteDamage;
    public float IgniteDuration => alimentDuration;
    public float IgniteInterval => igniteDamageCooldown;
    public GameObject ShockStrikePrefab => shockStrikePrefab;
    internal void SetConfirmedBurn(bool active) { isIgnited = active; }

    public int currentHealth;
    public long NetworkHealthVersion { get; private set; }
    private int confirmedNetworkMaximum;
    internal void EnsureNetworkHealthVersion()
    {
        if (IsAuthoritative && IsRuntimeInitialized && EntityRuntimePermissions.Identity(this)?.IsBound == true && NetworkHealthVersion == 0)
            NetworkHealthVersion = 1;
    }

    internal long ApplyAuthorityNetworkHealth(int finalHealth)
    {
        if (!IsAuthoritative || EntityRuntimePermissions.Identity(this)?.IsBound != true || !IsRuntimeInitialized ||
            finalHealth < 0 || finalHealth > GetMaxHealthValue()) throw new System.InvalidOperationException("Use initialized A health state.");
        EnsureNetworkHealthVersion();
        long version = checked(NetworkHealthVersion + 1);
        currentHealth = finalHealth; NetworkHealthVersion = version;
        return version; // No legacy TakeDamage/Die/elements/drops on the network path.
    }

    internal void ApplyConfirmedNetworkHealth(int health, int maximum, long version)
    {
        if (EntityRuntimePermissions.Identity(this)?.Role != NetworkEntityRole.Display)
            throw new System.InvalidOperationException("Only a display applies A-confirmed final health.");
        currentHealth = health; confirmedNetworkMaximum = maximum; NetworkHealthVersion = version;
    }
    internal void MarkNetworkDead()
    {
        if (!GameSimulationClock.IsNetwork || currentHealth != 0) throw new System.InvalidOperationException("Only a zero-health room entity can die.");
        isDead = true;
    }

    public System.Action OnHealthChanged;
    public bool isDead { get; private set; }
    public bool isInvincible { get; private set; }
    public bool isVulnerable { get; private set; }

    protected virtual void Start()
    {
        fx = GetComponent<EntityFX>();
        IsRuntimeInitialized = true;
        if (!IsAuthoritative) return;
        currentHealth = GetMaxHealthValue();
        EnsureNetworkHealthVersion();
    }

    protected virtual void Update()
    {
        if (!IsAuthoritative) return;
        if (EntityRuntimePermissions.Identity(this) != null) return; // Room effects advance only in A's fixed-tick service.
        ignitedTimer -= GameSimulationClock.DeltaTime;
        chillTimer -= GameSimulationClock.DeltaTime;
        shockedTimer -= GameSimulationClock.DeltaTime;
        igniteDamageTimer -= GameSimulationClock.DeltaTime;

        if (ignitedTimer < 0)
            isIgnited = false;
        if (chillTimer < 0)
            isChilled = false;
        if (shockedTimer < 0)
            isShocked = false;
        if (isIgnited)
            ApplyIgniteDamage();
    }

    public void MakeVulnerableFor(float _duration)
    {
        if (!IsAuthoritative) return;
        StartCoroutine(VulnerableForCoroutine(_duration));
    }

    private IEnumerator VulnerableForCoroutine(float _duration)
    {
        isVulnerable = true;

        yield return GameSimulationClock.Wait(_duration);

        isVulnerable = false;
    }

    public virtual void IncreaseStatBy(int _modifier, float _duration, Stat _statToModify)
    {
        if (!IsAuthoritative) return;
        StartCoroutine(StatModCoroutine(_modifier, _duration, _statToModify));
    }

    private IEnumerator StatModCoroutine(int _modifier, float _duration, Stat _statToModify)
    {
        _statToModify.AddModifier(_modifier);
        yield return GameSimulationClock.Wait(_duration);
        _statToModify.RemoveModifier(_modifier);
    }

    public virtual void DoDamage(CharaterStats _targetStats)
    {
        if (!EntityRuntimePermissions.CanDamage(this, _targetStats)) return;
        bool criticalStrike = false;
        if (TargetCanAvoidAttack(_targetStats))
            return;

        _targetStats.GetComponent<Entity>().SetupKonckbackDir(transform);

        int totalDamage = damage.GetValue() + strength.GetValue();

        if (CanCrit())
        {
            totalDamage = CalculateCriticalDamage(totalDamage);
            criticalStrike = true;
        }

        fx.CreatedHitFx(_targetStats.transform, criticalStrike);

        totalDamage = CheckTargetArmor(_targetStats, totalDamage);
        _targetStats.TakeDamage(totalDamage);
        
        DoMagicalDamage(_targetStats);
    }

    #region Magical damage and aliments
    public virtual void DoMagicalDamage(CharaterStats _targetStats)
    {
        if (!EntityRuntimePermissions.CanDamage(this, _targetStats)) return;
        int _fireDamage = fireDamage.GetValue();
        int _iceDamage = iceDamage.GetValue();
        int _lightningDamage = lightningDamage.GetValue();

        int totalMagicDamage = DamageCalculations.SumDirectMagic(_fireDamage, _iceDamage, _lightningDamage, intelligence.GetValue());

        totalMagicDamage = CheckTargetResistance(_targetStats, totalMagicDamage);
        _targetStats.TakeDamage(totalMagicDamage);

        if (Mathf.Max(_fireDamage, _iceDamage, _lightningDamage) <= 0)
            return;
        AttemptToApplyElements(_targetStats, _fireDamage, _iceDamage, _lightningDamage);
    }

    private void AttemptToApplyElements(CharaterStats _targetStats, int _fireDamage, int _iceDamage, int _lightningDamage)
    {
        bool canApplyIgnite = _fireDamage > _iceDamage && _fireDamage > _lightningDamage;
        bool canApplyChill = _iceDamage > _fireDamage && _iceDamage > _lightningDamage;
        bool canApplyShock = _lightningDamage > _fireDamage && _lightningDamage > _iceDamage;

        while (!canApplyIgnite && !canApplyChill && !canApplyShock)
        {
            if (Random.value < 0.3f && _fireDamage > 0)
            {
                canApplyIgnite = true;
                _targetStats.ApplyAilments(canApplyIgnite, canApplyChill, canApplyShock);
                AudioManager.instance.PlaySFX(7, null);
                return;
            }
            if (Random.value < 0.5f && _iceDamage > 0)
            {
                canApplyChill = true;
                _targetStats.ApplyAilments(canApplyIgnite, canApplyChill, canApplyShock);
                return;
            }
            if (Random.value < 1f && _lightningDamage > 0)
            {
                canApplyShock = true;
                _targetStats.ApplyAilments(canApplyIgnite, canApplyChill, canApplyShock);
                AudioManager.instance.PlaySFX(13, null);
                return;
            }
        }

        if (canApplyIgnite)
            _targetStats.SetupIgniteDamage(DamageCalculations.IgniteTickDamage(_fireDamage));

        if (canApplyShock)
            _targetStats.SetUpShockDamage(DamageCalculations.ShockChainDamage(_lightningDamage));

        _targetStats.ApplyAilments(canApplyIgnite, canApplyChill, canApplyShock);
    }

    public void ApplyAilments(bool _ignite, bool _chill, bool _shock)
    {
        if (!IsAuthoritative) return;
        bool canApplyIgnite = !isShocked && !isIgnited && !isChilled;
        bool canApplyChill = !isShocked && !isIgnited && !isChilled;
        bool canApplyShock = !isIgnited && !isChilled;

        if (_ignite && canApplyIgnite)
        {
            isIgnited = _ignite;
            ignitedTimer = alimentDuration;

            fx.IgniteFxFor(alimentDuration);
        }
        if (_chill && canApplyChill)
        {
            isChilled = _chill;
            chillTimer = alimentDuration;

            float slowPercentage = 1 - DamageCalculations.ChillMultiplier;
            GetComponent<Entity>().SlowEntityBy(slowPercentage, alimentDuration);
            fx.ChillFxFor(alimentDuration);
        }
        if(_shock && canApplyShock)
        {
            if (!isShocked)
            {
                ApplyShock(_shock);
            }
            else
            {
                if (GetComponent<Player>() != null)
                    return;
                HitNearestTargetWithShockStrike();
            }
        }
    }

    public void ApplyShock(bool _shock)
    {
        if (!IsAuthoritative) return;
        if (isShocked)
            return;

        shockedTimer = alimentDuration;
        isShocked = _shock;

        fx.ShockFxFor(alimentDuration);
    }

    private void HitNearestTargetWithShockStrike()
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, 25);
        float closestDistance = Mathf.Infinity;
        Transform closestEnemy = null;
        foreach (var hit in colliders)
        {
            if (hit.GetComponent<Enemy>() != null)
            {
                float distanceToEnemy = Vector2.Distance(transform.position, hit.transform.position);
                if (distanceToEnemy < closestDistance)
                {
                    closestDistance = distanceToEnemy;
                    closestEnemy = hit.transform;
                }
            }
        }

        if (closestEnemy != null)
        {
            GameObject newShockStrike = Instantiate(shockStrikePrefab, transform.position, Quaternion.identity);
            newShockStrike.GetComponent<ShockStrikeController>().SetUp(shockDamage, closestEnemy.GetComponent<CharaterStats>());
        }
    }

    private void ApplyIgniteDamage()
    {
        if (igniteDamageTimer < 0)
        {
            DecreaseHealthBy(igniteDamage);
            if (currentHealth < 0 && !isDead)
                Die();
            igniteDamageTimer = igniteDamageCooldown;
        }
    }

    public void SetupIgniteDamage(int _damage) { if (IsAuthoritative) igniteDamage = _damage; }

    private void SetUpShockDamage(int _damage) => shockDamage = _damage;
    #endregion

    public virtual void TakeDamage(int _damage)
    {
        if (!IsAuthoritative) return;
        if (isInvincible)
            return;

        DecreaseHealthBy(_damage);
        GetComponent<Entity>().DamageImpact();
        fx.AttackedFlashFX();
        if (currentHealth <= 0 && !isDead) 
            Die();

        OnHealthChanged?.Invoke();
    }

    public virtual void IncreaseHealthBy(int _amount)
    {
        if (!IsAuthoritative) return;
        currentHealth += _amount;

        if (currentHealth > GetMaxHealthValue())
            currentHealth = GetMaxHealthValue();

        if (OnHealthChanged != null)
            OnHealthChanged();
    }

    public virtual void DecreaseHealthBy(int _damage)
    {
        if (!IsAuthoritative) return;
        _damage = DamageCalculations.ApplyVulnerability(_damage, isVulnerable);

        currentHealth -= _damage;

        if (_damage > 0)
            fx.CreatePopUpText(_damage.ToString());

        if (OnHealthChanged != null)
            OnHealthChanged();
    }


    protected virtual void Die()
    {
        if (!IsAuthoritative) return;
        isDead = true;
    }

    public void KillEntity()
    {
        if (!IsAuthoritative) return;
        if (!isDead)
            Die();
    }

    public void MakeInvincible(bool _i) { if (IsAuthoritative) isInvincible = _i; }
    internal void ApplyConfirmedInvincibility(bool value) => isInvincible = value;

    #region Start calculations
    protected int CheckTargetArmor(CharaterStats _targetStats, int totalDamage)
    {
        totalDamage -= DamageCalculations.ApplyChilledArmor(_targetStats.armor.GetValue(), _targetStats.isChilled);

        totalDamage = Mathf.Clamp(totalDamage, 0, int.MaxValue);
        return totalDamage;
    }

    private int CheckTargetResistance(CharaterStats _targetStats, int totalMagicDamage)
    {
        return DamageCalculations.ApplyResistance(totalMagicDamage, _targetStats.magicResistance.GetValue(), _targetStats.intelligence.GetValue());
    }

    public virtual void OnEvasion()
    {

    }

    protected bool TargetCanAvoidAttack(CharaterStats _targetStats)
    {
        int totalEvasion = _targetStats.evasion.GetValue() + _targetStats.agility.GetValue();

        if (isShocked)
            totalEvasion += 20;

        if (Random.Range(0, 100) < totalEvasion)
        {
            _targetStats.OnEvasion();
            return true;
        }
        return false;
    }

    protected bool CanCrit()
    {
        int totalCritialChance = critChance.GetValue() + agility.GetValue();
        if (Random.Range(0, 100) <= totalCritialChance)
            return true;
        return false;
    }

    protected int CalculateCriticalDamage(int _damage)
    {
        float totalCriticalPower = (critPower.GetValue() + strength.GetValue()) * 0.01f;
        float critDamage = _damage * totalCriticalPower;
        return Mathf.RoundToInt(critDamage);
    }

    public int GetMaxHealthValue() => NetworkHealthVersion > 0 && EntityRuntimePermissions.Identity(this)?.Role == NetworkEntityRole.Display
        ? confirmedNetworkMaximum : maxHealth.GetValue() + 5 * vitality.GetValue();
    #endregion

    public Stat GetStat(StatType buffType)
    {
        if (buffType == StatType.strength) return strength;
        else if (buffType == StatType.agility) return agility;
        else if (buffType == StatType.intelligence) return intelligence;
        else if (buffType == StatType.vitality) return vitality;
        else if (buffType == StatType.damage) return damage;
        else if (buffType == StatType.critChance) return critChance;
        else if (buffType == StatType.critPower) return critPower;
        else if (buffType == StatType.health) return maxHealth;
        else if (buffType == StatType.armor) return armor;
        else if (buffType == StatType.evasion) return evasion;
        else if (buffType == StatType.magicRes) return magicResistance;
        else if (buffType == StatType.fireDamage) return fireDamage;
        else if (buffType == StatType.iceDamage) return iceDamage;
        else if (buffType == StatType.lightingDamage) return lightningDamage;

        return null;
    }
}
