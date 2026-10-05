using System.Collections.Generic;

namespace DigimonWorldEternity;

/// <summary>
/// Time Stranger motion codes. The suffix means the same thing on every Digimon.
/// Technique clips (e002) are named per species when that move is in the kit.
/// </summary>
public static class ClipLegend
{
    private static readonly Dictionary<string, string> Shared = new()
    {
        ["ba01"] = "Attack",
        ["ba02"] = "Attack 2",
        ["bd01"] = "Light hit",
        ["bd02"] = "Knockdown",
        ["bd03"] = "Get up",
        ["bf01"] = "Attack 3",
        ["bg01"] = "Guard",
        ["bg02"] = "Guard 2",
        ["bn01"] = "Idle",
        ["bn02"] = "Tired idle",
        ["bn90"] = "Idle",
        ["br01"] = "Run",
        ["bs01"] = "Battle pose",
        ["bv01"] = "Victory",
        ["bv02"] = "Victory 2",
        ["bv03"] = "Victory 3",
        ["fq01"] = "Yes",
        ["fq02"] = "No",
        ["fn01"] = "Field idle",
        ["fn01_01"] = "Field idle",
        ["fw01"] = "Field walk",
        ["fw01_01"] = "Field walk",
        ["fr01"] = "Field run",
        ["fr01_01"] = "Field run",
        ["fn02"] = "Sit",
        ["fn02_01"] = "Sit down",
        ["fn02_02"] = "Sitting",
        ["fe01"] = "Reaction",
        ["fe02"] = "Reaction 2",
        ["fe03"] = "Reaction 3",
        ["fe04"] = "Reaction 4",
    };

    /// <summary>Motion code after the character id. chr050_ba01 becomes ba01.</summary>
    public static string Code(string clip)
    {
        if (string.IsNullOrEmpty(clip))
            return "";
        int bar = clip.LastIndexOf('|');
        if (bar >= 0)
            clip = clip[(bar + 1)..];
        int under = clip.IndexOf('_');
        if (under > 0 && IsPackedId(clip[..under]))
            return clip[(under + 1)..];
        return clip;
    }

    /// <summary>Readable name for this clip. Empty when the code is not in the index.</summary>
    public static string Name(string slug, string clip)
    {
        string code = Code(clip);
        if (Shared.TryGetValue(code, out string? shared))
            return shared;
        if (TryTechnique(code, out string id, out string tail))
        {
            string? move = DigimonKit.TechniqueName(slug, id);
            string label = move ?? "Technique";
            if (tail == "_end")
                return label + " end";
            if (tail.Length > 0)
                return label + " " + tail.TrimStart('_');
            return label;
        }
        if (code.Length == 4 && code[0] == 'f' && char.IsDigit(code[1]))
            return "Face shape";
        if (code.Contains("ovr") || code.Contains("lips"))
            return "Face overlay";
        if (code.Contains("loop") || code.EndsWith("in01") || code.EndsWith("out01"))
            return "Ride";
        if (IsPackedId(code))
            return "Bind pose";
        return "";
    }

    /// <summary>Code, then the name. The code stays so the file can still be found.</summary>
    public static string Caption(string slug, string clip) => Caption(slug, clip, npc: false);

    /// <summary>
    /// NPC clips share the field codes, then emotes and look poses.
    /// An e-code on an NPC is an emote, not a technique.
    /// </summary>
    public static string Caption(string slug, string clip, bool npc)
    {
        string code = Code(clip);
        string name = npc ? NpcName(code) : Name(slug, clip);
        return name.Length == 0 ? code : $"{code}    {name}";
    }

    /// <summary>
    /// Look poses use l/r/u/d, and t/b for the up/down diagonal.
    /// Confirm from playback before treating a name as final.
    /// </summary>
    private static string NpcName(string code)
    {
        if (Facing.TryGetValue(code, out string? facing))
            return facing;
        if (Shared.TryGetValue(code, out string? shared))
            return shared;
        if (IsPackedId(code))
            return "Bind pose";
        if (code.StartsWith("blend") && code.Length > 5 && char.IsDigit(code[5]))
            return "Blend " + code[5..].TrimStart('0');
        if (code.StartsWith("ev"))
            return "Event";
        if (code.StartsWith('m') && code.Contains("_c"))
            return "Scene";
        if (TryNumbered(code, "e", out string emoteRest, out string emoteNo))
            return JoinLabel("Emote " + emoteNo, emoteRest);
        if (TryNumbered(code, "m", out string mouthRest, out string mouthNo))
            return JoinLabel("Mouth " + mouthNo, mouthRest);
        if (TryNumbered(code, "t", out string talkRest, out string talkNo))
            return JoinLabel("Talk " + talkNo, talkRest);
        if (TryField(code, out string field))
            return field;
        if (code.Contains("ovr") || code.Contains("lips"))
            return "Face overlay";
        return "";
    }

