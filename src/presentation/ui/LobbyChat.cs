using Godot;
using System;

namespace DigimonWorldEternity;

/// <summary>
/// Local lobby chat. Enter opens the line box. The log stays on screen.
/// A server can feed the same log later.
/// </summary>
public partial class LobbyChat : CanvasLayer
{
    public bool IsTyping { get; private set; }

    /// <summary>Arena docks the log on the right so it clears the command diamond.</summary>
    public bool DockRight { get; set; }

    /// <summary>When this returns true, Enter is left alone.</summary>
    public Func<bool>? Suppressed { get; set; }

    private readonly VBoxContainer _log = new();
    private readonly LineEdit _input = new();
    private const int MaxLines = 8;

    public override void _Ready()
    {
        Layer = 5;
        var box = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.SetAnchorsPreset(DockRight
            ? Control.LayoutPreset.BottomRight
            : Control.LayoutPreset.BottomLeft);
        box.OffsetLeft = DockRight ? -536 : 16;
        box.OffsetTop = -248;
        box.OffsetRight = DockRight ? -16 : 520;
        box.OffsetBottom = -16;
        AddChild(box);

        var frame = new NinePatchRect
        {
            Texture = UiChrome.Load("chat_window.png"),
            PatchMarginLeft = 24,
            PatchMarginTop = 24,
            PatchMarginRight = 24,
            PatchMarginBottom = 24,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.AddChild(frame);

        _log.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _log.OffsetLeft = 18;
        _log.OffsetTop = 14;
        _log.OffsetRight = -16;
        _log.OffsetBottom = -58;
        _log.AddThemeConstantOverride("separation", 2);
        _log.MouseFilter = Control.MouseFilterEnum.Ignore;
        box.AddChild(_log);

        _input.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _input.OffsetLeft = 16;
        _input.OffsetTop = -50;
        _input.OffsetRight = -16;
        _input.OffsetBottom = -12;
        _input.Visible = false;
        _input.PlaceholderText = "Say something";
        _input.MaxLength = 120;
        _input.AddThemeFontSizeOverride("font_size", 16);
        _input.AddThemeColorOverride("font_color", Colors.White);
        _input.AddThemeColorOverride("font_placeholder_color", new Color(0.7f, 0.78f, 0.86f));
        StyleBoxTexture field = UiChrome.ButtonBox();
        _input.AddThemeStyleboxOverride("normal", field);
        _input.AddThemeStyleboxOverride("focus", field);
        _input.TextSubmitted += Submit;
        box.AddChild(_input);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;
        if (Suppressed != null && Suppressed())
            return;

        bool enter = key.Keycode is Key.Enter or Key.KpEnter;
        if (enter && !IsTyping)
        {
            OpenInput();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (IsTyping && key.Keycode == Key.Escape)
        {
            CloseInput();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Post(string speaker, string text)
    {
        string safeSpeaker = speaker.Replace("[", "(");
        string safeText = text.Replace("[", "(");
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            Text = $"[color=#ffd36a]{safeSpeaker}[/color][color=#e7eef8]: {safeText}[/color]",
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("normal_font_size", 16);
        _log.AddChild(label);
        while (_log.GetChildCount() > MaxLines)
            _log.GetChild(0).QueueFree();
    }

    public void CloseInput()
    {
        IsTyping = false;
        _input.Text = "";
        _input.Visible = false;
        _input.ReleaseFocus();
    }

    private void OpenInput()
    {
        IsTyping = true;
        _input.Visible = true;
        _input.Text = "";
        _input.GrabFocus();
    }

    private void Submit(string text)
    {
        string line = text.Trim();
        CloseInput();
        if (line.Length == 0)
            return;
        if (ArenaMaps.TryCommand(line, out string reply))
        {
            foreach (string row in reply.Split('\n'))
                Post("Debug", row);
            return;
        }
        string speaker = GameSession.Current.PlayerName;
        Post(speaker, line);
    }
}
