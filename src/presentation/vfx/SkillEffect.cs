using System.Collections.Generic;
using System.IO;
using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// Plays a Time Stranger battle effect. Ranged techniques travel from the
/// attacker to the defender. A hit spark just plays on the defender.
/// The baked clips burst in place, so a carrier moves the whole effect and
/// keeps its center on that path.
/// </summary>
public static class SkillEffect
{
    public static void Play(Node world, Vector3 from, Vector3 to, string stem, bool travel)
    {
        string path = RepoPaths.Effect(stem);
        if (!File.Exists(path))
        {
            GD.PrintErr($"effect missing: {path}");
            return;
        }

        Node3D effect = GlbLoader.Load(path, stem);
        var cards = new List<ShaderMaterial>();
        Soften(effect, cards);
        var flight = new EffectFlight();
        world.AddChild(flight);
        flight.AddChild(effect);

        float extent = RestExtent(effect);
        float growth = travel ? 5f : 6f;
        float target = travel ? 3.6f : 2.2f;
        if (extent > 0.05f)
            effect.Scale = Vector3.One * (target / (extent * growth));

        Vector3 start = from + Vector3.Up * 0.9f;
        Vector3 end = to + Vector3.Up * 0.85f;
        Vector3 flat = end - start;
        flat.Y = 0f;
        if (travel && flat.Length() > 1.4f)
            end -= flat.Normalized() * 0.65f;
        if (flat.LengthSquared() > 0.01f)
            flight.Rotation = new Vector3(0f, Mathf.Atan2(flat.X, flat.Z), 0f);

        AnimationPlayer? anim = FindAnim(effect);
        double life = 1.2;
        if (anim != null && anim.GetAnimationList().Length > 0)
        {
            string clip = anim.GetAnimationList()[0];
            Animation? animation = anim.GetAnimation(clip);
            if (animation != null)
                animation.LoopMode = Animation.LoopModeEnum.None;
            anim.Play(clip);
            anim.Advance(0);
            life = animation != null ? animation.Length + 0.15 : 1.5;
        }

        float gap = flat.Length();
        float duration = travel ? Mathf.Clamp(gap / 12f, 0.38f, 0.72f) : 0.01f;
        flight.Launch(travel ? start : end, end, duration, life, cards);
    }

    private static readonly Dictionary<ulong, Texture2D> SoftTextures = new();
    private static Shader? _cardShader;

    /// <summary>
    /// Particle cards were blending as solid rectangles: clear texels are
    /// stored white, and the debris chunks borrowed those same maps. Additive
    /// cards keep the sprite and drop the box.
    /// </summary>
    private static void Soften(Node node, List<ShaderMaterial> into)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh != null)
        {
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            if (Chunk(mesh))
            {
                mesh.Visible = false;
            }
            else
            {
                _cardShader ??= GD.Load<Shader>("res://assets/shaders/skill_card.gdshader");
                for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                {
                    Texture2D? tex = null;
                    if (mesh.GetActiveMaterial(surface) is BaseMaterial3D current)
                        tex = current.GetTexture(BaseMaterial3D.TextureParam.Albedo) as Texture2D;
                    tex = tex != null ? SoftTexture(tex) : Glow();
                    var material = new ShaderMaterial { Shader = _cardShader };
                    material.SetShaderParameter("card_tex", tex);
                    material.SetShaderParameter("fade", 1f);
                    mesh.SetSurfaceOverrideMaterial(surface, material);
                    into.Add(material);
                }
            }
        }

        foreach (Node child in node.GetChildren())
            Soften(child, into);
    }

    /// <summary>Rock and ramp blobs. A flat card is thin along one axis.</summary>
    private static bool Chunk(MeshInstance3D mesh)
    {
        int tris = mesh.Mesh.GetFaces().Length / 3;
        if (tris < 80)
            return false;
        Vector3 size = mesh.GetAabb().Size.Abs();
        float longest = Mathf.Max(size.X, Mathf.Max(size.Y, size.Z));
        float shortest = Mathf.Min(size.X, Mathf.Min(size.Y, size.Z));
        return longest > 0.001f && shortest / longest > 0.35f;
    }

    private static Texture2D SoftTexture(Texture2D tex)
    {
        ulong id = tex.GetInstanceId();
        if (SoftTextures.TryGetValue(id, out Texture2D? ready))
            return ready;

        Image? image = tex.GetImage();
        if (image == null)
        {
            Texture2D glow = Glow();
            SoftTextures[id] = glow;
            return glow;
        }
        if (image.IsCompressed())
            image.Decompress();

        int width = image.GetWidth();
        int height = image.GetHeight();
        int step = Mathf.Max(1, Mathf.Max(width, height) / 24);
        bool opaque = true;
        for (int y = 0; y < height && opaque; y += step)
        {
            for (int x = 0; x < width; x += step)
            {
                if (image.GetPixel(x, y).A < 0.97f)
                {
                    opaque = false;
                    break;
                }
            }
        }

        var punched = (Image)image.Duplicate();
        int solid = 0;
        int counted = 0;
        var alpha = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color = punched.GetPixel(x, y);
                float luma = Mathf.Max(color.R, Mathf.Max(color.G, color.B));
                float a = opaque ? Mathf.SmoothStep(0.35f, 0.82f, luma) : color.A;
                if (opaque)
                {
                    float edge = Mathf.Min((x + 0.5f) / width, 1f - (x + 0.5f) / width);
                    edge = Mathf.Min(edge, Mathf.Min((y + 0.5f) / height, 1f - (y + 0.5f) / height));
                    a *= Mathf.SmoothStep(0f, 0.08f, edge);
                }
                alpha[y * width + x] = a;
                if (a > 0.5f)
                    solid++;
                counted++;
            }
        }

        bool disc = opaque && counted > 0 && solid / (float)counted > 0.72f;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color = punched.GetPixel(x, y);
                float a = alpha[y * width + x];
                if (disc)
                {
                    float nx = (x + 0.5f) / width * 2f - 1f;
                    float ny = (y + 0.5f) / height * 2f - 1f;
                    float radius = Mathf.Sqrt(nx * nx + ny * ny);
                    a *= 1f - Mathf.SmoothStep(0.42f, 1f, radius);
                }
                punched.SetPixel(x, y, new Color(color.R * a, color.G * a, color.B * a, a));
            }
        }

        ImageTexture soft = ImageTexture.CreateFromImage(punched);
        SoftTextures[id] = soft;
        return soft;
    }

    private static Texture2D? _glow;

    private static Texture2D Glow()
    {
        if (_glow != null)
            return _glow;
        var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float nx = (x + 0.5f) / 64f * 2f - 1f;
                float ny = (y + 0.5f) / 64f * 2f - 1f;
                float a = 1f - Mathf.SmoothStep(0.15f, 1f, Mathf.Sqrt(nx * nx + ny * ny));
                image.SetPixel(x, y, new Color(a, a, a, a));
            }
        }
        _glow = ImageTexture.CreateFromImage(image);
        return _glow;
    }

    private static float RestExtent(Node3D root)
    {
        float extent = 0f;
        foreach (Node node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh == null)
                continue;
            Vector3 size = mesh.Mesh.GetAabb().Size.Abs();
            Vector3 scaled = size * mesh.GlobalTransform.Basis.Scale.Abs();
            extent = Mathf.Max(extent, Mathf.Max(scaled.X, Mathf.Max(scaled.Y, scaled.Z)));
        }
        return extent;
    }

    private static AnimationPlayer? FindAnim(Node node)
    {
        if (node is AnimationPlayer player)
            return player;
        foreach (Node child in node.GetChildren())
        {
            AnimationPlayer? found = FindAnim(child);
            if (found != null)
                return found;
        }
        return null;
    }
}

