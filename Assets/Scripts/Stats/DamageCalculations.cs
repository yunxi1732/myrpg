using System;
using UnityEngine;

/// <summary>Side-effect-free original combat formulas, shared by solo combat and A's room settlement.</summary>
public static class DamageCalculations
{
    public const float ChillMultiplier = .8f;
    public static int ApplyChilledArmor(int armor, bool chilled) => chilled ? Mathf.RoundToInt(armor * ChillMultiplier) : armor;
    public static int IgniteTickDamage(int fire) => Mathf.RoundToInt(fire * .2f);
    public static int ShockChainDamage(int lightning) => Mathf.RoundToInt(lightning * .1f);
    public static int SumDirectMagic(int fire, int ice, int lightning, int intelligence) =>
        (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)fire + ice + lightning + intelligence));

    public static int ApplyResistance(int damage, int resistance, int targetIntelligence) =>
        (int)Math.Max(0, Math.Min(int.MaxValue, (long)damage - resistance - 3L * targetIntelligence));

    public static int ApplyVulnerability(int damage, bool vulnerable)
    {
        if (!vulnerable) return damage;
        float value = damage * 1.1f; // Preserve the original Mathf rounding and vulnerability multiplier.
        return value >= int.MaxValue ? int.MaxValue : Mathf.RoundToInt(value);
    }
}
