using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DigimonWorldEternity;

/// <summary>
/// Puts the iris atlas on the eye meshes. Digimon irises live in a UV atlas
/// (alpha is the pupil). The player eye is a single disc texture.
/// </summary>
public static class EyeBinder
{
    private static Shader? _shader;
    private static readonly Dictionary<string, Texture2D?> Packed = new();

    public static void Apply(Node3D actor, string texturePath, bool clipAlpha)
    {
        if (!File.Exists(texturePath))
            return;
        var image = new Image();
        if (image.Load(texturePath) != Error.Ok)
            return;

        _shader ??= GD.Load<Shader>("res://assets/shaders/eye_iris.gdshader");
        var texture = ImageTexture.CreateFromImage(image);
        foreach (MeshInstance3D mesh in MeshQuery.Find(actor))
        {
            if (mesh.Mesh == null)
                continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var active = mesh.GetActiveMaterial(surface);
                string name = active?.ResourceName ?? "";
                if (!IsIris(name) && !IsIris(mesh.Name.ToString()))
                    continue;
                // The player iris is already in the model. Replacing it blanks the eye.
                if (!clipAlpha && active is StandardMaterial3D painted && painted.AlbedoTexture != null)
                    continue;
                var material = new ShaderMaterial { Shader = _shader };
                material.SetShaderParameter("iris_tex", texture);
                material.SetShaderParameter("clip_alpha", clipAlpha);
                mesh.SetSurfaceOverrideMaterial(surface, material);
            }
        }
    }

    /// <summary>
    /// Digimon iris color lives in the p01 atlas. The eye cards are bound to
    /// the e01h shine map, which is blank in RGB, so the eyes render white.
    /// The mesh UVs already frame this character's iris.
    /// </summary>
    public static void ApplyPackedIris(Node3D actor, string glbPath)
    {
        bool digimon = false;
        foreach (MeshInstance3D mesh in MeshQuery.Find(actor))
        {
            string name = mesh.Name.ToString().ToLowerInvariant();
            if (name is "eye_l" or "eye_r")
            {
                digimon = true;
                break;
            }
        }
        if (!digimon)
            return;

        Texture2D? iris = PackedIris(glbPath);
        if (iris == null)
            return;

        _shader ??= GD.Load<Shader>("res://assets/shaders/eye_iris.gdshader");
        foreach (MeshInstance3D mesh in MeshQuery.Find(actor))
        {
            string name = mesh.Name.ToString().ToLowerInvariant();
            if (name is not ("eye_l" or "eye_r") || mesh.Mesh == null)
                continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var material = new ShaderMaterial { Shader = _shader, RenderPriority = 2 };
                material.SetShaderParameter("iris_tex", iris);
                material.SetShaderParameter("clip_alpha", true);
                mesh.SetSurfaceOverrideMaterial(surface, material);
            }
        }
    }

    private static Texture2D? PackedIris(string glbPath)
    {
        if (Packed.TryGetValue(glbPath, out Texture2D? cached))
            return cached;

        Texture2D? texture = null;
        try
        {
            byte[]? png = ReadP01(glbPath);
            if (png != null)
            {
                var image = new Image();
                if (image.LoadPngFromBuffer(png) == Error.Ok)
                    texture = ImageTexture.CreateFromImage(image);
            }
        }
        catch (Exception err)
        {
            GD.PrintErr($"iris atlas failed: {glbPath} {err.Message}");
        }

        Packed[glbPath] = texture;
        return texture;
    }

    private static byte[]? ReadP01(string glbPath)
    {
        byte[] file = File.ReadAllBytes(glbPath);
        if (file.Length < 20 || file[0] != (byte)'g')
            return null;

        int offset = 12;
        while (offset + 8 <= file.Length)
        {
            int length = BitConverter.ToInt32(file, offset);
            string kind = System.Text.Encoding.ASCII.GetString(file, offset + 4, 4);
            offset += 8;
            if (length < 0 || offset + length > file.Length)
                return null;
            if (kind == "JSON")
            {
                var json = new byte[length];
                Buffer.BlockCopy(file, offset, json, 0, length);
                using JsonDocument doc = JsonDocument.Parse(json);
                return PngFromJson(doc.RootElement, file, offset + length);
            }
            offset += length;
        }
        return null;
    }

    private static byte[]? PngFromJson(JsonElement root, byte[] file, int binStart)
    {
        if (!root.TryGetProperty("images", out JsonElement images))
            return null;
        int imageIndex = -1;
        int i = 0;
        foreach (JsonElement image in images.EnumerateArray())
        {
            string name = image.TryGetProperty("name", out JsonElement named) ? named.GetString() ?? "" : "";
            if (name.EndsWith("p01", StringComparison.OrdinalIgnoreCase))
            {
                imageIndex = i;
                break;
            }
            i++;
        }
        if (imageIndex < 0)
            return null;

        JsonElement imageNode = images[imageIndex];
        if (!imageNode.TryGetProperty("bufferView", out JsonElement viewIndex))
            return null;
        if (!root.TryGetProperty("bufferViews", out JsonElement views))
            return null;
        JsonElement view = views[viewIndex.GetInt32()];
        int viewOffset = view.TryGetProperty("byteOffset", out JsonElement byteOffset) ? byteOffset.GetInt32() : 0;
        int viewLength = view.GetProperty("byteLength").GetInt32();
        // The BIN chunk follows the JSON chunk. binStart is the first byte of BIN length.
        if (binStart + 8 > file.Length)
            return null;
        int binLength = BitConverter.ToInt32(file, binStart);
        int binData = binStart + 8;
        if (viewOffset < 0 || viewLength < 0 || binData + viewOffset + viewLength > file.Length)
            return null;
        if (binLength < viewOffset + viewLength)
            return null;
        var png = new byte[viewLength];
        Buffer.BlockCopy(file, binData + viewOffset, png, 0, viewLength);
        return png;
    }

    private static bool IsIris(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("blow") || lower.Contains("lash") || lower.Contains("sirome") || lower.Contains("outline"))
            return false;
        return lower is "eye" or "eye_l" or "eye_r";
    }
}
