namespace DigimonWorldEternity;

public enum DigimonElement
{
    Neutral,
    Fire,
    Water,
    Ice,
    Thunder,
    Wind,
    Plant,
    Dark,
}

/// <summary>
/// One learned technique. Numbers are a temporary practice kit until
/// levels and training can change them. The effect stem is a Time Stranger
/// battle animation.
/// </summary>
public sealed class AbilityRecord
{
    public required string Name { get; init; }
    public required int Power { get; init; }
    public required int Mp { get; init; }
    public required bool Melee { get; init; }
    public required DigimonElement Element { get; init; }

    /// <summary>Clip suffix on the digimon GLB, such as e002 or ba01.</summary>
    public required string Clip { get; init; }

    /// <summary>Time Stranger effect stem, such as ef_b_gfi_s02.</summary>
    public required string Effect { get; init; }

    /// <summary>Auto-attack reach. Ranged techniques keep their own distance.</summary>
    public const float MeleeRange = 1.075f;

    /// <summary>Where a melee technique wants to stand, inside MeleeRange.</summary>
    public const float MeleePreferred = 0.8f;

    /// <summary>Farthest this technique can connect, in meters.</summary>
    public float Range => Melee ? MeleeRange : 6.0f;

    /// <summary>Where the Digimon wants to stand to use this technique.</summary>
    public float Preferred => Melee ? MeleePreferred : 4.7f;
}

/// <summary>
/// Static six-stat sheet for a partner we can field today.
/// </summary>
public sealed class DigimonKit
{
    public required string Slug { get; init; }
    public required int Hp { get; init; }
    public required int Mp { get; init; }
    public required int Offense { get; init; }
    public required int Defense { get; init; }
    public required int Speed { get; init; }
    public required int Brains { get; init; }
    public required AbilityRecord[] Abilities { get; init; }

    public static string IconFor(AbilityRecord ability)
    {
        if (ability.Mp <= 0)
            return "skills/ui_icon_skill_000.png";
        return ability.Element switch
        {
            DigimonElement.Fire => "skills/ui_icon_skill_001.png",
            DigimonElement.Ice => "skills/ui_icon_skill_002.png",
            DigimonElement.Plant => "skills/ui_icon_skill_003.png",
            DigimonElement.Water => "skills/ui_icon_skill_004.png",
            DigimonElement.Thunder => "skills/ui_icon_skill_005.png",
            DigimonElement.Wind => "skills/ui_icon_skill_007.png",
            DigimonElement.Dark => "skills/ui_icon_skill_010.png",
            _ => "skills/ui_icon_skill_000.png",
        };
    }

    public static DigimonKit For(string slug)
    {
        return _all.TryGetValue(slug, out DigimonKit? kit) ? kit : _all["agumon"];
    }

    /// <summary>
    /// Move name for a technique clip on this species, such as e002.
    /// Unknown species and basic-attack clips return null.
    /// </summary>
    public static string? TechniqueName(string slug, string clip)
    {
        if (!_all.TryGetValue(slug, out DigimonKit? kit))
            return null;
        foreach (AbilityRecord ability in kit.Abilities)
        {
            if (ability.Clip == clip && ability.Mp > 0)
                return ability.Name;
        }
        return null;
    }

    public string Sheet(string displayName)
    {
        var lines = new System.Text.StringBuilder();
        lines.AppendLine(displayName);
        lines.AppendLine($"HP {Hp}    MP {Mp}");
        lines.AppendLine($"Offense {Offense}    Defense {Defense}");
        lines.AppendLine($"Speed {Speed}    Brains {Brains}");
        lines.AppendLine();
        foreach (AbilityRecord ability in Abilities)
        {
            string reach = ability.Melee ? "Melee" : "Ranged";
            lines.AppendLine($"{ability.Name}   {ability.Power} pow   {ability.Mp} MP   {ability.Element}   {reach}");
        }
        return lines.ToString().TrimEnd();
    }

    private static AbilityRecord Ability(
        string name, int power, int mp, bool melee, DigimonElement element, string clip, string effect)
    {
        return new AbilityRecord
        {
            Name = name,
            Power = power,
            Mp = mp,
            Melee = melee,
            Element = element,
            Clip = clip,
            Effect = effect,
        };
    }

    private static readonly System.Collections.Generic.Dictionary<string, DigimonKit> _all = Build();