    private static readonly Dictionary<string, string> Facing = new()
    {
        ["head_n"] = "Look ahead",
        ["head_l"] = "Look left",
        ["head_r"] = "Look right",
        ["head_u"] = "Look up",
        ["head_d"] = "Look down",
        ["head_lt"] = "Look up-left",
        ["head_lb"] = "Look down-left",
        ["head_rt"] = "Look up-right",
        ["head_rb"] = "Look down-right",
        ["head_ub"] = "Look up far",
        ["head_db"] = "Look down far",
        ["eye_l"] = "Eyes left",
        ["eye_r"] = "Eyes right",
        ["eye_u"] = "Eyes up",
        ["eye_d"] = "Eyes down",
    };

    private static readonly Dictionary<string, string> Field = new()
    {
        ["fn"] = "Field idle",
        ["fw"] = "Field walk",
        ["fr"] = "Field run",
        ["ft"] = "Turn",
        ["fnt"] = "Idle turn",
        ["fd"] = "Pose",
        ["fs"] = "Stance",
        ["fst"] = "Stance turn",
        ["fg"] = "Gesture",
    };

    private static bool TryField(string code, out string label)
    {
        label = "";
        foreach (string prefix in new[] { "fnt", "fst", "fn", "fw", "fr", "ft", "fd", "fs", "fg" })
        {
            if (!code.StartsWith(prefix) || code.Length == prefix.Length || !char.IsDigit(code[prefix.Length]))
                continue;
            int i = prefix.Length;
            while (i < code.Length && char.IsDigit(code[i]))
                i++;
            string number = code[prefix.Length..i].TrimStart('0');
            if (number.Length == 0)
                number = "1";
            string name = Field[prefix];
            label = number == "1" ? name : name + " " + number;
            string rest = code[i..];
            if (rest.Length == 0)
                return true;
            if (rest[0] == '_')
            {
                string take = rest[1..].TrimStart('0');
                if (take.Length > 0 && take != "1")
                    label += " " + take;
                return true;
            }
            label += " " + rest;
            return true;
        }
        return false;
    }

    private static bool TryNumbered(string code, string prefix, out string rest, out string number)
    {
        rest = "";
        number = "";
        if (!code.StartsWith(prefix) || code.Length == prefix.Length || !char.IsDigit(code[prefix.Length]))
            return false;
        int i = prefix.Length;
        while (i < code.Length && char.IsDigit(code[i]))
            i++;
        number = code[prefix.Length..i].TrimStart('0');
        if (number.Length == 0)
            number = "0";
        rest = code[i..];
        return true;
    }

    private static string JoinLabel(string label, string rest)
    {
        if (rest.Length == 0)
            return label;
        if (rest.Contains("selfie"))
            return label + " selfie";
        if (rest.Contains("_end") || rest.StartsWith("_end"))
            return label + " end";
        if (rest.Contains("_in"))
            return label + " in";
        if (rest.Contains("close"))
            return label + " closed";
        if (rest.Contains("lips"))
            return label + " lips";
        if (rest == "_ovr" || rest.EndsWith("_ovr"))
            return label + " overlay";
        if (rest.StartsWith('_'))
        {
            string take = rest[1..].TrimStart('_').TrimStart('0');
            return take.Length == 0 ? label : label + " " + take;
        }
        return label + " " + rest.TrimStart('_');
    }

    /// <summary>chr050, npc017a, npch017a. The motion code is the part after the id.</summary>
    private static bool IsPackedId(string head)
    {
        if (head.StartsWith("npch"))
            head = head[4..];
        else if (head.StartsWith("npc") || head.StartsWith("chr"))
            head = head[3..];
        else
            return false;
        if (head.Length == 0 || !char.IsDigit(head[0]))
            return false;
        foreach (char c in head)
        {
            if (!char.IsDigit(c) && !char.IsAsciiLetter(c))
                return false;
        }
        return true;
    }

    private static bool TryTechnique(string code, out string id, out string tail)
    {
        id = "";
        tail = "";
        if (code.Length < 4 || code[0] != 'e' || !char.IsDigit(code[1]))
            return false;
        int i = 1;
        while (i < code.Length && char.IsDigit(code[i]))
            i++;
        if (i < 4)
            return false;
        id = code[..i];
        tail = code[i..];
        return true;
    }
}
