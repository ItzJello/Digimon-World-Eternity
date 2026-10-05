using Godot;
using System;

namespace DigimonWorldEternity;

/// <summary>
/// Field dialogue. The frame is the Time Stranger message window.
/// </summary>
public partial class DialogueView : CanvasLayer
{
    public bool IsOpen { get; private set; }

    private Control _stack = null!;
    private Label _speaker = null!;
    private Label _body = null!;
    private HBoxContainer _options = null!;
    private Action<int>? _chosen;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 10;

        _stack = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _stack.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _stack.OffsetLeft = 140;
        _stack.OffsetRight = -140;
        _stack.OffsetTop = -250;
        _stack.OffsetBottom = -24;
        AddChild(_stack);

        var frame = new NinePatchRect
        {
            Texture = UiChrome.DialogueTexture,
            PatchMarginLeft = 24,
            PatchMarginTop = 58,
            PatchMarginRight = 24,
            PatchMarginBottom = 24,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _stack.AddChild(frame);

        _speaker = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _speaker.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _speaker.OffsetLeft = 28;
        _speaker.OffsetTop = 10;
        _speaker.OffsetRight = -28;
        _speaker.OffsetBottom = 48;
        _speaker.AddThemeFontSizeOverride("font_size", 20);
        _speaker.AddThemeColorOverride("font_color", new Color(1f, 0.86f, 0.45f));
        _stack.AddChild(_speaker);

        var content = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        content.OffsetLeft = 28;
        content.OffsetTop = 62;
        content.OffsetRight = -24;
        content.OffsetBottom = -18;
        content.AddThemeConstantOverride("separation", 10);
        _stack.AddChild(content);

        _body = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _body.AddThemeFontSizeOverride("font_size", 20);
        _body.AddThemeColorOverride("font_color", new Color(0.93f, 0.95f, 0.98f));
        _body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        content.AddChild(_body);

        _options = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _options.AddThemeConstantOverride("separation", 10);
        content.AddChild(_options);
    }

    public void Open(string speaker, string line, string[] options, Action<int> chosen)
    {
        _chosen = chosen;
        IsOpen = true;
        _stack.Visible = true;
        _speaker.Text = speaker;
        _body.Text = line;
        foreach (Node child in _options.GetChildren())
            child.QueueFree();
        for (int i = 0; i < options.Length; i++)
        {
            int choice = i;
            Button button = UiChrome.Button(options[i], () => Choose(choice));
            button.CustomMinimumSize = new Vector2(180, 44);
            _options.AddChild(button);
        }
    }

    public void Close()
    {
        IsOpen = false;
        _stack.Visible = false;
        _chosen = null;
    }

    private void Choose(int index)
    {
        Action<int>? chosen = _chosen;
        Close();
        chosen?.Invoke(index);
    }

}
