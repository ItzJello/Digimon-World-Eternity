using Godot;

namespace DigimonWorldEternity;

public static class StageLight
{
    /// <summary>
    /// Floor runs on a typical modern GPU at this window size. Raised spends
    /// more on the shadow map, ambient occlusion, indirect light, and bloom.
    /// </summary>
    public enum StageTier
    {
        Floor,
        Raised,
    }

    public static StageTier Tier { get; set; } = StageTier.Floor;

    /// <summary>
    /// Point the sun the way the field's own sun sits. <paramref name="from"/>
    /// is where the sun is in the sky relative to the map origin.
    /// </summary>
    public static void AimSun(Node parent, Vector3 from)
    {
        if (parent.GetNodeOrNull<DirectionalLight3D>("Sun") is not { } sun)
            return;
        if (from.LengthSquared() < 1.0f)
            return;
        Vector3 direction = from.Normalized();
        // Keep it above the horizon so the shadows still read.
        float minimumHeight = Mathf.Sin(Mathf.DegToRad(12.0f));
        if (direction.Y < minimumHeight)
            direction = new Vector3(direction.X, minimumHeight, direction.Z).Normalized();
        // A noon sun looks straight down, so lean it a little for a readable shadow.
        if (new Vector2(direction.X, direction.Z).LengthSquared() < 0.01f)
            direction = new Vector3(0.25f, direction.Y, 0.25f).Normalized();
        sun.LookAtFromPosition(direction * 50.0f, Vector3.Zero, Vector3.Up);
    }

    public static void Add(Node parent)
    {
        bool raised = Tier == StageTier.Raised;
        ApplyHardware(raised);

        var skyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.45f, 0.66f, 0.92f),
            SkyHorizonColor = new Color(0.78f, 0.86f, 0.95f),
            GroundBottomColor = new Color(0.28f, 0.30f, 0.28f),
            GroundHorizonColor = new Color(0.62f, 0.66f, 0.64f),
            SunAngleMax = 28.0f,
            EnergyMultiplier = 0.88f,
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = skyMaterial },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.32f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 0.96f,
            SsaoEnabled = true,
            SsaoRadius = raised ? 1.3f : 0.9f,
            SsaoIntensity = raised ? 1.4f : 1.0f,
            SsaoPower = 1.5f,
            SsaoDetail = raised ? 0.6f : 0.45f,
            SsaoHorizon = 0.12f,
            SsaoSharpness = 0.9f,
            SsaoLightAffect = 0.35f,
            SsilEnabled = raised,
            SsilRadius = 2.5f,
            SsilIntensity = 0.5f,
            SsilSharpness = 0.5f,
            FogEnabled = false,
            GlowEnabled = true,
            GlowIntensity = raised ? 0.5f : 0.35f,
            GlowStrength = 0.8f,
            GlowBloom = raised ? 0.35f : 0.22f,
            GlowHdrThreshold = 1.0f,
            GlowHdrScale = 1.5f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
        };
        for (int level = 0; level < 7; level++)
            environment.SetGlowLevel(level, level < (raised ? 6 : 4) ? 1.0f : 0.0f);

        AddSharpen(parent);
        parent.AddChild(new WorldEnvironment
        {
            Name = "StageEnvironment",
            Environment = environment,
        });

        // The sky dome does not cast, which was punching holes in the light.
        parent.AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-48, -32, 0),
            LightColor = new Color(1.0f, 0.97f, 0.9f),
            LightEnergy = 1.2f,
            ShadowEnabled = true,
            ShadowBlur = raised ? 1.2f : 0.8f,
            ShadowBias = 0.02f,
            ShadowNormalBias = 0.08f,
            DirectionalShadowMode = raised
                ? DirectionalLight3D.ShadowMode.Parallel4Splits
                : DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowMaxDistance = raised ? 64.0f : 36.0f,
            DirectionalShadowFadeStart = 0.8f,
            DirectionalShadowBlendSplits = true,
        });

        parent.AddChild(new DirectionalLight3D
        {
            Name = "Fill",
            RotationDegrees = new Vector3(-22, 148, 0),
            LightColor = new Color(0.78f, 0.86f, 1.0f),
            LightEnergy = 0.06f,
            ShadowEnabled = false,
        });
    }

    /// <summary>
    /// Shadow map size, occlusion quality, and bloom upscale. The floor stays
    /// on the cheap side of Forward+.
    /// </summary>
    private static void ApplyHardware(bool raised)
    {
        RenderingServer.DirectionalShadowAtlasSetSize(raised ? 4096 : 2048, !raised);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(
            raised ? RenderingServer.ShadowQuality.SoftMedium : RenderingServer.ShadowQuality.SoftLow);
        RenderingServer.EnvironmentSetSsaoQuality(
            raised ? RenderingServer.EnvironmentSsaoQuality.Medium : RenderingServer.EnvironmentSsaoQuality.Low,
            !raised,
            0.5f,
            raised ? 2 : 1,
            30.0f,
            60.0f);
        RenderingServer.EnvironmentGlowSetUseBicubicUpscale(raised);
    }

    /// <summary>
    /// A light sharpen on the 3D view. Layer 0 sits under the HUD.
    /// </summary>
    private static void AddSharpen(Node parent)
    {
        var rect = new ColorRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = new ShaderMaterial
            {
                Shader = GD.Load<Shader>("res://assets/shaders/view_sharpen.gdshader"),
            },
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var layer = new CanvasLayer { Name = "ViewSharpen", Layer = 0 };
        layer.AddChild(rect);
        parent.AddChild(layer);
    }
}
