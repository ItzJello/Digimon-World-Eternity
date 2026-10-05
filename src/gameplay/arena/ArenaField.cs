using Godot;
using System.Collections.Generic;
using System.IO;

namespace DigimonWorldEternity;

/// <summary>
/// A match field built from a floor, a closed wall, a backdrop, and a sky.
/// The pictures are tiled on purpose. The old field meshes carried atlas
/// coordinates, so every surface looked torn.
/// </summary>
public static class ArenaField
{
    private readonly record struct Theme(
        string Floor,
        string Wall,
        string Backdrop,
        float FloorTile,
        float WallRepeat,
        float BackdropRepeat);

    private static readonly Dictionary<string, Theme> Themes = new()
    {
        ["d0172b"] = new("floor_dark.png", "wall_rock.png", "wall_rock_tile.png", 3.6f, 4f, 2f),
        ["d0178b"] = new("floor_wood.png", "floor_wood.png", "backdrop_canopy.png", 3.2f, 3f, 1f),
        ["d0273b"] = new("floor_panels.png", "wall_truss.png", "backdrop_panels.png", 4.6f, 10f, 6f),
        ["d0374b"] = new("floor_stone.png", "wall_crystal.png", "backdrop_crystal.png", 4.2f, 5f, 3f),
        ["d0572b"] = new("floor_rust.png", "wall_rib.png", "backdrop_rust.png", 4.4f, 8f, 4f),
    };

    private static readonly Dictionary<string, Texture2D> Textures = new();

