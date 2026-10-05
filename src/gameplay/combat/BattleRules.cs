namespace DigimonWorldEternity;

/// <summary>
/// Tuning shared by every battle. Placeholder values until the owner's
/// stats plan says how the six stats should move a bout.
/// </summary>
public static class BattleRules
{
    /// <summary>Open floor inside the rock ring. Past this the camera leaves the bowl.</summary>
    public const float ArenaRadius = 6.4f;

    /// <summary>
    /// Closest two fighters may stand. Stays inside the melee strike range
    /// so an auto attack can still connect.
    /// </summary>
    public const float Contact = 0.68f;

    /// <summary>Wind-up before the swing lands.</summary>
    public static float ChargeTime(AbilityRecord tech) => tech.Melee ? 0.35f : 0.55f;

    /// <summary>How long the strike frame holds.</summary>
    public const float StrikeTime = 0.18f;

    /// <summary>Cooldown after a swing before the next decision.</summary>
    public static float RecoverTime(AbilityRecord tech) => tech.Melee ? 0.45f : 0.7f;

    /// <summary>Cooldown applied to the technique that was just used.</summary>
    public static float Cooldown(AbilityRecord tech)
    {
        if (tech.Mp <= 0)
            return 2.4f;
        return tech.Melee ? 8f : 11f;
    }

    /// <summary>Using a special locks the other specials for at least this long.</summary>
    public const float SpecialLockout = 6.5f;

    /// <summary>Seconds per point of MP regen. Distance recovers faster.</summary>
    public static float RegenPeriod(BattleCommand command) => command == BattleCommand.Distance ? 0.16f : 0.33f;

    /// <summary>How long a target stays set up before the AI reconsiders.</summary>
    public const float IntentPatience = 3.2f;
}
