using Godot;
using System;
using System.Collections.Generic;

namespace DigimonWorldEternity;

public readonly struct SavedCharacter
{
    public string Name { get; }
    public int Model { get; }
    public string Outfit { get; }

    public SavedCharacter(string name, int model, string outfit)
    {
        Name = name;
        Model = model == 2 ? 2 : 1;
        Outfit = string.IsNullOrEmpty(outfit) ? "a" : outfit;
    }

    public string BodyLabel => Model == 2 ? "Female" : "Male";
}

/// <summary>
/// Local tamer list. One file, rewritten when a character is created.
/// </summary>
public static class CharacterLibrary
{
    private const string SavePath = "user://characters.json";

    public static List<SavedCharacter> LoadAll()
    {
        var list = new List<SavedCharacter>();
        if (!FileAccess.FileExists(SavePath))
            return list;
        using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (file == null)
            return list;
        Variant parsed = Json.ParseString(file.GetAsText());
        if (parsed.VariantType != Variant.Type.Array)
            return list;
        foreach (Variant item in parsed.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary)
                continue;
            Godot.Collections.Dictionary row = item.AsGodotDictionary();
            string name = row.ContainsKey("name") ? row["name"].AsString().Trim() : "";
            int model = row.ContainsKey("model") ? row["model"].AsInt32() : 1;
            string outfit = row.ContainsKey("outfit") ? row["outfit"].AsString() : "a";
            if (name.Length == 0)
                continue;
            list.Add(new SavedCharacter(name, model, outfit));
        }
        return list;
    }

    public static bool Exists(string name)
    {
        string wanted = name.Trim();
        foreach (SavedCharacter saved in LoadAll())
        {
            if (string.Equals(saved.Name, wanted, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static Error Delete(string name)
    {
        var next = new Godot.Collections.Array();
        foreach (SavedCharacter saved in LoadAll())
        {
            if (string.Equals(saved.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            next.Add(Row(saved.Name, saved.Model, saved.Outfit));
        }
        using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file == null)
            return FileAccess.GetOpenError();
        file.StoreString(Json.Stringify(next));
        return Error.Ok;
    }

    public static Error Save(string name, int model, string outfit)
    {
        var next = new Godot.Collections.Array();
        foreach (SavedCharacter saved in LoadAll())
        {
            if (string.Equals(saved.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            next.Add(Row(saved.Name, saved.Model, saved.Outfit));
        }
        next.Add(Row(name.Trim(), model == 2 ? 2 : 1, outfit));
        using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file == null)
            return FileAccess.GetOpenError();
        file.StoreString(Json.Stringify(next));
        return Error.Ok;
    }

    private static Godot.Collections.Dictionary Row(string name, int model, string outfit)
    {
        var row = new Godot.Collections.Dictionary();
        row["name"] = name;
        row["model"] = model;
        row["outfit"] = string.IsNullOrEmpty(outfit) ? "a" : outfit;
        return row;
    }
}
