using System;

namespace DigimonWorldEternity;

/// <summary>
/// Turns a trainer command into a technique to set up and a range to hold.
/// Movement closes the gap, and the swing starts once the technique can
/// connect. Distance keeps its spacing unless the opponent is already in melee.
/// </summary>
public static class TechniquePlanner
{
    /// <summary>Index of the technique to set up, or -1 to hold.</summary>
    public static int Pick(Combatant self, Combatant other, Random rng)
    {
        int basic = BasicIndex(self);
        bool basicReady = basic >= 0 && self.Cooldown[basic] <= 0f;
        float distance = self.DistanceTo(other);
        // Nose to nose, throw the basic. Switching to a ranged spacing here
        // is the walk-away.
        if (basicReady && self.Command != BattleCommand.Distance && distance <= AbilityRecord.MeleeRange + 0.18f)
            return basic;

        int special = -1;
        for (int i = 0; i < self.Kit.Abilities.Length; i++)
        {
            AbilityRecord tech = self.Kit.Abilities[i];
            if (tech.Mp <= 0 || self.Cooldown[i] > 0f || self.Mp < tech.Mp)
                continue;
            if (self.Command == BattleCommand.Distance && tech.Melee)
                continue;
            if (special < 0 || tech.Power > self.Kit.Abilities[special].Power)
                special = i;
        }

        if (special >= 0 && rng.Next(100) < Aggression(self.Command))
            return special;

        if (self.Command == BattleCommand.Distance)
        {
            if (basicReady && distance <= AbilityRecord.MeleeRange)
                return basic;
            return -1;
        }

        if (!basicReady)
            return special;
        return basic;
    }

    public static int BasicIndex(Combatant self)
    {
        for (int i = 0; i < self.Kit.Abilities.Length; i++)
        {
            if (self.Kit.Abilities[i].Mp <= 0)
                return i;
        }
        return -1;
    }

    /// <summary>Range to hold while setting up this technique.</summary>
    public static float Spacing(Combatant self, AbilityRecord tech)
    {
        float preferred = tech.Preferred;
        if (tech.Melee)
            return Math.Min(preferred, tech.Range - 0.22f);
        if (self.Command == BattleCommand.Distance)
            preferred = Math.Min(tech.Range - 0.35f, preferred + 1.15f);
        else if (self.Command == BattleCommand.Attack)
            preferred = Math.Max(3.4f, preferred - 0.4f);
        return preferred;
    }

    /// <summary>Range to hold when nothing is being set up.</summary>
    public static float RestRange(BattleCommand command)
    {
        return command switch
        {
            BattleCommand.Attack => AbilityRecord.MeleeRange + 0.13f,
            BattleCommand.Distance => 5.2f,
            BattleCommand.Defend => 4.6f,
            _ => 3.4f,
        };
    }

    /// <summary>
    /// Fire once the technique can connect. A ranged shot still waits if they
    /// are on top of each other, so Distance can open the gap first.
    /// </summary>
    public static bool CanFire(Combatant self, Combatant other, AbilityRecord tech)
    {
        float distance = self.DistanceTo(other);
        if (distance > tech.Range)
            return false;
        if (!tech.Melee && distance < Spacing(self, tech) - 1.4f)
            return false;
        return true;
    }

    /// <summary>Chance out of 100 to reach for a special when one is ready.</summary>
    public static int Aggression(BattleCommand command)
    {
        return command switch
        {
            BattleCommand.Attack => 85,
            BattleCommand.Moderate => 50,
            BattleCommand.Distance => 30,
            _ => 0,
        };
    }

    /// <summary>Decision window. Brains shortens the think pause.</summary>
    public static float DecisionWindow(Combatant self, Random rng)
    {
        float brains = Math.Clamp(self.Kit.Brains / 200f, 0f, 1f);
        float window = 0.7f + (0.4f - 0.7f) * brains;
        return window + rng.NextSingle() * 0.2f;
    }
}
