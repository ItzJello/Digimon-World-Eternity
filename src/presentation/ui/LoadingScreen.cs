using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// Drawn before a scene change. The next scene builds on the frame after this
/// one, so the caption is on screen for the hitch.
/// </summary>
public partial class LoadingScreen : Control
{
    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
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
            Color = new Color(0.01f, 0.03f, 0.06f, 0.72f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(420, 120),
        };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.OffsetLeft = -210;
        panel.OffsetTop = -60;
        panel.OffsetRight = 210;
        panel.OffsetBottom = 60;
        panel.AddThemeStyleboxOverride("panel", UiChrome.PanelBox());
        AddChild(panel);

        var label = new Label
        {
            Text = GameSession.Current.LoadingCaption,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 28);
        label.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 1f));
        panel.AddChild(label);

        // Let this screen draw before the lobby load blocks the frame.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string next = GameSession.Current.PendingScene;
        if (string.IsNullOrEmpty(next) || next == GameSession.LoadingScene)
            next = GameSession.LobbyScene;
        GetTree().ChangeSceneToFile(next);
    }
}
