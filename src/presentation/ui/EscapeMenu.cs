using Godot;
using System;

namespace DigimonWorldEternity;

/// <summary>
/// Escape menu. Partner swaps are live. Storage is only the entry point.
/// </summary>
public partial class EscapeMenu : CanvasLayer
{
    public bool IsOpen { get; private set; }

    private Control _root = null!;
    private VBoxContainer _main = null!;
    private VBoxContainer _partners = null!;
    private VBoxContainer _sheet = null!;
    private Label _sheetBody = null!;
    private string _sheetSlug = "agumon";
    private VBoxContainer _storage = null!;
    private VBoxContainer _maps = null!;
    private VBoxContainer _mapList = null!;
    private LineEdit _filter = null!;
    private Button? _returnButton;
    private Button? _flyButton;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 20;
        _root = new Control { Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.OffsetLeft = -250;
        panel.OffsetTop = -300;
        panel.OffsetRight = 250;
        panel.OffsetBottom = 300;
        panel.AddThemeStyleboxOverride("panel", UiChrome.PanelBox());
        _root.AddChild(panel);

        var pages = new Control();
        panel.AddChild(pages);

        _main = BuildPage(pages);
        _main.AddChild(Title("Menu"));
        _main.AddChild(MakeButton("Resume", Close));
        _main.AddChild(MakeButton("Partner", ShowPartners));
        _main.AddChild(MakeButton("Maps", ShowMaps));
        _flyButton = MakeButton("Fly  off", ToggleFly);
        _main.AddChild(_flyButton);
        _main.AddChild(MakeButton("Storage", ShowStorage));
        _returnButton = MakeButton("Return to the lobby", () =>
        {
            Close();
            GameSession.Current.Travel(GameSession.LobbyScene, "Returning to File City");
        });
        _main.AddChild(_returnButton);
        _main.AddChild(MakeButton("Quit", () => GetTree().Quit()));

        _partners = BuildPage(pages);
        _partners.Visible = false;
        _partners.AddChild(Title("Partner"));
        foreach (PartnerRecord record in PartnerRoster.All)
        {
            PartnerRecord chosen = record;
            _partners.AddChild(MakeButton(record.DisplayName, () => ShowSheet(chosen.Slug)));
        }
        _partners.AddChild(MakeButton("Back", ShowMain));

        _sheet = BuildPage(pages);
        _sheet.Visible = false;
        _sheet.AddChild(Title("Partner"));
        _sheetBody = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _sheetBody.AddThemeFontSizeOverride("font_size", 16);
        _sheetBody.AddThemeColorOverride("font_color", new Color(0.9f, 0.94f, 1f));
        _sheet.AddChild(_sheetBody);
        _sheet.AddChild(MakeButton("Bring along", () =>
        {
            GameSession.Current.SetPartner(_sheetSlug);
            ShowPartners();
        }));
        _sheet.AddChild(MakeButton("Back", ShowPartners));

        _storage = BuildPage(pages);
        _storage.Visible = false;
        _storage.AddChild(Title("Storage"));
        var storageNote = new Label
        {
            Text = "Party storage is not set up yet.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        storageNote.AddThemeColorOverride("font_color", new Color(0.86f, 0.9f, 0.95f));
        _storage.AddChild(storageNote);
        _storage.AddChild(MakeButton("Back", ShowMain));

        _maps = BuildPage(pages);
        _maps.Visible = false;
        _maps.AddChild(Title("Maps"));
        _filter = new LineEdit { PlaceholderText = "Filter, such as forest or t0102" };
        _filter.AddThemeColorOverride("font_color", Colors.White);
        _filter.AddThemeColorOverride("font_placeholder_color", new Color(0.7f, 0.78f, 0.86f));
        _filter.TextChanged += _ => FillMaps();
        _maps.AddChild(_filter);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 340),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _maps.AddChild(scroll);
        _mapList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_mapList);
        _maps.AddChild(MakeButton("Back", ShowMain));
    }

    /// <summary>
    /// Return true to swallow Escape, for example while dialogue is open.
    /// </summary>
    public System.Func<bool>? BeforeOpen { get; set; }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.Escape)
            return;
        if (IsOpen)
            Close();
        else if (BeforeOpen != null && BeforeOpen())
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        else
            Open();
        GetViewport().SetInputAsHandled();
    }

    public void Open()
    {
        IsOpen = true;
        _root.Visible = true;
        GetTree().Paused = true;
        bool inCity = GetTree().CurrentScene?.SceneFilePath == GameSession.LobbyScene;
        if (_returnButton != null)
            _returnButton.Visible = !inCity;
        RefreshFly();
        ShowMain();
    }

    public void Close()
    {
        IsOpen = false;
        _root.Visible = false;
        GetTree().Paused = false;
    }

    private void ShowMain()
    {
        _main.Visible = true;
        _partners.Visible = false;
        _sheet.Visible = false;
        _storage.Visible = false;
        _maps.Visible = false;
        MarkCurrentPartner();
        RefreshFly();
    }

    private void ShowMaps()
    {
        _main.Visible = false;
        _partners.Visible = false;
        _sheet.Visible = false;
        _storage.Visible = false;
        _maps.Visible = true;
        FillMaps();
    }

    private void ToggleFly()
    {
        GameSession.Current.Fly = !GameSession.Current.Fly;
        RefreshFly();
    }

    private void RefreshFly()
    {
        if (_flyButton != null)
            _flyButton.Text = GameSession.Current.Fly ? "Fly  on" : "Fly  off";
    }

    private void FillMaps()
    {
        foreach (Node child in _mapList.GetChildren())
            child.QueueFree();
        string needle = _filter.Text.Trim();
        foreach (string stem in WalkMaps.Available())
        {
            string label = MapLabel(stem);
            if (needle.Length > 0
                && !stem.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !label.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;
            string chosen = stem;
            var button = MakeButton(label, () =>
            {
                Close();
                GameSession.Current.OpenWalkMap(chosen);
            });
            button.CustomMinimumSize = new Vector2(280, 40);
            _mapList.AddChild(button);
        }
    }

    private void ShowPartners()
    {
        _main.Visible = false;
        _partners.Visible = true;
        _sheet.Visible = false;
        _storage.Visible = false;
        _maps.Visible = false;
        MarkCurrentPartner();
    }

    private void ShowStorage()
    {
        _main.Visible = false;
        _partners.Visible = false;
        _sheet.Visible = false;
        _storage.Visible = true;
        _maps.Visible = false;
    }

    private void ShowSheet(string slug)
    {
        _sheetSlug = slug;
        PartnerRecord? record = PartnerRoster.Find(slug);
        string name = record?.DisplayName ?? slug;
        _sheetBody.Text = DigimonKit.For(slug).Sheet(name);
        _main.Visible = false;
        _partners.Visible = false;
        _storage.Visible = false;
        _maps.Visible = false;
        _sheet.Visible = true;
        GameSession.Current.SetPartner(slug);
        MarkCurrentPartner();
    }

    private void MarkCurrentPartner()
    {
        string current = GameSession.Current.PartnerSlug;
        foreach (Node child in _partners.GetChildren())
        {
            if (child is not Button button)
                continue;
            PartnerRecord? record = null;
            foreach (PartnerRecord candidate in PartnerRoster.All)
            {
                if (candidate.DisplayName == button.Text || candidate.DisplayName + "  (with you)" == button.Text)
                {
                    record = candidate;
                    break;
                }
            }
            if (record == null)
                continue;
            button.Text = record.Slug == current
                ? record.DisplayName + "  (with you)"
                : record.DisplayName;
        }
    }

    private static string MapLabel(string stem) => stem.ToLowerInvariant() switch
    {
        "t3001f" => "Bar",
        "t3002f" => "Cinema",
        "t3003f" => "VIP room",
        "t3004f" => "Lounge",
        _ => stem,
    };

    private static VBoxContainer BuildPage(Control parent)
    {
        var page = new VBoxContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(page);
        return page;
    }

    private static Label Title(string text)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", new Color(0.9f, 0.95f, 1f));
        return label;
    }

    private static Button MakeButton(string text, global::System.Action pressed)
    {
        return UiChrome.Button(text, pressed);
    }
}
