using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// The screen between the arena master and the floor: a search, a versus
/// card, or the bracket.
/// </summary>
public partial class ArenaCard : Control
{
    private Control _page = null!;

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
            Color = new Color(0.01f, 0.03f, 0.07f, 0.62f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        _page = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_page);

        CardStep step = GameSession.Current.Step;
        if (step == CardStep.Search)
            ShowSearch();
        else if (step == CardStep.Versus)
            ShowVersus();
        else
            ShowBracket();
    }

    private async void ShowSearch()
    {
        Clear();
        var panel = CenterPanel(520, 160);
        panel.AddChild(Line("Searching for an opponent...", 28, new Color(0.95f, 0.97f, 1f)));
        panel.AddChild(Line("The arena is not online yet. A stand-in tamer will answer.", 16, new Color(0.75f, 0.82f, 0.9f)));
        await ToSignal(GetTree().CreateTimer(2.1), SceneTreeTimer.SignalName.Timeout);
        if (!IsInsideTree())
            return;
        GameSession.Current.FinishSearch();
        if (GameSession.Current.Step == CardStep.Versus)
            ShowVersus();
        else
            ShowBracket();
    }

    private async void ShowVersus()
    {
        Clear();
        GameSession session = GameSession.Current;
        session.OpenFieldSession();
        PartnerRecord? mine = PartnerRoster.Find(session.PartnerSlug);
        string leftMon = mine?.DisplayName ?? session.PartnerSlug;
        string rightMon = string.IsNullOrEmpty(session.OpponentDigimon) ? session.OpponentSlug : session.OpponentDigimon;

        var gap = new ColorRect
        {
            Color = new Color(0.01f, 0.02f, 0.05f, 0.94f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        gap.SetAnchorsPreset(LayoutPreset.FullRect);
        _page.AddChild(gap);

        _page.AddChild(Wedge(false, new Color(0.04f, 0.1f, 0.24f)));
        _page.AddChild(Wedge(true, new Color(0.28f, 0.04f, 0.07f)));
        _page.AddChild(Portrait(false, $"mons/{session.PartnerSlug}.png", true));
        _page.AddChild(Portrait(true, $"mons/{session.OpponentSlug}.png", false));
        _page.AddChild(Slash());

        _page.AddChild(NamePlate(false, leftMon, session.PlayerName));
        _page.AddChild(NamePlate(true, rightMon, session.OpponentTrainer));

        var versus = Line("VS", 64, new Color(1f, 0.9f, 0.45f));
        versus.SetAnchorsPreset(LayoutPreset.Center);
        versus.OffsetLeft = -80;
        versus.OffsetTop = -48;
        versus.OffsetRight = 80;
        versus.OffsetBottom = 36;
        versus.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.02f, 0.02f));
        versus.AddThemeConstantOverride("outline_size", 10);
        _page.AddChild(versus);

        var field = Line(ArenaMaps.Caption(session.SessionField), 22, new Color(0.82f, 0.9f, 1f));
        field.SetAnchorsPreset(LayoutPreset.CenterBottom);
        field.OffsetLeft = -240;
        field.OffsetTop = -72;
        field.OffsetRight = 240;
        field.OffsetBottom = -28;
        field.HorizontalAlignment = HorizontalAlignment.Center;
        _page.AddChild(field);

        await ToSignal(GetTree().CreateTimer(4.6), SceneTreeTimer.SignalName.Timeout);
        if (!IsInsideTree())
            return;
        GameSession.Current.StartBout();
    }

    private static Control Wedge(bool right, Color fill)
    {
        var color = new ColorRect
        {
            Color = fill,
            MouseFilter = MouseFilterEnum.Ignore,
            Material = CutMaterial(right, false, fill),
        };
        color.SetAnchorsPreset(LayoutPreset.FullRect);
        return color;
    }

    /// <summary>
    /// Full-body still, kept inside its wedge. The cut shader was cropping
    /// these into a face.
    /// </summary>
    private static TextureRect Portrait(bool right, string file, bool flip)
    {
        var art = new TextureRect
        {
            Texture = UiChrome.Load(file),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            FlipH = flip,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        art.AnchorTop = 0.02f;
        art.AnchorBottom = 0.68f;
        if (right)
        {
            art.AnchorLeft = 0.58f;
            art.AnchorRight = 0.98f;
        }
        else
        {
            art.AnchorLeft = 0.02f;
            art.AnchorRight = 0.42f;
        }
        return art;
    }

    private static ColorRect Slash()
    {
        var slash = new ColorRect
        {
            Color = new Color(0.95f, 0.92f, 1f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        slash.SetAnchorsPreset(LayoutPreset.FullRect);
        var shader = new Shader { Code = SlashShader };
        slash.Material = new ShaderMaterial { Shader = shader };
        return slash;
    }

    private static ShaderMaterial CutMaterial(bool right, bool textured, Color fill)
    {
        var shader = new Shader { Code = CutShader };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("keep_right", right ? 1.0f : 0.0f);
        material.SetShaderParameter("use_texture", textured ? 1.0f : 0.0f);
        material.SetShaderParameter("fill", fill);
        return material;
    }

    private const string CutShader = """
        shader_type canvas_item;
        uniform float top_cut = 0.60;
        uniform float bottom_cut = 0.40;
        uniform float keep_right = 0.0;
        uniform float gap = 0.014;
        uniform vec4 fill : source_color = vec4(1.0);
        uniform float use_texture = 1.0;
        uniform float zoom = 1.5;
        void fragment() {
            vec2 p = FRAGCOORD.xy * SCREEN_PIXEL_SIZE;
            float cut = mix(top_cut, bottom_cut, p.y);
            bool drop = keep_right < 0.5 ? (p.x > cut - gap) : (p.x < cut + gap);
            if (drop)
                discard;
            if (use_texture < 0.5) {
                COLOR = fill;
            } else {
                vec2 uv = (UV - vec2(0.5, 0.46)) * zoom + vec2(0.5);
                if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0)
                    COLOR = vec4(0.0);
                else
                    COLOR = texture(TEXTURE, uv) * COLOR;
            }
        }
        """;

    private const string SlashShader = """
        shader_type canvas_item;
        uniform float top_cut = 0.60;
        uniform float bottom_cut = 0.40;
        uniform float half_width = 0.004;
        void fragment() {
            vec2 p = FRAGCOORD.xy * SCREEN_PIXEL_SIZE;
            float cut = mix(top_cut, bottom_cut, p.y);
            if (abs(p.x - cut) > half_width)
                discard;
            COLOR = vec4(0.96, 0.93, 1.0, 1.0);
        }
        """;

    private static Control NamePlate(bool right, string digimon, string trainer)
    {
        var plate = new TextureRect
        {
            Texture = UiChrome.Load(right ? "plate_red.png" : "plate_blue.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            FlipH = right,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        plate.AnchorLeft = right ? 0.52f : 0.04f;
        plate.AnchorRight = right ? 0.96f : 0.48f;
        plate.AnchorTop = 0.74f;
        plate.AnchorBottom = 0.90f;

        var root = new Control { MouseFilter = MouseFilterEnum.Ignore };
        root.AnchorLeft = right ? 0.50f : 0.02f;
        root.AnchorRight = right ? 0.98f : 0.50f;
        root.AnchorTop = 0.70f;
        root.AnchorBottom = 0.92f;

        plate.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddChild(plate);

        var mon = Line(digimon, 30, Colors.White);
        mon.AnchorLeft = 0.08f;
        mon.AnchorRight = 0.92f;
        mon.AnchorTop = 0.12f;
        mon.AnchorBottom = 0.58f;
        mon.VerticalAlignment = VerticalAlignment.Center;
        mon.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.03f, 0.06f));
        mon.AddThemeConstantOverride("outline_size", 8);
        mon.AutowrapMode = TextServer.AutowrapMode.Off;
        root.AddChild(mon);

        var who = Line(string.IsNullOrEmpty(trainer) ? "Tamer" : trainer, 20, new Color(1f, 0.92f, 0.55f));
        who.AnchorLeft = 0.08f;
        who.AnchorRight = 0.92f;
        who.AnchorTop = 0.52f;
        who.AnchorBottom = 0.90f;
        who.VerticalAlignment = VerticalAlignment.Center;
        who.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.04f, 0.02f));
        who.AddThemeConstantOverride("outline_size", 7);
        who.AutowrapMode = TextServer.AutowrapMode.Off;
        root.AddChild(who);
        return root;
    }

    private void ShowBracket()
    {
        Clear();
        TournamentBoard? board = GameSession.Current.Board;
        if (board == null)
        {
            GameSession.Current.Travel(GameSession.LobbyScene, "Returning to File City");
            return;
        }

        var title = Line(board.Title, 28, new Color(1f, 0.86f, 0.45f));
        title.SetAnchorsPreset(LayoutPreset.CenterTop);
        title.OffsetLeft = -400;
        title.OffsetTop = 28;
        title.OffsetRight = 400;
        title.OffsetBottom = 68;
        _page.AddChild(title);

        string status = board.Champion
            ? "You won the tournament."
            : board.PlayerOut
                ? "You have been eliminated."
                : "Win your match to advance.";
        var note = Line(status, 18, new Color(0.9f, 0.94f, 1f));
        note.SetAnchorsPreset(LayoutPreset.CenterTop);
        note.OffsetLeft = -400;
        note.OffsetTop = 68;
        note.OffsetRight = 400;
        note.OffsetBottom = 98;
        _page.AddChild(note);

        TournamentMatch? next = board.PlayerOut || board.Champion ? null : board.PlayerMatch();
        var tree = new BracketView { MouseFilter = MouseFilterEnum.Ignore };
        tree.SetAnchorsPreset(LayoutPreset.FullRect);
        tree.OffsetLeft = 16;
        tree.OffsetTop = 100;
        tree.OffsetRight = -16;
        tree.OffsetBottom = -88;
        tree.ShowBoard(board, next);
        _page.AddChild(tree);

        if (next != null && next.Left >= 0 && next.Right >= 0)
        {
            int foeId = next.Left == board.PlayerId ? next.Right : next.Left;
            TournamentSlot foe = board.Slot(foeId);
            Button fight = UiChrome.Button($"Fight {foe.Digimon}", () =>
            {
                GameSession.Current.ArmBout(foe.Slug, foe.Trainer, foe.Digimon);
                ShowVersus();
            });
            fight.SetAnchorsPreset(LayoutPreset.CenterBottom);
            fight.OffsetLeft = -280;
            fight.OffsetTop = -72;
            fight.OffsetRight = 20;
            fight.OffsetBottom = -16;
            _page.AddChild(fight);
        }

        Button leave = UiChrome.Button("Return to File City", () =>
            GameSession.Current.Travel(GameSession.LobbyScene, "Returning to File City"));
        leave.SetAnchorsPreset(LayoutPreset.CenterBottom);
        leave.OffsetLeft = next == null ? -160 : 28;
        leave.OffsetTop = -72;
        leave.OffsetRight = next == null ? 160 : 320;
        leave.OffsetBottom = -16;
        _page.AddChild(leave);
    }

    private VBoxContainer CenterPanel(float width, float height)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, height) };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.OffsetLeft = -width / 2f;
        panel.OffsetTop = -height / 2f;
        panel.OffsetRight = width / 2f;
        panel.OffsetBottom = height / 2f;
        panel.AddThemeStyleboxOverride("panel", UiChrome.PanelBox());
        _page.AddChild(panel);
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        return box;
    }

    private static Label Line(string text, int size, Color color)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private void Clear()
    {
        foreach (Node child in _page.GetChildren())
            child.QueueFree();
    }
}
