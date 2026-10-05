namespace DigimonWorldEternity;

/// <summary>
/// The four DW1 trainer commands. The trainer picks one; the Digimon AI
/// owns approach, spacing, technique choice, and timing.
/// </summary>
public enum BattleCommand
{
    Attack,
    Moderate,
    Distance,
    Defend,
}

public static class BattleCommands
{
    public static string Label(this BattleCommand command)
    {
        return command switch
        {
            BattleCommand.Attack => "Attack",
            BattleCommand.Distance => "Distance",
            BattleCommand.Defend => "Defend",
            _ => "Moderate",
        };
    }
}