/// <summary>
/// Moves a skill effect along a shot and pins the animated cards to that line.
/// The clip's own bone tracks stay on the caster, so the carrier is what travels.
/// </summary>
public partial class EffectFlight : Node3D
{
    private Vector3 _start;
    private Vector3 _end;
    private float _duration = 0.5f;
    private float _age;
    private double _life = 1.2;
    private bool _live;
    private List<ShaderMaterial> _cards = new();

    public void Launch(Vector3 start, Vector3 end, float duration, double life, List<ShaderMaterial> cards)
    {
        _start = start;
        _end = end;
        _duration = Mathf.Max(0.05f, duration);
        _life = life;
        _cards = cards;
        _age = 0f;
        _live = true;
        // Run after the effect's AnimationPlayer (priority 0) so bone tracks
        // cannot leave the cards sitting on the caster.
        ProcessPriority = -100;
        GlobalPosition = start;
    }

    public override void _Process(double delta)
    {
        if (!_live)
            return;

        _age += (float)delta;
        float u = Mathf.Clamp(_age / _duration, 0f, 1f);
        float s = u * u * (3f - 2f * u);
        Vector3 desired = _start.Lerp(_end, s);
        GlobalPosition = desired;
        PinCentroid(this, desired);
        float fade = 1f;
        const float tail = 0.4f;
        if (_age > _life - tail)
            fade = Mathf.Clamp((float)((_life - _age) / tail), 0f, 1f);
        foreach (ShaderMaterial card in _cards)
            card.SetShaderParameter("fade", fade);

        if (_age < _life)
            return;
        _live = false;
        QueueFree();
    }

    private static Vector3 Centroid(Node3D root)
    {
        bool any = false;
        Aabb box = new Aabb();
        foreach (Node node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh == null || !mesh.Visible)
                continue;
            Aabb world = mesh.GlobalTransform * mesh.GetAabb();
            if (!any)
            {
                box = world;
                any = true;
            }
            else
            {
                box = box.Merge(world);
            }
        }
        return any ? box.GetCenter() : root.GlobalPosition;
    }

    private static void PinCentroid(Node3D root, Vector3 desired)
    {
        Vector3 center = Centroid(root);
        Vector3 shift = desired - center;
        if (shift.LengthSquared() < 0.0001f)
            return;

        foreach (Node child in root.GetChildren())
        {
            if (child is Node3D body)
                body.GlobalPosition += shift;
        }
    }
}
