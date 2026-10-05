using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// Arena master. The format is chosen here, before anyone is called to the floor.
/// </summary>
public partial class ArenaGate : Control
{
    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var background = new TextureRect
        {
            Texture = UiChrome.Load("menu_bg.jpg"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var dim = new ColorRect
        {
            Color = new Color(0.02f, 0.04f, 0.08f, 0.55f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var frame = new NinePatchRect
        {
            Texture = UiChrome.DialogueTexture,
            PatchMarginLeft = 24,
            PatchMarginTop = 58,
            PatchMarginRight = 24,
            PatchMarginBottom = 24,
        };
        frame.SetAnchorsPreset(LayoutPreset.Center);
        frame.OffsetLeft = -390;
        frame.OffsetTop = -280;
        frame.OffsetRight = 390;
        frame.OffsetBottom = 280;
        AddChild(frame);

        var speaker = new Label { Text = "Arena Master" };
        speaker.SetAnchorsPreset(LayoutPreset.TopWide);
        speaker.OffsetLeft = 36;
        speaker.OffsetTop = 12;
        speaker.OffsetRight = -36;
        speaker.OffsetBottom = 52;
        speaker.AddThemeFontSizeOverride("font_size", 22);
        speaker.AddThemeColorOverride("font_color", new Color(1f, 0.86f, 0.45f));
        frame.AddChild(speaker);

        var body = new Label
        {
            Text = "The floor is open. A quick battle is one match. A tournament is a bracket. Practice stays in the house. A match looks for another tamer.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        body.SetAnchorsPreset(LayoutPreset.TopWide);
        body.OffsetLeft = 36;
        body.OffsetTop = 68;
        body.OffsetRight = -36;
        body.OffsetBottom = 150;
        body.AddThemeFontSizeOverride("font_size", 18);
        body.AddThemeColorOverride("font_color", new Color(0.93f, 0.95f, 0.98f));
        frame.AddChild(body);

        var options = new VBoxContainer();
        options.SetAnchorsPreset(LayoutPreset.FullRect);
        options.OffsetLeft = 48;
        options.OffsetTop = 160;
        options.OffsetRight = -48;
        options.OffsetBottom = -28;
        options.AddThemeConstantOverride("separation", 8);
        frame.AddChild(options);

        options.AddChild(Choice("1v1 Quick Battle", () => GameSession.Current.ChooseFormat(ArenaFormat.QuickBattle)));
        options.AddChild(Choice("Single Elimination — Practice", () => GameSession.Current.ChooseFormat(ArenaFormat.SingleEliminationPve)));
        options.AddChild(Choice("Single Elimination — Match", () => GameSession.Current.ChooseFormat(ArenaFormat.SingleEliminationPvp)));
        options.AddChild(Choice("Double Elimination — Match", () => GameSession.Current.ChooseFormat(ArenaFormat.DoubleEliminationPvp)));
        options.AddChild(Choice("Not now", () => GameSession.Current.Travel(GameSession.LobbyScene, "Returning to File City")));
    }

    private static Button Choice(string text, global::System.Action pressed)
    {
        Button button = UiChrome.Button(text, pressed);
        button.CustomMinimumSize = new Vector2(640, 48);
        return button;
    }
}
