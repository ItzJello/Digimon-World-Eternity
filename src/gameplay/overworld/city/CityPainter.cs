using Godot;
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
    public static void Apply(Node3D city, string textureDir, string mapResPath)
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
        foreach (MeshInstance3D mesh in MeshQuery.Find(city))
        {
            if (mesh.Mesh == null)
                continue;
            int painted = 0;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (!TryFile(map, mesh, surface, out string? file) || file == null)
                    continue;
                if (!cache.TryGetValue(file, out Texture2D? texture))
                {
                    var image = new Image();
                    if (image.Load(Path.Combine(textureDir, file)) != Error.Ok)
                        continue;
                    texture = ImageTexture.CreateFromImage(image);
                    cache[file] = texture;
                }

                var source = mesh.GetActiveMaterial(surface) as StandardMaterial3D;
                var copy = source != null ? (StandardMaterial3D)source.Duplicate() : new StandardMaterial3D();
                copy.AlbedoTexture = texture;
                copy.AlbedoColor = Colors.White;
                copy.Metallic = 0;
                copy.Roughness = 0.86f;
                copy.MetallicSpecular = 0.2f;
                copy.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
                Dress(copy, file);
                mesh.SetSurfaceOverrideMaterial(surface, copy);
                textured++;
                painted++;
            }
            // A surface with no color map draws as a white shell. Leave meshes
            // that were already given a texture, such as the barricades.
            if (painted == 0 && !HasAlbedo(mesh))
                mesh.Visible = false;
            mesh.CastShadow = CastsShadow(mesh.Name.ToString())
                ? GeometryInstance3D.ShadowCastingSetting.On
                : GeometryInstance3D.ShadowCastingSetting.Off;
        }
        GD.Print($"map textures {textured}");
    }

    private static bool CastsShadow(string meshName)
    {
        string name = meshName.ToLowerInvariant();
        // Hills, water, and the sky fill the shadow map and barely change the look.
        return !name.Contains("sky")
            && !name.Contains("grass")
            && !name.Contains("ground")
            && !name.Contains("gravel")
            && !name.Contains("wave")
            && !name.Contains("water");
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
}
