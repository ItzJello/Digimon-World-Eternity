using System;
using System.IO;
using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// Runtime art lives on disk under <c>res://assets/</c> and is loaded by
/// absolute path. It is not imported as Godot resources. See
/// <c>assets/README.md</c> for the folders a checkout needs.
/// </summary>
public static class RepoPaths
{
    public static string Project =>
        ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');

    public static string Assets => Path.Combine(Project, "assets");

    public static string DigimonDir => Path.Combine(Assets, "models", "digimon");
    public static string CharactersDir => Path.Combine(Assets, "models", "characters");
    public static string EyesDir => Path.Combine(Assets, "models", "eyes");
    public static string MapsDir => Path.Combine(Assets, "maps");
    public static string StagesDir => Path.Combine(Assets, "stages");
    public static string EffectsDir => Path.Combine(Assets, "effects");
    public static string AudioDir => Path.Combine(Assets, "audio");

    public static string DigimonGlb(string slug) =>
        Path.Combine(DigimonDir, slug + ".glb");

    public static string CharacterGlb(string fileName) =>
        Path.Combine(CharactersDir, fileName);

    public static string Eye(string fileName) =>
        Path.Combine(EyesDir, fileName);

    public static string Music(string fileName) =>
        Path.Combine(AudioDir, fileName);

    public static string Map(string fileName) =>
        Path.Combine(MapsDir, fileName);

    public static string Effect(string stem) =>
        Path.Combine(EffectsDir, stem + ".glb");
}
