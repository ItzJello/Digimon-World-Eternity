using Godot;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DigimonWorldEternity;

/// <summary>
/// The park file stores tree positions without the meshes. Godot drops those
/// empty points, so this places the Time Stranger tree models back on them.
/// </summary>
public static class ParkPlants
{
    public static void Attach(Node3D park)
    {
        string mapPath = ProjectSettings.GlobalizePath("res://data/maps/park_plants.json");
        if (!File.Exists(mapPath))
            return;

        var spots = JsonSerializer.Deserialize<List<PlantSpot>>(File.ReadAllText(mapPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new List<PlantSpot>();
        GD.Print($"park trees {spots.Count}");
        string props = Path.Combine(RepoPaths.MapsDir, "Props");
        string textures = Path.Combine(RepoPaths.MapsDir, "Textures");
        var groups = new Dictionary<string, List<Transform3D>>();
        foreach (PlantSpot spot in spots)
        {
            if (string.IsNullOrEmpty(spot.Mesh))
                continue;
            if (!groups.TryGetValue(spot.Mesh, out List<Transform3D>? list))
            {
                list = new List<Transform3D>();
                groups[spot.Mesh] = list;
            }
            list.Add(spot.ToTransform());
        }

        // One shared mesh per tree type. Copying each tree was a separate
        // shadowed object, which is what dropped the frame rate.
        foreach ((string meshName, List<Transform3D> places) in groups)
        {
            string glb = Path.Combine(props, meshName + ".glb");
            if (!File.Exists(glb))
                continue;
            Node3D template = GlbLoader.Load(glb, meshName);
            Paint(template, textures);
            Scatter(park, template, places);
            template.Free();
        }
    }

    private static void Scatter(Node3D park, Node3D template, List<Transform3D> places)
    {
        foreach (MeshInstance3D source in MeshQuery.Find(template))
        {
            if (!source.Visible || source.Mesh == null)
                continue;
            bool leaf = IsLeaf(source);
            Mesh batched = (Mesh)source.Mesh.Duplicate();
            for (int surface = 0; surface < batched.GetSurfaceCount(); surface++)
            {
                Material? material = source.GetSurfaceOverrideMaterial(surface)
                    ?? source.Mesh.SurfaceGetMaterial(surface);
                if (material != null)
                    batched.SurfaceSetMaterial(surface, material);
            }
            var multi = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = batched,
                InstanceCount = places.Count,
            };
            Transform3D local = LocalTo(template, source);
            for (int i = 0; i < places.Count; i++)
                multi.SetInstanceTransform(i, places[i] * local);

            var batch = new MultiMeshInstance3D
            {
                Name = source.Name,
                Multimesh = multi,
                // Leaf cards are alpha-cut and were the expensive shadow casters.
                CastShadow = leaf
                    ? GeometryInstance3D.ShadowCastingSetting.Off
                    : GeometryInstance3D.ShadowCastingSetting.On,
            };
            park.AddChild(batch);
        }
    }

    private static bool IsLeaf(MeshInstance3D mesh)
    {
        if (mesh.Name.ToString().Contains("leaf", System.StringComparison.OrdinalIgnoreCase))
            return true;
        if (mesh.Mesh == null)
            return false;
        for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
        {
            if (mesh.GetSurfaceOverrideMaterial(surface) is StandardMaterial3D material
                && material.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor)
                return true;
        }
        return false;
    }

    private static Transform3D LocalTo(Node3D root, Node3D node)
    {
        Transform3D transform = Transform3D.Identity;
        Node? current = node;
        while (current is Node3D spatial && current != root)
        {
            transform = spatial.Transform * transform;
            current = current.GetParent();
        }
        return transform;
    }

    /// <summary>
    /// The tree shaders only bind a normal and a mask. The color lives in the
    /// matching trunk and leaf images.
    /// </summary>
    private static void Paint(Node3D root, string textures)
    {
        var cache = new Dictionary<string, Texture2D>();
        foreach (MeshInstance3D mesh in MeshQuery.Find(root))
        {
            string name = mesh.Name.ToString();
            if (mesh.Mesh == null)
                continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                string material = mesh.GetActiveMaterial(surface)?.ResourceName ?? name;
                if (material.Equals("hide", System.StringComparison.OrdinalIgnoreCase))
                {
                    mesh.Visible = false;
                    continue;
                }
                string? file = ColorFile(material);
                if (file == null)
                    continue;
                if (!cache.TryGetValue(file, out Texture2D? texture))
                {
                    texture = CityPainter.LoadMapTexture(Path.Combine(textures, file), false);
                    if (texture == null)
                        continue;
                    cache[file] = texture;
                }

                bool leaf = file.StartsWith("leaf_");
                var painted = new StandardMaterial3D
                {
                    AlbedoTexture = texture,
                    AlbedoColor = Colors.White,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                    Metallic = 0,
                    Roughness = leaf ? 0.8f : 0.9f,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                    CullMode = leaf
                        ? BaseMaterial3D.CullModeEnum.Disabled
                        : BaseMaterial3D.CullModeEnum.Back,
                    Transparency = leaf
                        ? BaseMaterial3D.TransparencyEnum.AlphaScissor
                        : BaseMaterial3D.TransparencyEnum.Disabled,
                    AlphaScissorThreshold = 0.25f,
                };
                CityPainter.AttachRelief(painted, textures, file);
                mesh.SetSurfaceOverrideMaterial(surface, painted);
            }
        }
    }

    private static string? ColorFile(string material)
    {
        return material switch
        {
            "treeL06_mesh1" or "treeL07_mesh1" => "trunk_01_c.png",
            "treeL06_mesh2" or "treeL07_mesh2" or "tree08_mesh02" or "etree01_leaf01_mesh01" => "leaf_06_ca.png",
            "tree08_mesh01" or "etree01_mesh01" => "trunk_06_c.png",
            "treeL01_trunk" or "treeL03_trunk03" => "trunk_03_c.png",
            "treeL01_leaf" or "treeL01_leaf12" => "leaf_02_ca.png",
            "treeL03_leaf02" => "leaf_03_ca.png",
            _ => null,
        };
    }

    private sealed class PlantSpot
    {
        public string Mesh { get; set; } = "";
        public float[] T { get; set; } = { 0, 0, 0 };
        public float[] R { get; set; } = { 0, 0, 0, 1 };
        public float[] S { get; set; } = { 1, 1, 1 };

        public Transform3D ToTransform()
        {
            var rotation = new Quaternion(R[0], R[1], R[2], R[3]);
            var basis = new Basis(rotation).Scaled(new Vector3(S[0], S[1], S[2]));
            return new Transform3D(basis, new Vector3(T[0], T[1], T[2]));
        }
    }
}
