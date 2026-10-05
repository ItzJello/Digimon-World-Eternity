using System.Collections.Generic;
using System.Linq;

namespace DigimonWorldEternity;

public sealed class PartnerRecord
{
    public required string Slug { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// Digimon the player can field right now. Storage is a later system.
/// </summary>
public static class PartnerRoster
{
    public static IReadOnlyList<PartnerRecord> All { get; } = new PartnerRecord[]
    {
        new() { Slug = "agumon", DisplayName = "Agumon" },
        new() { Slug = "gabumon", DisplayName = "Gabumon" },
        new() { Slug = "patamon", DisplayName = "Patamon" },
        new() { Slug = "biyomon", DisplayName = "Biyomon" },
        new() { Slug = "tentomon", DisplayName = "Tentomon" },
        new() { Slug = "palmon", DisplayName = "Palmon" },
        new() { Slug = "gomamon", DisplayName = "Gomamon" },
        new() { Slug = "elecmon", DisplayName = "Elecmon" },
        new() { Slug = "betamon", DisplayName = "Betamon" },
        new() { Slug = "gazimon", DisplayName = "Gazimon" },
    };

    public static PartnerRecord? Find(string slug)
    {
        return All.FirstOrDefault(record => record.Slug == slug);
    }
}
