using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// The face texture leaves the sockets empty. The iris and the white of the
/// eye sample the face atlas. Catchlight cards are stored as white and sit
/// on one eye; a duplicated card is renamed eye_h2, so the covered eye flips
/// with the rig. Those cards are hidden. The iris draws just in front of the
/// white, and depth testing stays on so it cannot show through the head.
/// </summary>
public static class FaceEyes
{
    public static void Apply(Node3D actor)
    {
        Shader? irisShader = GD.Load<Shader>("res://assets/shaders/player_iris.gdshader");
        foreach (MeshInstance3D mesh in MeshQuery.Find(actor))
        {
            string name = mesh.Name.ToString().ToLowerInvariant();
            // Catchlights are stored as white RGB with a clear alpha. Drawn
            // opaque, they cover the iris. Godot renames the second card
            // eye_h2. Lashes and brows stay.
            if (IsCatchlight(name))
            {
                mesh.Visible = false;
                continue;
            }
            if (mesh.Mesh == null)
                continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D source)
                    continue;
                if (name == "sirome")
                {
                    var white = (StandardMaterial3D)source.Duplicate();
                    white.AlbedoTexture = null;
                    white.AlbedoColor = new Color(0.93f, 0.94f, 0.96f);
                    white.Metallic = 0;
                    white.MetallicTexture = null;
                    white.RoughnessTexture = null;
                    white.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                    white.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                    white.RenderPriority = 2;
                    mesh.SetSurfaceOverrideMaterial(surface, white);
                }
                else if (name == "eye" && irisShader != null && source.AlbedoTexture != null)
                {
                    var iris = new ShaderMaterial { Shader = irisShader };
                    iris.SetShaderParameter("iris_tex", source.AlbedoTexture);
                    iris.RenderPriority = 3;
                    mesh.SetSurfaceOverrideMaterial(surface, iris);
                }
            }
        }
    }

    private static bool IsCatchlight(string name)
    {
        return name.StartsWith("eye_h") || name.StartsWith("eye_s") || name.StartsWith("eye_blow");
    }
}
