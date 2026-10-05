using Godot;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DigimonWorldEternity;

/// <summary>
/// The park file keeps barricades as empty instance points, and Godot drops
/// those points on import. This puts the barricade meshes back on those points.
/// Night and evening cloud cards stay hidden so the afternoon sky is the one on screen.
/// </summary>
public static class ParkProps
{
    public static void Attach(Node3D park)
    {
        HideOtherSkies(park);
        string textures = Path.Combine(RepoPaths.MapsDir, "Textures");
        string props = Path.Combine(RepoPaths.MapsDir, "Props");
        string mapPath = ProjectSettings.GlobalizePath("res://data/maps/park_barricades.json");
        if (!File.Exists(mapPath))
            return;

        var spots = JsonSerializer.Deserialize<List<BarricadeSpot>>(File.ReadAllText(mapPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new List<BarricadeSpot>();
        var templates = new Dictionary<string, Node3D>();
        foreach (BarricadeSpot spot in spots)
        {
            if (!templates.TryGetValue(spot.Mesh, out Node3D? template))
            {
                string glb = Path.Combine(props, spot.Mesh + ".glb");
                if (!File.Exists(glb))
                    continue;
                template = GlbLoader.Load(glb, spot.Mesh);
                string texture = Path.Combine(textures, "barricade_01_c.png");
                if (File.Exists(texture))
                    Paint(template, texture);
                templates[spot.Mesh] = template;
            }

            var holder = new Node3D
            {
                Position = spot.At,
                Scale = spot.Size,
            };
            if (spot.Rotation is { Length: 4 } rotation)
                holder.Quaternion = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
            holder.AddChild(template.Duplicate());
            park.AddChild(holder);
        }

        foreach (Node3D template in templates.Values)
            template.Free();
    }

    private static void Paint(Node3D root, string texturePath)
    {
        var image = new Image();
        if (image.Load(texturePath) != Error.Ok)
            return;
        var material = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image),
            AlbedoColor = Colors.White,
            Metallic = 0,
            Roughness = 0.7f,
        };
        foreach (MeshInstance3D mesh in MeshQuery.Find(root))
        {
            if (mesh.Mesh == null)
                continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                mesh.SetSurfaceOverrideMaterial(surface, material);
        }
    }

    private static void HideOtherSkies(Node3D park)
    {
        foreach (MeshInstance3D mesh in MeshQuery.Find(park))
        {
            string name = mesh.Name.ToString();
            if (name.StartsWith("An_sky") || name.StartsWith("Ae_sky"))
                mesh.Visible = false;
        }
    }

    private sealed class BarricadeSpot
    {
        public string Mesh { get; set; } = "";
        public float[] Translation { get; set; } = { 0, 0, 0 };
        public float[]? Rotation { get; set; }
        public float[] Scale { get; set; } = { 1, 1, 1 };

        public Vector3 At => new(Translation[0], Translation[1], Translation[2]);
        public Vector3 Size => new(Scale[0], Scale[1], Scale[2]);
    }
}
