using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

[CreateAssetMenu(fileName = "Buff Effect", menuName = "Data/Item effect/Buff effect")]
public class BuffEffect : ItemEffect
{
    private PlayerStats stats;
    [SerializeField] private StatType buffType;
    [SerializeField] private int buffAmount;
    [SerializeField] private int buffDuration;

    public override void ExecuteEffect(Transform _enemyPosition)
    {
        stats = PlayerManager.instance.player.GetComponent<PlayerStats>();
        stats.IncreaseStatBy(buffAmount, buffDuration, StatToModify());
    }

    private Stat StatToModify()
    {
        if (buffType == StatType.strength) return stats.strength;
        else if (buffType == StatType.agility) return stats.agility;
        else if (buffType == StatType.intelligence) return stats.intelligence;
        else if (buffType == StatType.vitality) return stats.vitality;
        else if (buffType == StatType.damage) return stats.damage;
        else if (buffType == StatType.critChance) return stats.critChance;
        else if (buffType == StatType.critPower) return stats.critPower;
        else if (buffType == StatType.health) return stats.maxHealth;
        else if (buffType == StatType.armor) return stats.armor;
        else if (buffType == StatType.evasion) return stats.evasion;
        else if (buffType == StatType.magicRes) return stats.magicResistance;
        else if (buffType == StatType.fireDamage) return stats.fireDamage;
        else if (buffType == StatType.iceDamage) return stats.iceDamage;
        else if (buffType == StatType.lightingDamage) return stats.lightningDamage;

        return null;
    }
}
