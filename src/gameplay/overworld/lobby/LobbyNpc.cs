using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// A lobby character with one placeholder line. Quests and shops come later.
/// </summary>
public partial class LobbyNpc : Node3D
{
    public const float TalkRange = 2.3f;

    public string Speaker { get; set; } = "";
    public string Line { get; set; } = "";
    public string[] Options { get; set; } = { "Leave" };

    public bool InRangeOf(Vector3 playerPosition)
    {
        return GlobalPosition.DistanceTo(playerPosition) <= TalkRange;
    }
}
