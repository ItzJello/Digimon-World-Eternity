using Godot;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DigimonWorldEternity;

/// <summary>
/// Time Stranger walk collision for a map that is not the lobby.
/// Solid mesh names stay blocking after the texture pass hides untextured
/// shells. Border props are the instance points Godot drops on import.
/// </summary>
public static class MapCollision
{
    public static IReadOnlySet<string>? Solids(string stem)
    {
        MapFile? map = Read(stem);
        if (map == null || map.Solid.Count == 0)
            return null;
        return new HashSet<string>(map.Solid);
    }

    public static void Place(Node3D city, string stem)
    {
        MapFile? map = Read(stem);
        if (map != null)
            PlaceSpots(city, map.Borders, "MapBorders");
        DressingFile? dressing = ReadDressing(stem);
        if (dressing == null)
            return;
        var blocking = new List<BorderSpot>();
        var visual = new List<BorderSpot>();
        foreach (BorderSpot spot in dressing.Spots)
        {
            if (IsCard(spot.Mesh))
                continue;
            if (Blocks(spot.Mesh))
                blocking.Add(spot);
            else
                visual.Add(spot);
        }
        PlaceSpots(city, blocking, "MapBorders");
        PlaceSpots(city, visual, "MapDressing");
    }

    private static bool IsCard(string mesh)
    {
        string name = mesh.ToLowerInvariant();
        return name.Contains("sky") || name.Contains("shadow") || name.Contains("outline")
            || name.Contains("fog") || name.Contains("cloud") || name.Contains("particle")
            || name.Contains("effect") || name.Contains("expression");
    }

    private static bool Blocks(string mesh)
    {
        string name = mesh.ToLowerInvariant();
        return name.Contains("build") || name.Contains("bui") || name.Contains("station")
            || name.Contains("wall") || name.Contains("block") || name.Contains("pillar")
            || name.Contains("piller") || name.Contains("gate") || name.Contains("concrete");
    }

    private static void PlaceSpots(Node3D city, List<BorderSpot> spots, string rootName)
    {
        if (spots.Count == 0)
            return;
        string props = Path.Combine(RepoPaths.MapsDir, "Props");
        var root = new Node3D { Name = rootName };
        city.AddChild(root);
        var templates = new Dictionary<string, Node3D?>();
        int placed = 0;
        foreach (BorderSpot spot in spots)
        {
            if (string.IsNullOrEmpty(spot.Mesh))
                continue;
            if (!templates.TryGetValue(spot.Mesh, out Node3D? template))
            {
                string glb = Path.Combine(props, spot.Mesh + ".glb");
                if (!File.Exists(glb))
                {
                    templates[spot.Mesh] = null;
                    continue;
                }
                template = GlbLoader.Load(glb, spot.Mesh);
                string albedo = $"res://data/maps/props/{spot.Mesh}_albedo.json";
                if (File.Exists(ProjectSettings.GlobalizePath(albedo)))
                    CityPainter.Apply(template, Path.Combine(RepoPaths.MapsDir, "Textures"), albedo);
                templates[spot.Mesh] = template;
            }
            if (template == null)
                continue;

            var holder = new Node3D
            {
                Position = spot.At,
                Scale = spot.Size,
            };
            if (spot.Rotation is { Length: 4 } rotation)
                holder.Quaternion = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
            holder.AddChild(template.Duplicate());
            root.AddChild(holder);
            placed++;
        }

        foreach (Node3D? template in templates.Values)
        {
            if (template != null)
                template.Free();
        }
        GD.Print($"{rootName} {placed}");
    }

    private static DressingFile? ReadDressing(string stem)
    {
        string path = ProjectSettings.GlobalizePath($"res://data/maps/{stem}_dressing.json");
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<DressingFile>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static MapFile? Read(string stem)
    {
        string path = ProjectSettings.GlobalizePath($"res://data/maps/{stem}_collision.json");
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<MapFile>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private sealed class MapFile
    {
        public List<string> Solid { get; set; } = new();
        public List<BorderSpot> Borders { get; set; } = new();
    }

    private sealed class DressingFile
    {
        public List<BorderSpot> Spots { get; set; } = new();
    }

    private sealed class BorderSpot
    {
        public string Mesh { get; set; } = "";
        public float[] Translation { get; set; } = { 0, 0, 0 };
        public float[]? Rotation { get; set; }
        public float[] Scale { get; set; } = { 1, 1, 1 };

        public Vector3 At => new(Translation[0], Translation.Length > 1 ? Translation[1] : 0, Translation.Length > 2 ? Translation[2] : 0);
        public Vector3 Size => new(
            Scale.Length > 0 ? Scale[0] : 1,
            Scale.Length > 1 ? Scale[1] : 1,
            Scale.Length > 2 ? Scale[2] : 1);
    }
}
