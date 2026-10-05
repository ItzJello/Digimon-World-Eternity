using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// Replaces imported character materials with a lit shader that keeps their textures.
/// </summary>
public static class SurfaceLight
{
    private static Shader? _shader;

    public static void Apply(Node3D actor)
    {
        _shader ??= GD.Load<Shader>("res://assets/shaders/actor_lit.gdshader");
        foreach (MeshInstance3D mesh in MeshQuery.Find(actor))
        {
            if (mesh.Mesh == null)
                continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var source = mesh.GetActiveMaterial(surface) as StandardMaterial3D;
                var material = new ShaderMaterial { Shader = _shader };
                if (source?.AlbedoTexture != null)
                {
                    material.SetShaderParameter("has_albedo", true);
                    material.SetShaderParameter("albedo_tex", source.AlbedoTexture);
                }
                else
                {
                    material.SetShaderParameter("has_albedo", false);
                    material.SetShaderParameter("albedo_color", source?.AlbedoColor ?? Colors.White);
                }
                mesh.SetSurfaceOverrideMaterial(surface, material);
            }
        }
    }
}
