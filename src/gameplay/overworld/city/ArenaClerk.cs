using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// Debug NPC. Talking offers the arena test.
/// </summary>
public partial class ArenaClerk : Node3D
{
    public const float TalkRange = 2.6f;
    public const string Speaker = "Arena Clerk";
    public const string Line = "The arena is open. The master inside will set the format.";
    public static readonly string[] Options = { "Enter the Arena", "Leave" };

    public bool InRangeOf(Vector3 playerPosition)
    {
        return GlobalPosition.DistanceTo(playerPosition) <= TalkRange;
    }
}
