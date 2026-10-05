using Godot;

namespace DigimonWorldEternity;

public static class StageLight
{
    public static void Add(Node parent)
    {
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
            AmbientLightEnergy = 0.40f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 0.96f,
            SsaoEnabled = true,
            SsaoRadius = 0.8f,
            SsaoIntensity = 0.55f,
            SsaoPower = 1.2f,
            SsaoDetail = 0.4f,
            SsaoHorizon = 0.2f,
            SsaoLightAffect = 0.15f,
            FogEnabled = false,
            GlowEnabled = false,
        };
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
            LightEnergy = 1.25f,
            ShadowEnabled = true,
            ShadowBias = 0.03f,
            ShadowNormalBias = 0.35f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 80.0f,
            DirectionalShadowFadeStart = 0.8f,
            DirectionalShadowBlendSplits = true,
        });

        parent.AddChild(new DirectionalLight3D
        {
            Name = "Fill",
            RotationDegrees = new Vector3(-22, 148, 0),
            LightColor = new Color(0.78f, 0.86f, 1.0f),
            LightEnergy = 0.10f,
            ShadowEnabled = false,
        });
    }
}
