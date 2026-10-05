namespace DigimonWorldEternity;

/// <summary>
/// Where a combatant is in its action loop. The presentation layer maps
/// these onto clips: Charge plays the technique, Hitstun the light flinch,
/// Down the knockdown.
/// </summary>
public enum FightState
{
    Idle,
    Charge,
    Strike,
    Recover,
    Hitstun,
    Down,
}