    public static Node3D Build(string stem)
    {
        if (!Themes.ContainsKey(stem))
            stem = "d0172b";
        Theme theme = Themes[stem];
        var root = new Node3D { Name = "arena_field" };

        // A flat square, larger than the wall. No disc and no extra vertex ring,
        // so there is no circular seam to read as a gap.
        const float span = 28f;
        var floorMat = Surface(theme.Floor, Colors.White);
        floorMat.Uv1Scale = new Vector3(span / theme.FloorTile, span / theme.FloorTile, 1f);
        var floor = new MeshInstance3D
        {
            Name = "floor",
            Mesh = new PlaneMesh { Size = new Vector2(span, span) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        floor.SetSurfaceOverrideMaterial(0, floorMat);
        root.AddChild(floor);

        // Tall enough that the fight camera looks at the wall, not over it into the sky.
        // The wall starts below the floor so the seam cannot open onto the sky.
        Texture2D? backdropTex = Load(theme.Backdrop);
        float aspect = backdropTex != null && backdropTex.GetWidth() > 0
            ? backdropTex.GetHeight() / (float)backdropTex.GetWidth()
            : 1f;
        const float tileWidth = 8f;
        const float wallRadius = 10.45f;
        float around = Mathf.Tau * wallRadius / tileWidth;
        float up = 12.6f / (tileWidth * Mathf.Max(aspect, 0.05f));
        var wall = new MeshInstance3D
        {
            Name = "boundary",
            Mesh = Curtain(wallRadius, -0.6f, 12f, around, up),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        wall.SetSurfaceOverrideMaterial(0, Surface(theme.Backdrop, Colors.White));
        root.AddChild(wall);

        AddProps(root, stem, theme);
        return root;
    }

    /// <summary>Kind, x, z, yaw, lean, scale, squash, bury.</summary>
    private readonly record struct Spot(char Kind, float X, float Z, float Yaw, float Lean, float Scale, float Squash, float Bury);

    // Piles and gaps, not a ring of copies. Big pieces stay outside the fight circle.
    private static readonly Dictionary<string, Spot[]> Layouts = new()
    {
        ["d0172b"] = new Spot[]
        {
            new('S', -7.4f, -5.6f, 0.4f, 0.22f, 1.25f, 0.95f, 0.4f),
            new('S', -8.8f, -4.2f, 2.2f, 0.12f, 0.72f, 0.8f, 0.28f),
            new('S', -6.2f, -7.6f, 1.1f, 0.48f, 0.48f, 0.62f, 0.16f),
            new('S', -9.0f, -6.6f, 4.4f, 0.18f, 0.58f, 1.2f, 0.22f),
            new('S', -7.8f, -7.2f, 0.7f, 0.62f, 0.36f, 0.5f, 0.12f),
            new('S', 8.6f, 2.2f, 2.5f, 0.28f, 1.05f, 0.88f, 0.4f),
            new('S', 7.2f, 3.4f, 1.4f, 0.4f, 0.42f, 0.55f, 0.14f),
            new('S', 5.2f, -6.4f, 1.7f, 0.2f, 0.95f, 1.05f, 0.32f),
            new('S', 6.6f, -5.2f, 0.5f, 0.46f, 0.46f, 0.58f, 0.14f),
            new('S', 4.0f, -7.4f, 3.2f, 0.16f, 0.58f, 0.85f, 0.2f),
            new('S', 1.6f, 8.4f, 3.3f, 0.16f, 0.95f, 1.15f, 0.32f),
            new('S', -0.4f, 7.2f, 5.2f, 0.38f, 0.55f, 0.7f, 0.2f),
            new('S', 2.8f, 6.8f, 0.2f, 0.25f, 0.4f, 0.48f, 0.12f),
            new('S', -4.2f, 6.2f, 2.8f, 0.55f, 0.34f, 0.42f, 0.1f),
        },
        ["d0178b"] = new Spot[]
        {
            new('T', -8.6f, -2.2f, 0.6f, 0.06f, 1.35f, 1f, 0.05f),
            new('T', -7.2f, -4.4f, 2.4f, 0.1f, 0.82f, 1f, 0f),
            new('T', -9.2f, -4.6f, 1.2f, 0.04f, 1.05f, 1f, 0.04f),
            new('T', -8.8f, -0.4f, 3.6f, 0.08f, 0.58f, 1f, 0f),
            new('S', -6.8f, -3.2f, 1.7f, 0.35f, 0.4f, 0.6f, 0.12f),
            new('S', -8.0f, -5.4f, 0.4f, 0.5f, 0.32f, 0.45f, 0.08f),
            new('T', 6.8f, -6.6f, 2.1f, 0.05f, 1.15f, 1f, 0.02f),
            new('T', 8.4f, -5.2f, 4.8f, 0.12f, 0.7f, 1f, 0f),
            new('T', 1.4f, 8.8f, 0.9f, 0.07f, 1.45f, 1f, 0.06f),
            new('S', 3.2f, 7.6f, 2.2f, 0.42f, 0.38f, 0.55f, 0.1f),
        },
        ["d0273b"] = new Spot[]
        {
            new('P', -8.2f, 2.4f, 0.3f, 0.02f, 1.35f, 1f, 0f),
            new('P', -7.2f, 4.0f, 0.5f, 0.04f, 0.92f, 1f, 0f),
            new('P', 5.2f, -8.0f, 1.2f, 0.08f, 0.42f, 1f, 0f),
            new('S', 6.2f, -6.8f, 2.4f, 0.3f, 0.46f, 0.55f, 0.12f),
            new('S', 4.2f, -7.2f, 0.8f, 0.48f, 0.34f, 0.4f, 0.08f),
            new('P', 8.8f, -0.6f, 2.2f, 0.03f, 1.15f, 1f, 0f),
            new('P', -2.2f, 8.6f, 0.7f, 0.05f, 0.72f, 1f, 0f),
            new('S', -3.4f, 7.4f, 1.5f, 0.22f, 0.4f, 0.5f, 0.1f),
        },
        ["d0374b"] = new Spot[]
        {
            new('S', -8.4f, -4.8f, 0.5f, 0.1f, 0.7f, 1.85f, 0.3f),
            new('S', -6.8f, -6.6f, 2.1f, 0.18f, 1.05f, 0.9f, 0.35f),
            new('S', -9.0f, -6.2f, 1.4f, 0.35f, 0.42f, 1.4f, 0.16f),
            new('S', -7.6f, -3.4f, 4.0f, 0.22f, 0.5f, 0.7f, 0.18f),
            new('S', 7.8f, 4.6f, 2.6f, 0.12f, 0.62f, 1.7f, 0.28f),
            new('S', 8.8f, 2.4f, 0.9f, 0.28f, 1.0f, 0.85f, 0.35f),
            new('S', 6.4f, 5.6f, 3.3f, 0.4f, 0.36f, 0.55f, 0.1f),
            new('S', 5.6f, -6.2f, 1.5f, 0.14f, 0.55f, 1.65f, 0.24f),
            new('S', 4.2f, -7.6f, 2.8f, 0.32f, 0.9f, 0.8f, 0.3f),
            new('S', 6.8f, -4.8f, 0.6f, 0.22f, 0.38f, 0.5f, 0.1f),
            new('S', -1.2f, 8.6f, 1.6f, 0.15f, 0.85f, 1.55f, 0.32f),
            new('S', 1.4f, 7.4f, 5.0f, 0.45f, 0.4f, 0.6f, 0.14f),
        },
        ["d0572b"] = new Spot[]
        {
            new('S', -8.2f, -5.4f, 0.8f, 0.2f, 1.15f, 0.9f, 0.38f),
            new('S', -6.6f, -7.2f, 2.4f, 0.42f, 0.5f, 0.65f, 0.16f),
            new('P', -9.0f, -3.6f, 1.1f, 0.06f, 0.85f, 1f, 0f),
            new('S', -7.8f, -3.0f, 3.6f, 0.3f, 0.38f, 0.5f, 0.1f),
            new('P', 6.4f, -7.2f, 0.4f, 0.1f, 0.48f, 1f, 0f),
            new('S', 8.2f, -5.4f, 2.0f, 0.25f, 0.95f, 0.8f, 0.3f),
            new('S', 7.0f, -6.0f, 4.4f, 0.5f, 0.36f, 0.45f, 0.1f),
            new('S', 2.6f, 8.2f, 1.3f, 0.18f, 1.2f, 1.05f, 0.4f),
            new('P', 0.4f, 8.8f, 2.7f, 0.04f, 1.05f, 1f, 0f),
            new('S', -1.6f, 7.2f, 0.6f, 0.36f, 0.42f, 0.55f, 0.12f),
        },
    };

    private static void AddProps(Node3D root, string stem, Theme theme)
    {
        if (!Layouts.TryGetValue(stem, out Spot[]? spots))
            spots = Layouts["d0172b"];
        var holder = new Node3D { Name = "props" };
        Node3D? stoneA = LoadProp("stone_a.glb");
        Node3D? stoneB = LoadProp("stone_b.glb");
        Node3D? tree = LoadProp("tree.glb");
        ArrayMesh pillar = Pillar(3.2f, 0.4f, 2.4f);
        int index = 0;
        foreach (Spot spot in spots)
        {
            Vector3 at = Seat(spot);
            float shade = 0.76f + (index % 5) * 0.05f;
            if (spot.Kind == 'P')
            {
                var column = new MeshInstance3D
                {
                    Mesh = pillar,
                    Position = at,
                    Rotation = new Vector3(spot.Lean, spot.Yaw, spot.Lean * 0.4f),
                    Scale = new Vector3(0.82f + (index % 3) * 0.14f, spot.Scale, 0.82f + ((index + 1) % 3) * 0.12f),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                column.SetSurfaceOverrideMaterial(0, Surface(theme.Wall, new Color(shade, shade, shade)));
                holder.AddChild(column);
            }
            else
            {
                Node3D? source = spot.Kind == 'T' ? tree : (index % 2 == 0 ? stoneA : stoneB);
                if (source == null)
                {
                    index++;
                    continue;
                }
                var copy = (Node3D)source.Duplicate();
                copy.Position = at;
                copy.Rotation = new Vector3(spot.Lean, spot.Yaw, spot.Lean * 0.65f);
                copy.Scale = new Vector3(spot.Scale, spot.Scale * spot.Squash, spot.Scale * (0.92f + (index % 3) * 0.06f));
                if (spot.Kind != 'T')
                    Paint(copy, Surface(theme.Wall, new Color(shade, shade * 0.97f, shade * 0.92f)));
                Quiet(copy);
                holder.AddChild(copy);
            }
            index++;
        }
        stoneA?.Free();
        stoneB?.Free();
        tree?.Free();
        if (holder.GetChildCount() > 0)
            root.AddChild(holder);
    }

    private static Vector3 Seat(Spot spot)
    {
        float half = spot.Kind switch
        {
            'T' => 1.05f * spot.Scale,
            'P' => 0.5f,
            _ => 1.5f * spot.Scale,
        };
        var xz = new Vector2(spot.X, spot.Z);
        float distance = xz.Length();
        if (distance < 0.01f)
            xz = Vector2.Right;
        else
            xz /= distance;
        // Keep the piece's body on the rim: outside the fight circle, inside the wall.
        float min = 6.55f + half * 0.72f;
        float max = 9.9f - half;
        if (max < min)
            max = min;
        distance = Mathf.Clamp(distance, min, max);
        xz *= distance;
        return new Vector3(xz.X, -spot.Bury, xz.Y);
    }

    private static Node3D? LoadProp(string file)
    {
        string path = Path.Combine(RepoPaths.StagesDir, "props", file);
        if (!File.Exists(path))
            return null;
        return GlbLoader.Load(path, Path.GetFileNameWithoutExtension(file));
    }

    private static ArrayMesh Pillar(float height, float radius, float repeats)
    {
        const int sides = 8;
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i / (float)sides * Mathf.Tau;
            float a1 = (i + 1) / (float)sides * Mathf.Tau;
            Vector3 p0 = new(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
            Vector3 p1 = new(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
            Vector3 n0 = new Vector3(p0.X, 0f, p0.Z).Normalized();
            Vector3 n1 = new Vector3(p1.X, 0f, p1.Z).Normalized();
            float u0 = i / (float)sides * repeats;
            float u1 = (i + 1) / (float)sides * repeats;
            Put(tool, p0, n0, new Vector2(u0, 1f));
            Put(tool, p1, n1, new Vector2(u1, 1f));
            Put(tool, p1 + Vector3.Up * height, n1, new Vector2(u1, 0f));
            Put(tool, p0, n0, new Vector2(u0, 1f));
            Put(tool, p1 + Vector3.Up * height, n1, new Vector2(u1, 0f));
            Put(tool, p0 + Vector3.Up * height, n0, new Vector2(u0, 0f));
        }
        return tool.Commit();
    }

    private static void Paint(Node node, Material material)
    {
        if (node is MeshInstance3D mesh)
        {
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                mesh.SetSurfaceOverrideMaterial(surface, material);
        }
        foreach (Node child in node.GetChildren())
            Paint(child, material);
    }

    private static void Quiet(Node node)
    {
        if (node is GeometryInstance3D geometry)
            geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        foreach (Node child in node.GetChildren())
            Quiet(child);
    }

    public static void ApplySky(Node root, string stem)
    {
        WorldEnvironment? world = root.GetNodeOrNull<WorldEnvironment>("StageEnvironment");
        if (world?.Environment == null)
            return;
        string path = ArenaMaps.SkyPath(stem);
        if (!File.Exists(path))
            return;
        var image = new Image();
        if (image.Load(path) != Error.Ok)
            return;
        ArenaMaps.Entry? entry = ArenaMaps.Find(stem);
        world.Environment.BackgroundMode = Godot.Environment.BGMode.Sky;
        world.Environment.Sky = new Sky
        {
            SkyMaterial = new PanoramaSkyMaterial
            {
                Panorama = ImageTexture.CreateFromImage(image),
                EnergyMultiplier = entry?.SkyEnergy ?? 1.4f,
            },
        };
        world.Environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        world.Environment.AmbientLightColor = entry?.Ambient ?? new Color(0.55f, 0.64f, 0.82f);
        world.Environment.AmbientLightEnergy = entry?.AmbientEnergy ?? 0.55f;
    }

    private static StandardMaterial3D Surface(string file, Color tint)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = tint,
            AlbedoTexture = Load(file),
            Roughness = 0.92f,
            Metallic = 0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            TextureRepeat = true,
        };
        return material;
    }

    private static Texture2D? Load(string file)
    {
        if (Textures.TryGetValue(file, out Texture2D? cached))
            return cached;
        string path = Path.Combine(RepoPaths.StagesDir, "kit", file);
        if (!File.Exists(path))
            return null;
        var image = new Image();
        if (image.Load(path) != Error.Ok)
            return null;
        ImageTexture texture = ImageTexture.CreateFromImage(image);
        Textures[file] = texture;
        return texture;
    }

    /// <summary>Closed ring with a top lip. No angular gap.</summary>
    private static ArrayMesh Wall(float inner, float thickness, float height, float repeats)
    {
        const int segments = 64;
        float outer = inner + thickness;
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        Band(tool, inner, 0f, height, segments, repeats, true);
        Band(tool, outer, 0f, height, segments, repeats, false);
        Lip(tool, inner, outer, height, segments, repeats);
        return tool.Commit();
    }

    /// <summary>Closed sheet. The picture repeats up the wall so a tall one does not stretch.</summary>
    private static ArrayMesh Curtain(float radius, float y0, float y1, float repeats, float vertical)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        Band(tool, radius, y0, y1, 64, repeats, vertical, true);
        return tool.Commit();
    }

    private static void Band(SurfaceTool tool, float radius, float y0, float y1, int segments, float repeats, bool inward)
        => Band(tool, radius, y0, y1, segments, repeats, 1f, inward);

    private static void Band(SurfaceTool tool, float radius, float y0, float y1, int segments, float repeats, float vertical, bool inward)
    {
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * Mathf.Tau;
            float a1 = (i + 1) / (float)segments * Mathf.Tau;
            Vector3 p0 = new(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
            Vector3 p1 = new(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
            Vector3 n0 = new Vector3(p0.X, 0f, p0.Z).Normalized();
            Vector3 n1 = new Vector3(p1.X, 0f, p1.Z).Normalized();
            if (inward)
            {
                n0 = -n0;
                n1 = -n1;
            }
            float u0 = i / (float)segments * repeats;
            float u1 = (i + 1) / (float)segments * repeats;
            Vector3 b0 = p0 + Vector3.Up * y0;
            Vector3 t0 = p0 + Vector3.Up * y1;
            Vector3 b1 = p1 + Vector3.Up * y0;
            Vector3 t1 = p1 + Vector3.Up * y1;
            Put(tool, b0, n0, new Vector2(u0, vertical));
            Put(tool, b1, n1, new Vector2(u1, vertical));
            Put(tool, t1, n1, new Vector2(u1, 0f));
            Put(tool, b0, n0, new Vector2(u0, vertical));
            Put(tool, t1, n1, new Vector2(u1, 0f));
            Put(tool, t0, n0, new Vector2(u0, 0f));
        }
    }

    private static void Lip(SurfaceTool tool, float inner, float outer, float y, int segments, float repeats)
    {
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * Mathf.Tau;
            float a1 = (i + 1) / (float)segments * Mathf.Tau;
            Vector3 i0 = new(Mathf.Cos(a0) * inner, y, Mathf.Sin(a0) * inner);
            Vector3 i1 = new(Mathf.Cos(a1) * inner, y, Mathf.Sin(a1) * inner);
            Vector3 o0 = new(Mathf.Cos(a0) * outer, y, Mathf.Sin(a0) * outer);
            Vector3 o1 = new(Mathf.Cos(a1) * outer, y, Mathf.Sin(a1) * outer);
            float u0 = i / (float)segments * repeats;
            float u1 = (i + 1) / (float)segments * repeats;
            Put(tool, i0, Vector3.Up, new Vector2(u0, 0.08f));
            Put(tool, i1, Vector3.Up, new Vector2(u1, 0.08f));
            Put(tool, o1, Vector3.Up, new Vector2(u1, 0f));
            Put(tool, i0, Vector3.Up, new Vector2(u0, 0.08f));
            Put(tool, o1, Vector3.Up, new Vector2(u1, 0f));
            Put(tool, o0, Vector3.Up, new Vector2(u0, 0f));
        }
    }

    private static void Put(SurfaceTool tool, Vector3 position, Vector3 normal, Vector2 uv)
    {
        tool.SetNormal(normal);
        tool.SetUV(uv);
        tool.AddVertex(position);
    }
}
