using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rpg.Networking;

public class PlayerStats : CharaterStats
{
    public Player player;

    protected override void Start()
    {
        player = GetComponent<Player>();
        base.Start();
    }

    public override void TakeDamage(int _damage)
    {
        base.TakeDamage(_damage);
    }

    protected override void Die()
    {
        if (!IsAuthoritative) return;
        base.Die();
        player.Die();
        if (!EntityRuntimePermissions.CanUseLegacyWorld(this)) return;

        GameManager.instance.lostCurrencyAmount = PlayerManager.instance.currency;
        PlayerManager.instance.currency = 0;

        GetComponent<PlayerItemDrop>()?.GenerateDrop();
    }

    public override void DecreaseHealthBy(int _damage)
    {
        if (!IsAuthoritative) return;
        base.DecreaseHealthBy(_damage);
        if (!EntityRuntimePermissions.CanUseLegacyWorld(this)) return;
        if (_damage > GetMaxHealthValue() * 0.3f)
        {
            player.SetupKnockbackPower(new Vector2(5, 3));
            player.fx.ScreenShake(player.fx.shakeHighDamage);
            Debug.Log("high damage taken");
        }
        ItemData_Equipment currentArmor = Inventory.instance.GetEquipment(EquipmentType.Armor);
        if (currentArmor != null)
            currentArmor.Effect(player.transform);
    }

    public override void OnEvasion()
    {
        if (!EntityRuntimePermissions.CanUseLegacyActions(this)) return;
        player.skill.dodge.CreateMirageOnDodge();
    }

    public void CloneDoDamage(CharaterStats _targetStats, float _multiplier)
    {
        if (!EntityRuntimePermissions.CanDamage(this, _targetStats)) return;
        if (TargetCanAvoidAttack(_targetStats))
            return;

        int totalDamage = damage.GetValue() + strength.GetValue();

        if (_multiplier > 0)
            totalDamage = Mathf.RoundToInt(totalDamage * _multiplier);

        if (CanCrit())
            totalDamage = CalculateCriticalDamage(totalDamage);

        totalDamage = CheckTargetArmor(_targetStats, totalDamage);
        _targetStats.TakeDamage(totalDamage);

        DoMagicalDamage(_targetStats);
    }
}
