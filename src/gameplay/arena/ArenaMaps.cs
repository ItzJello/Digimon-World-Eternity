using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace DigimonWorldEternity;

/// <summary>
/// Fields a match session can open. The choice is made when the session
/// starts, not by swapping the ground under a fight already in progress.
/// </summary>
public static class ArenaMaps
{
    public readonly record struct Entry(
        string Stem,
        string Name,
        Color Ambient,
        float AmbientEnergy,
        float SkyEnergy);

    public static readonly Entry[] All =
    {
        new("d0172b", "Night rock", new Color(0.42f, 0.50f, 0.72f), 0.62f, 3.0f),
        new("d0178b", "Forest cove", new Color(0.78f, 0.86f, 0.80f), 0.9f, 1.15f),
        new("d0273b", "Plate cliff", new Color(0.70f, 0.82f, 0.80f), 0.85f, 1.2f),
        new("d0374b", "Crystal yard", new Color(0.42f, 0.52f, 0.72f), 0.7f, 1.35f),
        new("d0572b", "Rust arch", new Color(0.62f, 0.48f, 0.58f), 0.62f, 2.4f),
    };

    public static string SkyPath(string stem) =>
        Path.Combine(RepoPaths.StagesDir, stem + "_sky.png");

    public static bool Exists(string stem)
    {
        if (Find(stem) == null)
            return false;
        return File.Exists(Path.Combine(RepoPaths.StagesDir, "kit", "floor_dark.png"))
            && File.Exists(SkyPath(stem));
    }

    public static List<Entry> Available()
    {
        var found = new List<Entry>();
        foreach (Entry entry in All)
        {
            if (Exists(entry.Stem))
                found.Add(entry);
        }
        return found;
    }

    public static Entry? Find(string stem)
    {
        foreach (Entry entry in All)
        {
            if (entry.Stem == stem)
                return entry;
        }
        return null;
    }

    public static string Caption(string stem) => Find(stem)?.Name ?? stem;

    public static string Roll()
    {
        List<Entry> found = Available();
        if (found.Count == 0)
            return "";
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        return found[rng.RandiRange(0, found.Count - 1)].Stem;
    }

    /// <summary>
    /// Local debug. Queues a field for the next match session.
    /// Ordinary chat lines return false.
    /// </summary>
    public static bool TryCommand(string line, out string reply)
    {
        reply = "";
        string text = line.Trim();
        if (!text.StartsWith('/'))
            return false;

        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string command = parts[0].ToLowerInvariant();
        if (command is not ("/map" or "/maps" or "/field"))
        {
            reply = "Session commands: /maps, /map <name>, /map random";
            return true;
        }

        List<Entry> found = Available();
        if (found.Count == 0)
        {
            reply = "No fields are on disk.";
            return true;
        }

        if (command == "/maps" || parts.Length == 1)
        {
            reply = ListReply(found);
            return true;
        }

        string which = parts[1].ToLowerInvariant();
        if (which is "random" or "any")
        {
            GameSession.Current.QueueField("");
            reply = "The next match will roll a field when the session starts.";
            return true;
        }

        int index = Match(found, which);
        if (index < 0)
        {
            reply = $"No field called \"{parts[1]}\". " + ListReply(found);
            return true;
        }

        Entry chosen = found[index];
        GameSession.Current.QueueField(chosen.Stem);
        reply = $"Next match session will open on {chosen.Name}. The current fight stays where it is.";
        return true;
    }

    private static string ListReply(List<Entry> found)
    {
        var lines = new List<string> { "Fields:" };
        for (int i = 0; i < found.Count; i++)
            lines.Add($"{i + 1}. {found[i].Name}");
        string queued = GameSession.Current.QueuedField;
        lines.Add(string.IsNullOrEmpty(queued)
            ? "The next session rolls one when the match starts. /map <name> to choose it."
            : $"Next session is queued for {Caption(queued)}.");
        return string.Join("\n", lines);
    }

    private static int Match(List<Entry> found, string which)
    {
        if (int.TryParse(which, out int number) && number >= 1 && number <= found.Count)
            return number - 1;
        for (int i = 0; i < found.Count; i++)
        {
            Entry entry = found[i];
            if (entry.Stem.StartsWith(which, StringComparison.OrdinalIgnoreCase)
                || entry.Name.Contains(which, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
