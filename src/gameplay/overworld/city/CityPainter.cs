using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DigimonWorldEternity;

/// <summary>
/// Puts each material's own color texture on the imported mesh.
/// Materials with no color map are left as they were imported.
/// </summary>
public static class CityPainter
{
    public static void Apply(Node3D city, string textureDir, string mapResPath, string stem = "")
    {
        string mapPath = ProjectSettings.GlobalizePath(mapResPath);
        if (!File.Exists(mapPath))
        {
            GD.PushWarning($"No albedo map at {mapPath}");
            return;
        }

        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(mapPath))
            ?? new Dictionary<string, string>();
        var cache = new Dictionary<string, Texture2D>();
        int textured = 0;
        int skies = 0;
        foreach (MeshInstance3D mesh in MeshQuery.Find(city))
        {
            if (mesh.Mesh == null)
                continue;
            int painted = 0;
            bool noShadow = false;
            bool flat = false;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (!TryFile(map, mesh, surface, out string? file) || file == null)
                    continue;
                if (!cache.TryGetValue(file, out Texture2D? texture))
                {
                    texture = LoadMapTexture(Path.Combine(textureDir, file), false);
                    if (texture == null)
                        continue;
                    cache[file] = texture;
                }

                // Projector photos and big glow sheets are light volumes. Drawn solid they sit on the ceiling as slabs.
                if (!CastsShadow(file))
                    flat = true;
                if (IsProjectorRay(file) || (IsGlowCard(file) && BigSheet(mesh)))
                {
                    mesh.Visible = false;
                    noShadow = true;
                    painted++;
                    continue;
                }

                var source = mesh.GetActiveMaterial(surface) as StandardMaterial3D;
                var copy = source != null ? (StandardMaterial3D)source.Duplicate() : new StandardMaterial3D();
                copy.AlbedoTexture = texture;
                copy.AlbedoColor = Colors.White;
                copy.Metallic = 0;
                copy.Roughness = 0.86f;
                copy.MetallicSpecular = 0.2f;
                copy.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
                Dress(copy, file);
                if (IsGlowCard(file))
                {
                    copy.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                    copy.DisableReceiveShadows = true;
                    copy.EmissionEnabled = true;
                    copy.EmissionTexture = texture;
                    copy.Emission = new Color(1.0f, 0.96f, 0.88f);
                    copy.EmissionEnergyMultiplier = 1.5f;
                    noShadow = true;
                }
                Material paintedSurface = copy;
                if (!noShadow && copy.Transparency == BaseMaterial3D.TransparencyEnum.Disabled)
                {
                    ShaderMaterial? ground = GroundMaterial(textureDir, file, texture);
                    if (ground != null)
                        paintedSurface = ground;
                }
                if (paintedSurface == copy)
                    AttachRelief(copy, textureDir, file);
                mesh.SetSurfaceOverrideMaterial(surface, paintedSurface);
                textured++;
                painted++;
            }
            // A surface with no color map draws as a white shell. The sky is
            // painted from its own picture, which is not on the material.
            if (painted == 0 && PaintSky(mesh, stem, textureDir))
            {
                painted = 1;
                skies++;
            }
            if (painted == 0 && !HasAlbedo(mesh))
                mesh.Visible = false;
            bool casts = mesh.Visible && !noShadow && !flat && CastsShadow(mesh.Name.ToString());
            mesh.CastShadow = casts
                ? GeometryInstance3D.ShadowCastingSetting.On
                : GeometryInstance3D.ShadowCastingSetting.Off;
        }
        GD.Print($"map textures {textured}");
        if (skies > 0)
            GD.Print($"sky painted {skies}");
    }

    /// <summary>
    /// The sky dome carries no color slot. Its picture is the field's own sky,
    /// or the shared gradient, and the cloud cards use the cloud atlas.
    /// </summary>
    private static bool PaintSky(MeshInstance3D mesh, string stem, string textureDir)
    {
        if (stem.Length == 0 || mesh.Mesh == null || !mesh.Visible)
            return false;
        string name = mesh.Name.ToString().ToLowerInvariant();
        if (!name.Contains("sky") && !name.Contains("cloud"))
            return false;
        string? file = name.Contains("cloud") ? CloudFile(name) : DomeFile(stem, textureDir);
        if (file == null)
            return false;
        string path = Path.Combine(textureDir, file);
        if (!File.Exists(path))
            return false;
        Texture2D? texture = LoadMapTexture(path, false);
        if (texture == null)
            return false;
        for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
        {
            var source = mesh.GetActiveMaterial(surface) as StandardMaterial3D;
            var copy = source != null ? (StandardMaterial3D)source.Duplicate() : new StandardMaterial3D();
            copy.AlbedoTexture = texture;
            copy.AlbedoColor = Colors.White;
            // The dome's vertex alpha is 0. It is not a transparency mask.
            copy.VertexColorUseAsAlbedo = false;
            Dress(copy, file);
            mesh.SetSurfaceOverrideMaterial(surface, copy);
        }
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return true;
    }

    private static string CloudFile(string name)
    {
        if (name.Contains("cloud04") || name.Contains("cloud_4") || name.Contains("cloud02") || name.Contains("cloud_2"))
            return name.Contains("cloud02") || name.Contains("cloud_2") ? "sky_cloud_02.png" : "sky_cloud_04a.png";
        if (name.Contains("noon"))
            return "sky_cloud_01noon.png";
        return "sky_cloud_01.png";
    }

    private static string? DomeFile(string stem, string textureDir)
    {
        if (stem.Length >= 5 && char.IsLetter(stem[0]) && char.IsDigit(stem[1]))
        {
            string code = stem[..5].ToLowerInvariant();
            string? best = null;
            foreach (string path in Directory.GetFiles(textureDir, code + "*sky*.png"))
            {
                string file = Path.GetFileName(path);
                string low = file.ToLowerInvariant();
                if (low.Contains("sky01") && low.Contains("_c") && !low.Contains("_ca"))
                    return file;
                if (best == null && low.EndsWith("_c.png"))
                    best = file;
            }
            if (best != null)
                return best;
        }
        return File.Exists(Path.Combine(textureDir, "sky_01.png")) ? "sky_01.png" : null;
    }

    /// <summary>A baked photo of the projector beam, not a lamp fixture.</summary>
    private static bool IsProjectorRay(string file) =>
        file.Contains("t3002_light", StringComparison.OrdinalIgnoreCase);

    /// <summary>The radial glow used on lamps and light sheets.</summary>
    private static bool IsGlowCard(string file) =>
        file.Contains("lighit", StringComparison.OrdinalIgnoreCase);

    /// <summary>Two long sides means a sheet hung in the room, not a lamp.</summary>
    private static bool BigSheet(MeshInstance3D mesh)
    {
        Vector3 size = mesh.GetAabb().Size.Abs();
        int longSides = 0;
        if (size.X > 2.0f)
            longSides++;
        if (size.Y > 2.0f)
            longSides++;
        if (size.Z > 2.0f)
            longSides++;
        return longSides >= 2;
    }

    private static bool CastsShadow(string name)
    {
        string n = name.ToLowerInvariant();
        // Ground, water, and the sky fill the shadow map. Floors and ceilings
        // would block the sun, so a person standing in a room never casts.
        if (n.Contains("sky") || n.Contains("grass") || n.Contains("ground")
            || n.Contains("gravel") || n.Contains("wave") || n.Contains("water")
            || n.Contains("leaf"))
            return false;
        if ((n.Contains("floor") || n.Contains("carpet")) && !n.Contains("wall"))
            return false;
        if (n.Contains("ceiling") && !n.Contains("wall"))
            return false;
        return true;
    }

    private static bool HasAlbedo(MeshInstance3D mesh)
    {
        if (mesh.Mesh == null)
            return false;
        for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
        {
            if (mesh.GetSurfaceOverrideMaterial(surface) is StandardMaterial3D material && material.AlbedoTexture != null)
                return true;
        }
        return false;
    }

    private static void Dress(StandardMaterial3D copy, string file)
    {
        string lower = file.ToLowerInvariant();
        bool sky = lower.StartsWith("sky_");
        bool cutout = lower.Contains("cloud") || lower.Contains("ivy") || lower.Contains("leaf")
            || lower.Contains("fence") || lower.Contains("view");
        if (sky)
        {
            copy.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            copy.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            copy.DisableReceiveShadows = true;
        }
        if (cutout)
        {
            // Blended transparency shades every pixel of these cards, which is
            // most of the frame cost on a software renderer. Cut them out instead.
            copy.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
            copy.AlphaScissorThreshold = 0.45f;
            copy.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }
    }

    private static bool TryFile(Dictionary<string, string> map, MeshInstance3D mesh, int surface, out string? file)
    {
        file = null;
        if (mesh.GetActiveMaterial(surface) is Material material &&
            !string.IsNullOrEmpty(material.ResourceName) &&
            map.TryGetValue(material.ResourceName, out file))
            return true;
        return map.TryGetValue(mesh.Name.ToString(), out file);
    }

    /// <summary>
    /// A color picture plus the matching normal and roughness, with mipmaps.
    /// Normals were stored for DirectX, so the green channel is already flipped.
    /// </summary>
    public static Texture2D? LoadMapTexture(string path, bool normal)
    {
        if (!File.Exists(path))
            return null;
        var image = new Image();
        if (image.Load(path) != Error.Ok)
            return null;
        image.GenerateMipmaps(normal);
        return ImageTexture.CreateFromImage(image);
    }

    public static void AttachRelief(StandardMaterial3D copy, string textureDir, string file)
    {
        Texture2D? normal = ReliefTexture(textureDir, Sibling(file, "_n.png"), true);
        if (normal == null && file.Equals("asphalt_08_c.png", StringComparison.OrdinalIgnoreCase))
            normal = ReliefTexture(textureDir, "asphalt_07_n.png", true);
        if (normal != null)
        {
            copy.NormalEnabled = true;
            copy.NormalTexture = normal;
        }

        Texture2D? rough = ReliefTexture(textureDir, Sibling(file, "_rm.png"), false);
        if (rough == null)
            return;
        copy.RoughnessTexture = rough;
        copy.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red;
    }

    private static readonly Dictionary<string, Texture2D> ReliefCache = new();
    private static Shader? _ground;

    private sealed record DetailLayer(string Normal, float Scale);

    // Finer normals only. A second color mixed into the lawn softens the blades.

    private static readonly Dictionary<string, DetailLayer> Details = new(StringComparer.OrdinalIgnoreCase)
    {
        ["grass_01_c.png"] = new("deadleaves_01_n.png", 4f),
        ["tile_21_c.png"] = new("tile_20_n.png", 4f),
    };

    private static ShaderMaterial? GroundMaterial(string textureDir, string file, Texture2D albedo)
    {
        Texture2D? normal = ReliefTexture(textureDir, Sibling(file, "_n.png"), true);
        if (normal == null && file.Equals("asphalt_08_c.png", StringComparison.OrdinalIgnoreCase))
            normal = ReliefTexture(textureDir, "asphalt_07_n.png", true);
        if (normal == null)
            return null;
        _ground ??= GD.Load<Shader>("res://assets/shaders/park_ground.gdshader");
        if (_ground == null)
            return null;

        var material = new ShaderMaterial { Shader = _ground };
        material.SetShaderParameter("albedo_tex", albedo);
        material.SetShaderParameter("normal_tex", normal);
        Texture2D? rough = ReliefTexture(textureDir, Sibling(file, "_rm.png"), false);
        if (rough != null)
        {
            material.SetShaderParameter("has_rough", true);
            material.SetShaderParameter("rough_tex", rough);
        }

        if (Details.TryGetValue(file, out DetailLayer? detail) && detail != null)
        {
            Texture2D? detailNormal = ReliefTexture(textureDir, detail.Normal, true);
            if (detailNormal != null)
            {
                material.SetShaderParameter("has_detail_normal", true);
                material.SetShaderParameter("detail_normal_tex", detailNormal);
                material.SetShaderParameter("detail_scale", detail.Scale);
            }

        }

        bool lawn = file.Contains("grass", StringComparison.OrdinalIgnoreCase);
        material.SetShaderParameter("normal_depth", lawn ? 1.35f : 1.0f);
        return material;
    }

    private static Texture2D? ReliefTexture(string textureDir, string? file, bool normal)
    {
        if (string.IsNullOrEmpty(file))
            return null;
        string path = Path.Combine(textureDir, file);
        if (ReliefCache.TryGetValue(path, out Texture2D? texture))
            return texture;
        texture = LoadMapTexture(path, normal);
        if (texture != null)
            ReliefCache[path] = texture;
        return texture;
    }

    private static string? Sibling(string file, string suffix)
    {
        string stem = Path.GetFileNameWithoutExtension(file);
        if (stem.EndsWith("_ca", StringComparison.OrdinalIgnoreCase))
            stem = stem[..^3];
        else if (stem.EndsWith("_c", StringComparison.OrdinalIgnoreCase))
            stem = stem[..^2];
        else
            return null;
        return stem + suffix;
    }

}
