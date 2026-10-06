using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DigimonWorldEternity;

/// <summary>
/// Walk maps on disk. <c>/gm</c> moves the player onto one.
/// The park lobby is not in this list. <c>/gm lobby</c> sends them back there.
/// </summary>
public static class WalkMaps
{
    public const string Plaza = "HigashiShinjuku_VisionPlaza";

    public static bool TryCommand(string line, out string reply)
    {
        reply = "";
        string text = line.Trim();
        if (!text.StartsWith("/gm", StringComparison.OrdinalIgnoreCase))
            return false;

        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            reply = ListReply(Available());
            return true;
        }

        if (parts[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            reply = ListGroup(Available(), parts.Length >= 3 ? parts[2] : "");
            return true;
        }

        string which = parts[1].Equals("map", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3
            ? parts[2]
            : parts[1];
        if (which.Equals("lobby", StringComparison.OrdinalIgnoreCase)
            || which.Equals("park", StringComparison.OrdinalIgnoreCase)
            || which.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            GameSession.Current.ReturnToLobby();
            reply = "Returning to the park.";
            return true;
        }

        List<string> found = Available();
        List<string> hits = Match(found, which);
        if (hits.Count == 0)
        {
            reply = $"No walk map called \"{which}\".\n" + ListReply(found);
            return true;
        }
        if (hits.Count > 1)
        {
            reply = "More than one map matches.\n" + string.Join(", ", hits.Take(12));
            return true;
        }

        GameSession.Current.OpenWalkMap(hits[0]);
        reply = $"Walking {hits[0]}.";
        return true;
    }

    /// <summary>
    /// The walk map file for a Time Stranger field code such as t0102.
    /// Null when that field has not been exported. The park lobby is t0302.
    /// </summary>
    public static string? StemForCode(string code)
    {
        if (string.IsNullOrEmpty(code))
            return null;
        string low = code.ToLowerInvariant();
        foreach (string stem in Available())
        {
            if (stem.Equals(low + "f", StringComparison.OrdinalIgnoreCase))
                return stem;
        }
        string? named = low switch
        {
            "t0101" => Plaza,
            "t0103" => "Kabukicho_TheaterSquare",
            "t0201" => "Akihabara_ElectricTown",
            "t0301" => "Tokyo_MetropolitanGovernment",
            _ => null,
        };
        if (named != null && File.Exists(RepoPaths.Map(named + ".glb")))
            return named;
        return null;
    }

    public static bool IsLobbyCode(string code) =>
        code.Equals("t0302", StringComparison.OrdinalIgnoreCase);

    /// <summary>Maps built in code, rather than loaded from a field file.</summary>
    public static readonly string[] Built = System.Array.Empty<string>();

    public static List<string> Available()
    {
        var found = new List<string>();
        foreach (string built in Built)
            found.Add(built);
        if (!Directory.Exists(RepoPaths.MapsDir))
            return found;
        foreach (string path in Directory.GetFiles(RepoPaths.MapsDir, "*.glb"))
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (stem.Equals("ShinjukuPark_Waterfall", StringComparison.OrdinalIgnoreCase))
                continue;
            found.Add(stem);
        }
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    /// <summary>
    /// Every map id, one row per area so the chat can scroll through them.
    /// <c>/gm list d02</c> shows a single area.
    /// </summary>
    private static string ListReply(List<string> found)
    {
        var lines = new List<string>
        {
            "/gm <id> walks there. /gm lobby is the park. /gm list d02 shows one area.",
            "Cities: " + Join(found, stem => !IsCoded(stem) && Array.IndexOf(Built, stem) < 0),
        };
        var areas = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string stem in found)
        {
            if (!IsCoded(stem))
                continue;
            string area = stem[..3].ToLowerInvariant();
            if (!areas.TryGetValue(area, out List<string>? ids))
                areas[area] = ids = new List<string>();
            ids.Add(stem);
        }
        foreach ((char letter, string label) in Families)
        {
            bool first = true;
            foreach ((string area, List<string> ids) in areas)
            {
                if (area[0] != letter)
                    continue;
                if (first)
                    lines.Add(label);
                first = false;
                lines.Add($"{area}: {string.Join(" ", ids)}");
            }
        }
        bool other = true;
        foreach ((string area, List<string> ids) in areas)
        {
            if (Array.Exists(Families, f => f.Letter == area[0]))
                continue;
            if (other)
                lines.Add("Other");
            other = false;
            lines.Add($"{area}: {string.Join(" ", ids)}");
        }
        return string.Join("\n", lines);
    }

    private static string ListGroup(List<string> found, string prefix)
    {
        if (prefix.Length == 0)
            return ListReply(found);
        var names = new List<string>();
        foreach (string stem in found)
        {
            if (stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                names.Add(stem);
        }
        if (names.Count == 0)
            return $"No maps start with \"{prefix}\".";
        return $"{prefix}: " + string.Join(" ", names);
    }

    private static readonly (char Letter, string Label)[] Families =
    {
        ('d', "Regions"),
        ('t', "Towns"),
        ('h', "Hubs"),
    };

    private static bool IsCoded(string stem)
    {
        if (stem.Length < 6 || !char.IsLetter(stem[0]))
            return false;
        for (int i = 1; i < 5; i++)
        {
            if (!char.IsDigit(stem[i]))
                return false;
        }
        return true;
    }

    private static string Join(List<string> found, Func<string, bool> take)
    {
        var names = new List<string>();
        foreach (string stem in found)
        {
            if (take(stem))
                names.Add(stem);
        }
        return names.Count == 0 ? "none" : string.Join(" ", names);
    }

    private static List<string> Match(List<string> found, string which)
    {
        var exact = new List<string>();
        var partial = new List<string>();
        foreach (string stem in found)
        {
            if (stem.Equals(which, StringComparison.OrdinalIgnoreCase))
                exact.Add(stem);
            else if (stem.Contains(which, StringComparison.OrdinalIgnoreCase))
                partial.Add(stem);
        }
        return exact.Count > 0 ? exact : partial;
    }
}
