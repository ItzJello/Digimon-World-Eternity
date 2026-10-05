using Godot;
using System.IO;

namespace DigimonWorldEternity;

/// <summary>
/// Time Stranger frames and buttons, loaded from disk so they do not depend
/// on the editor import cache.
/// </summary>
public static class UiChrome
{
    private static Texture2D? _button;
    private static Texture2D? _panel;
    private static Texture2D? _frame;
    private static Texture2D? _dialogue;

    public static Texture2D ButtonTexture => _button ??= Load("menu_button.png");
    public static Texture2D PanelTexture => _panel ??= Load("menu_panel.png");
    public static Texture2D FrameTexture => _frame ??= Load("menu_frame.png");
    public static Texture2D DialogueTexture => _dialogue ??= Load("dialogue_window.png");

    public static Texture2D Load(string fileName)
    {
        string path = Path.Combine(ProjectSettings.GlobalizePath("res://"), "assets", "ui", fileName);
        var image = new Image();
        if (image.Load(path) != Error.Ok)
        {
            GD.PrintErr($"ui image failed: {path}");
            var blank = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
            blank.Fill(new Color(0.05f, 0.1f, 0.16f, 0.9f));
            return ImageTexture.CreateFromImage(blank);
        }
        return ImageTexture.CreateFromImage(image);
    }

    public static StyleBoxTexture ButtonBox()
    {
        return new StyleBoxTexture
        {
            Texture = ButtonTexture,
            TextureMarginLeft = 20,
            TextureMarginRight = 20,
            TextureMarginTop = 18,
            TextureMarginBottom = 18,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
    }

    public static StyleBoxTexture PanelBox()
    {
        return new StyleBoxTexture
        {
            Texture = PanelTexture,
            TextureMarginLeft = 32,
            TextureMarginRight = 32,
            TextureMarginTop = 32,
            TextureMarginBottom = 32,
            ContentMarginLeft = 22,
            ContentMarginRight = 22,
            ContentMarginTop = 20,
            ContentMarginBottom = 20,
        };
    }

    public static Button Button(string text, global::System.Action pressed)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(280, 52),
        };
        StyleBoxTexture normal = ButtonBox();
        StyleBoxTexture hot = ButtonBox();
        hot.ModulateColor = new Color(1.2f, 1.25f, 1.35f);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hot);
        button.AddThemeStyleboxOverride("pressed", hot);
        button.AddThemeStyleboxOverride("focus", normal);
        button.AddThemeFontSizeOverride("font_size", 18);
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", new Color(1f, 0.94f, 0.7f));
        button.Pressed += pressed;
        return button;
    }
}
