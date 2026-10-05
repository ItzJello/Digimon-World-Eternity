using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// A short line over a Digimon when they use a technique.
/// Auto attacks stay quiet.
/// </summary>
public static class SkillCallout
{
    private static Texture2D? _bubble;

    public static void Say(Node3D host, string line)
    {
        _bubble ??= UiChrome.Load("skill_bubble.png");
        float body = host.Scale.X;
        float fit = body > 0.05f ? 1f / body : 1f;

        var root = new Node3D
        {
            Name = "SkillCallout",
            Position = new Vector3(0f, 2.15f, 0f),
            Scale = Vector3.One * fit,
        };
        host.AddChild(root);

        var card = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.7f, 1.05f) },
            Position = new Vector3(0f, 0f, 0f),
        };
        card.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoTexture = _bubble,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        root.AddChild(card);

        root.AddChild(new Label3D
        {
            Text = line,
            FontSize = 42,
            PixelSize = 0.0042f,
            Position = new Vector3(0f, 0.12f, 0.02f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(1f, 0.95f, 0.86f),
            OutlineModulate = new Color(0.05f, 0.08f, 0.14f),
            OutlineSize = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        Tween tween = root.CreateTween();
        tween.TweenProperty(root, "position:y", 2.45f, 1.35);

        SceneTree? tree = root.GetTree();
        if (tree == null)
            return;
        tree.CreateTimer(1.45).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(root))
                root.QueueFree();
        };
    }
}
