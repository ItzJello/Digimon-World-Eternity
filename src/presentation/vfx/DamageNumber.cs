using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// A rising number over the Digimon that just got hit.
/// </summary>
public static class DamageNumber
{
    public static void Show(Node3D host, int amount, bool guarded)
    {
        Color ink = guarded
            ? new Color(0.72f, 0.9f, 1f)
            : amount >= 80
                ? new Color(1f, 0.48f, 0.28f)
                : new Color(1f, 0.94f, 0.62f);
        Float(host, amount.ToString(), ink, 72);
    }

    public static void Miss(Node3D host)
    {
        Float(host, "Miss", new Color(0.82f, 0.86f, 0.92f), 58);
    }

    private static void Float(Node3D host, string text, Color ink, int fontSize)
    {
        float body = host.Scale.X;
        float fit = body > 0.05f ? 1f / body : 1f;
        int stack = 0;
        foreach (Node child in host.GetChildren())
        {
            if (child.Name == "DamageFloater")
                stack++;
        }
        float rise = 0.62f * fit;
        float y = HeadTop(host) + (0.12f + stack * 0.34f) * fit;
        float jitter = (GD.Randf() - 0.5f) * 0.55f * fit;

        var root = new Node3D
        {
            Name = "DamageFloater",
            Position = new Vector3(jitter, y, 0f),
            Scale = Vector3.One * (fit * 0.62f),
        };
        host.AddChild(root);

        var label = new Label3D
        {
            Text = text,
            FontSize = fontSize,
            PixelSize = 0.0062f,
            OutlineSize = 18,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Modulate = ink,
            OutlineModulate = new Color(0.08f, 0.05f, 0.04f, 0.92f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        root.AddChild(label);

        Tween tween = root.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(root, "position:y", y + rise, 0.9)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(root, "scale", Vector3.One * fit, 0.16)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(label, "modulate:a", 0f, 0.38).SetDelay(0.52);
        tween.TweenProperty(label, "outline_modulate:a", 0f, 0.38).SetDelay(0.52);

        SceneTree? tree = root.GetTree();
        if (tree == null)
            return;
        tree.CreateTimer(0.98).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(root))
                root.QueueFree();
        };
    }

    /// <summary>Top of the model in the host's local space, ignoring callouts.</summary>
    private static float HeadTop(Node3D host)
    {
        float top = 1.7f;
        bool any = false;
        Measure(host, host, ref top, ref any);
        return any ? top : 1.9f;
    }

    private static void Measure(Node3D host, Node node, ref float top, ref bool any)
    {
        if (node != host && (node.Name == "DamageFloater" || node.Name == "SkillCallout"))
            return;

        if (node is MeshInstance3D mesh && mesh.Mesh != null)
        {
            Aabb box = mesh.GetAabb();
            Transform3D toHost = host.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
            Vector3 origin = box.Position;
            Vector3 end = box.End;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? origin.X : end.X,
                    (i & 2) == 0 ? origin.Y : end.Y,
                    (i & 4) == 0 ? origin.Z : end.Z);
                float y = (toHost * corner).Y;
                if (!any || y > top)
                    top = y;
                any = true;
            }
        }

        foreach (Node child in node.GetChildren())
            Measure(host, child, ref top, ref any);
    }
}
