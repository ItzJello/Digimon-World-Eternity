using System.IO;
using System.Linq;

namespace DigimonWorldEternity;

/// <summary>
/// Model 1 is the male body (pc001). Model 2 is the female body (pc002).
/// The other letters are outfits on that body. The full pc001a dump is never loaded.
/// </summary>
public static class CharacterRoster
{
    public static string PathFor(int model, string outfit)
    {
        string letter = string.IsNullOrEmpty(outfit) ? "a" : outfit.ToLowerInvariant();
        if (model != 2 && letter == "a")
            return RepoPaths.CharacterGlb("pc001a_city.glb");
        string stem = model == 2 ? "pc002" : "pc001";
        return RepoPaths.CharacterGlb(stem + letter + ".glb");
    }

    /// <summary>
    /// Creation preview. The full outfit files carry hundreds of unused clips,
    /// so the preview copy is the body plus the idle.
    /// </summary>
    public static string PreviewPath(int model, string outfit)
    {
        string full = PathFor(model, outfit);
        string preview = Path.Combine(
            Path.GetDirectoryName(full) ?? "",
            "preview",
            Path.GetFileName(full));
        return File.Exists(preview) ? preview : full;
    }

    public static string[] Outfits(int model)
    {
        string prefix = model == 2 ? "pc002" : "pc001";
        string dir = RepoPaths.CharactersDir;
        if (!Directory.Exists(dir))
            return new[] { "a" };
        var letters = Directory.GetFiles(dir, prefix + "*.glb")
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .Where(name => name.Length == prefix.Length + 1 && name != "pc001a")
            .Select(name => name[^1].ToString())
            .ToList();
        if (!letters.Contains("a"))
            letters.Add("a");
        letters.Sort(System.StringComparer.Ordinal);
        return letters.ToArray();
    }
}
