using System;

namespace DigimonWorldEternity;

/// <summary>
/// Hit, damage, and flinch formulas. Element does not change damage yet.
/// </summary>
public static class DamageRules
{
    /// <summary>Chance out of 100 that a technique connects once in range.</summary>
    public static int Accuracy(Combatant attacker, AbilityRecord tech)
    {
        int accuracy = Math.Min(100, 78 + attacker.Kit.Brains / 12);
        if (!tech.Melee)
            accuracy = Math.Min(100, accuracy + 8);
        return accuracy;
    }

    /// <summary>
    /// Power, shifted by the Offense/Defense gap (clamped to ±500), halved
    /// on guard, then rolled 90–110%. Never less than 1.
    /// </summary>
    public static int Damage(Combatant attacker, Combatant victim, AbilityRecord tech, int rollPercent)
    {
        int diff = Math.Clamp(attacker.Kit.Offense - victim.Kit.Defense, -500, 500);
        int damage = tech.Power + diff * tech.Power / 500;
        if (victim.Guarding)
            damage /= 2;
        return Math.Max(1, damage * rollPercent / 100);
    }

    public static float FlinchTime(int damage, bool guarding)
    {
        if (guarding)
            return 0.16f;
        if (damage >= 75)
            return 0.55f;
        if (damage >= 45)
            return 0.42f;
        return 0.28f;
    }

    /// <summary>How far a hit shoves the victim. Guard plants the feet.</summary>
    public static float Knockback(bool guarding) => guarding ? 0.35f : 0.8f;
}