    private static System.Collections.Generic.Dictionary<string, DigimonKit> Build()
    {
        var map = new System.Collections.Generic.Dictionary<string, DigimonKit>();
        void Add(DigimonKit kit) => map[kit.Slug] = kit;

        Add(new DigimonKit
        {
            Slug = "agumon", Hp = 820, Mp = 360, Offense = 130, Defense = 85, Speed = 105, Brains = 100,
            Abilities = new[]
            {
                Ability("Pepper Breath", 92, 22, false, DigimonElement.Fire, "e002", "ef_b_gfi_s02"),
                Ability("Spit Fire", 64, 12, false, DigimonElement.Fire, "e006", "ef_b_gfi_s01"),
                Ability("Claw Attack", 42, 0, true, DigimonElement.Fire, "ba01", "ef_b_hit_100_fire"),
            },
        });
        Add(new DigimonKit
        {
            Slug = "gabumon", Hp = 800, Mp = 340, Offense = 120, Defense = 100, Speed = 95, Brains = 110,
            Abilities = new[]
            {
                Ability("Blue Blaster", 90, 22, false, DigimonElement.Ice, "e002", "ef_b_gic_s02"),
                Ability("Little Horn", 48, 0, true, DigimonElement.Ice, "ba01", "ef_b_hit_120_ice"),
                Ability("Freeze Breath", 70, 16, false, DigimonElement.Ice, "e006", "ef_b_gic_s01"),
            },
        });
        Add(new DigimonKit
        {
            Slug = "patamon", Hp = 640, Mp = 400, Offense = 100, Defense = 70, Speed = 120, Brains = 130,
            Abilities = new[]
            {
                Ability("Boom Bubble", 78, 18, false, DigimonElement.Wind, "bs01", "ef_b_gwi_s02"),
                Ability("Air Shot", 60, 12, false, DigimonElement.Wind, "ba02", "ef_b_gwi_s01"),
                Ability("Pummel", 36, 0, true, DigimonElement.Wind, "ba01", "ef_b_hit_130_wind"),
            },
        });
        Add(new DigimonKit
        {
            Slug = "biyomon", Hp = 680, Mp = 380, Offense = 115, Defense = 75, Speed = 125, Brains = 105,
            Abilities = new[]
            {
                Ability("Spiral Twister", 84, 20, false, DigimonElement.Wind, "bs01", "ef_b_gwi_s02"),
                Ability("Peck", 40, 0, true, DigimonElement.Wind, "ba01", "ef_b_hit_130_wind"),
            },
        });
        Add(new DigimonKit
        {
            Slug = "tentomon", Hp = 740, Mp = 320, Offense = 110, Defense = 115, Speed = 80, Brains = 120,
            Abilities = new[]
            {
                Ability("Super Shocker", 88, 20, false, DigimonElement.Thunder, "bs01", "ef_b_gth_s02"),
                Ability("Talon Attack", 44, 0, true, DigimonElement.Thunder, "ba01", "ef_b_hit_140_thunder"),
            },
        });
        Add(new DigimonKit
        {
            Slug = "palmon", Hp = 700, Mp = 420, Offense = 95, Defense = 90, Speed = 85, Brains = 140,
            Abilities = new[]
            {
                Ability("Poison Ivy", 80, 18, false, DigimonElement.Plant, "bs01", "ef_b_hit_160_leaf"),
                Ability("Root Strike", 46, 0, true, DigimonElement.Plant, "ba01", "ef_b_hit_160_leaf"),
            },
        });
        Add(new DigimonKit
        {
            Slug = "gomamon", Hp = 760, Mp = 350, Offense = 105, Defense = 95, Speed = 100, Brains = 115,
            Abilities = new[]
            {
                Ability("Marching Fishes", 86, 20, false, DigimonElement.Water, "bs01", "ef_b_gwa_s02"),
                Ability("Tail Slap", 42, 0, true, DigimonElement.Water, "ba01", "ef_b_hit_110_water"),
            },
        });
        Add(new DigimonKit
        {
            Slug = "elecmon", Hp = 720, Mp = 330, Offense = 125, Defense = 80, Speed = 130, Brains = 90,
            Abilities = new[]
            {
                Ability("Super Thunder Strike", 90, 22, false, DigimonElement.Thunder, "bs01", "ef_b_gle_s02"),
                Ability("Body Blow", 48, 0, true, DigimonElement.Thunder, "ba01", "ef_b_hit_140_thunder"),
            },
        });
        Add(new DigimonKit
        {
            Slug = "betamon", Hp = 780, Mp = 300, Offense = 100, Defense = 110, Speed = 90, Brains = 85,
            Abilities = new[]
            {
                Ability("Electric Shock", 82, 18, false, DigimonElement.Thunder, "bs01", "ef_b_gth_s01"),
                Ability("Fin Cutter", 44, 0, true, DigimonElement.Water, "ba01", "ef_b_hit_110_water"),
            },
        });
        Add(new DigimonKit
        {
            Slug = "gazimon", Hp = 690, Mp = 310, Offense = 135, Defense = 70, Speed = 140, Brains = 80,
            Abilities = new[]
            {
                Ability("Electric Stun", 86, 18, false, DigimonElement.Dark, "bs01", "ef_b_gda_s02"),
                Ability("Black Claw", 50, 0, true, DigimonElement.Dark, "ba01", "ef_b_hit_190_dark"),
            },
        });
        return map;
    }
}
