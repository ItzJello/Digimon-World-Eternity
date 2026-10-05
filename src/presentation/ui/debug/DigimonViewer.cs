using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace DigimonWorldEternity;

/// <summary>
/// Debug viewer for exported GLBs. Digimon and NPCs are separate lists.
/// Pick a model, pick a clip, and it loops.
/// </summary>
public partial class DigimonViewer : Node3D
{
    private readonly record struct ModelEntry(string Slug, string Label, string Path, bool Npc);

    private readonly List<ModelEntry> _digimon = new();
    private readonly List<ModelEntry> _npcs = new();
    private ItemList _models = null!;
    private ItemList _clips = null!;
    private LineEdit _filter = null!;
    private Label _now = null!;
    private Button _digimonTab = null!;
    private Button _npcTab = null!;
    private Camera3D _camera = null!;
    private ActorVisual? _body;
    private string _slug = "";
    private bool _showingNpcs;
    private Vector3 _look = new(0, 0.8f, 0);
    private float _distance = 3.6f;
    private float _yaw;
    private bool _dragging;
    private bool _fillingModels;
    private bool _fillingClips;

    public override void _Ready()
    {
        StageLight.Add(this);
        AddChild(new MeshInstance3D
        {
            Name = "Floor",
            Mesh = new PlaneMesh { Size = new Vector2(16, 16) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.14f, 0.16f, 0.18f),
                Roughness = 1f,
            },
        });

        _camera = new Camera3D { Current = true, Fov = 40 };
        AddChild(_camera);
        Aim();

        Scan(RepoPaths.DigimonDir, npc: false, _digimon);
        Scan(RepoPaths.CharactersDir, npc: true, _npcs);
        _slug = _digimon.Exists(entry => entry.Slug == "agumon")
            ? "agumon"
            : _digimon.Count > 0 ? _digimon[0].Slug : "";

        BuildUi();
        PaintTabs();
        FillModels();
        if (_slug.Length > 0)
            ShowModel(_slug);
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left)
                _dragging = button.Pressed;
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
                _distance = Mathf.Max(0.8f, _distance - 0.3f);
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
                _distance = Mathf.Min(28f, _distance + 0.3f);
            else
                return;
            Aim();
            return;
        }

        if (_dragging && ev is InputEventMouseMotion motion && _body != null)
        {
            _yaw -= motion.Relative.X * 0.01f;
            _body.Rotation = new Vector3(0, _yaw, 0);
        }
    }

    private static void Scan(string dir, bool npc, List<ModelEntry> into)
    {
        if (!Directory.Exists(dir))
            return;
        foreach (string file in Directory.GetFiles(dir, "*.glb"))
        {
            string slug = Path.GetFileNameWithoutExtension(file);
            if (npc && slug.StartsWith("pc", StringComparison.OrdinalIgnoreCase))
                continue;
            into.Add(new ModelEntry(slug, Pretty(slug), file, npc));
        }
        into.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
    }

    private static string Pretty(string slug)
    {
        string[] words = slug.Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            if (word.Equals("publicsafety", StringComparison.OrdinalIgnoreCase))
                words[i] = "Public Safety";
            else if (word.Equals("8yearslater", StringComparison.OrdinalIgnoreCase))
                words[i] = "8 Years Later";
            else if (word.Length == 1)
                words[i] = word.ToUpperInvariant();
            else
                words[i] = char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
        }
        return string.Join(' ', words);
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        Button back = UiChrome.Button("Back", () =>
            GameSession.Current.Travel(GameSession.StartScene, "Title"));
        back.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        back.OffsetLeft = 24;
        back.OffsetTop = 20;
        back.OffsetRight = 220;
        back.OffsetBottom = 72;
        root.AddChild(back);

        _now = new Label
        {
            Text = "Drag to turn. Wheel to zoom.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _now.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _now.OffsetLeft = -420;
        _now.OffsetTop = 28;
        _now.OffsetRight = 420;
        _now.OffsetBottom = 64;
        _now.AddThemeFontSizeOverride("font_size", 22);
        _now.AddThemeColorOverride("font_color", Colors.White);
        _now.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.04f, 0.08f));
        _now.AddThemeConstantOverride("outline_size", 8);
        root.AddChild(_now);

        var models = Side(root, left: true);
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        _digimonTab = Tab("Digimon", () => SetMode(false));
        _npcTab = Tab("NPC", () => SetMode(true));
        tabs.AddChild(_digimonTab);
        tabs.AddChild(_npcTab);
        models.AddChild(tabs);

        _filter = new LineEdit { PlaceholderText = "Find a Digimon" };
        _filter.AddThemeFontSizeOverride("font_size", 16);
        _filter.AddThemeColorOverride("font_color", Colors.White);
        _filter.AddThemeColorOverride("font_placeholder_color", new Color(0.7f, 0.76f, 0.84f));
        StyleBoxTexture field = UiChrome.ButtonBox();
        _filter.AddThemeStyleboxOverride("normal", field);
        _filter.AddThemeStyleboxOverride("focus", field);
        _filter.TextChanged += _ => FillModels();
        models.AddChild(_filter);

        _models = List();
        _models.ItemSelected += index =>
        {
            if (_fillingModels)
                return;
            string slug = SlugAt(_models, (int)index);
            if (slug == _slug && _body != null)
                return;
            _slug = slug;
            ShowModel(slug);
        };
        models.AddChild(_models);

        var clips = Side(root, left: false);
        var heading = new Label { Text = "Animation" };
        heading.AddThemeFontSizeOverride("font_size", 18);
        heading.AddThemeColorOverride("font_color", Colors.White);
        clips.AddChild(heading);
        _clips = List();
        _clips.ItemSelected += index =>
        {
            if (_fillingClips)
                return;
            PlayNamed(ClipAt((int)index));
        };
        clips.AddChild(_clips);
    }

    private Button Tab(string text, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 44),
        };
        button.AddThemeFontSizeOverride("font_size", 18);
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.Pressed += pressed;
        return button;
    }

    private void SetMode(bool npcs)
    {
        if (_showingNpcs == npcs)
            return;
        _showingNpcs = npcs;
        _filter.Text = "";
        _filter.PlaceholderText = npcs ? "Find an NPC" : "Find a Digimon";
        PaintTabs();
        List<ModelEntry> list = Active();
        _slug = list.Count > 0 ? list[0].Slug : "";
        FillModels();
        if (_slug.Length > 0)
            ShowModel(_slug);
        else
        {
            _body?.QueueFree();
            _body = null;
            _now.Text = npcs ? "No NPCs exported" : "No Digimon exported";
            FillClips();
        }
    }

    private void PaintTabs()
    {
        PaintTab(_digimonTab, !_showingNpcs);
        PaintTab(_npcTab, _showingNpcs);
    }

    private static void PaintTab(Button button, bool on)
    {
        StyleBoxTexture box = UiChrome.ButtonBox();
        box.ModulateColor = on ? new Color(1.35f, 1.2f, 0.75f) : new Color(0.72f, 0.76f, 0.82f);
        button.AddThemeStyleboxOverride("normal", box);
        button.AddThemeStyleboxOverride("hover", box);
        button.AddThemeStyleboxOverride("pressed", box);
    }

    private static VBoxContainer Side(Control root, bool left)
    {
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(left ? Control.LayoutPreset.LeftWide : Control.LayoutPreset.RightWide);
        panel.OffsetTop = 96;
        panel.OffsetBottom = -24;
        if (left)
        {
            panel.OffsetLeft = 24;
            panel.OffsetRight = 460;
        }
        else
        {
            panel.OffsetLeft = -500;
            panel.OffsetRight = -24;
        }
        panel.AddThemeStyleboxOverride("panel", UiChrome.PanelBox());
        root.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        column.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        panel.AddChild(column);
        return column;
    }

    private static ItemList List()
    {
        var list = new ItemList
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
        };
        list.AddThemeFontSizeOverride("font_size", 18);
        list.AddThemeColorOverride("font_color", Colors.White);
        list.AddThemeColorOverride("font_hovered_color", new Color(1f, 0.94f, 0.7f));
        list.AddThemeColorOverride("font_selected_color", new Color(1f, 0.94f, 0.7f));
        var panel = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.05f, 0.08f, 0.55f),
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        var selected = new StyleBoxFlat
        {
            BgColor = new Color(0.45f, 0.32f, 0.08f, 0.9f),
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 4,
            ContentMarginBottom = 4,
        };
        list.AddThemeStyleboxOverride("panel", panel);
        list.AddThemeStyleboxOverride("selected", selected);
        list.AddThemeStyleboxOverride("hovered", selected);
        return list;
    }

    private List<ModelEntry> Active() => _showingNpcs ? _npcs : _digimon;

    private void FillModels()
    {
        _fillingModels = true;
        _models.Clear();
        string needle = _filter.Text.Trim();
        int select = -1;
        foreach (ModelEntry entry in Active())
        {
            if (needle.Length > 0
                && entry.Label.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0
                && entry.Slug.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            int row = _models.ItemCount;
            _models.AddItem(entry.Label);
            _models.SetItemMetadata(row, entry.Slug);
            if (entry.Slug == _slug)
                select = row;
        }
        if (select >= 0)
        {
            _models.Select(select);
            _models.EnsureCurrentIsVisible();
        }
        _fillingModels = false;
    }

    private void ShowModel(string slug)
    {
        _body?.QueueFree();
        _body = null;
        _yaw = 0f;
        ModelEntry? entry = Find(slug);
        if (entry == null)
        {
            _now.Text = slug;
            FillClips();
            return;
        }

        try
        {
            _body = ActorVisual.Spawn(entry.Value.Path, entry.Value.Label);
        }
        catch (Exception err)
        {
            _now.Text = $"{entry.Value.Label} failed to load";
            GD.PrintErr(err.Message);
            FillClips();
            return;
        }

        EyeBinder.Apply(_body, RepoPaths.Eye(slug + ".png"), clipAlpha: !entry.Value.Npc);
        float scale = entry.Value.Npc
            ? 1f
            : PartnerAvatar.ScaleFor(slug);
        _body.Scale = Vector3.One * scale;
        AddChild(_body);
        Frame(_body);
        FillClips();
    }

    private ModelEntry? Find(string slug)
    {
        foreach (ModelEntry entry in Active())
        {
            if (entry.Slug == slug)
                return entry;
        }
        return null;
    }

    private void FillClips()
    {
        _fillingClips = true;
        _clips.Clear();
        string[] names = _body?.ClipNames() ?? Array.Empty<string>();
        Array.Sort(names, StringComparer.Ordinal);
        int pick = PreferredClip(names, _showingNpcs);
        for (int i = 0; i < names.Length; i++)
        {
            _clips.AddItem(ClipLegend.Caption(_slug, names[i], _showingNpcs));
            _clips.SetItemMetadata(i, names[i]);
        }
        if (names.Length > 0)
        {
            _clips.Select(pick);
            _clips.EnsureCurrentIsVisible();
        }
        _fillingClips = false;
        if (names.Length > 0)
            PlayNamed(names[pick]);
        else
            _now.Text = Find(_slug)?.Label ?? _slug;
    }

    private static int PreferredClip(string[] names, bool npc)
    {
        int idle = -1;
        int walk = -1;
        int pose = 0;
        for (int i = 0; i < names.Length; i++)
        {
            string code = ClipLegend.Code(names[i]);
            if (npc && (code is "fn01" or "fn01_01"))
                idle = i;
            else if (npc && walk < 0 && (code is "fw01" or "fw01_01"))
                walk = i;
            else if (!npc && code == "bs01")
                pose = i;
        }
        if (idle >= 0)
            return idle;
        if (walk >= 0)
            return walk;
        return pose;
    }

    private void PlayNamed(string clip)
    {
        _body?.PlayClipLoop(clip);
        string label = Find(_slug)?.Label ?? _slug;
        _now.Text = $"{label}    {ClipLegend.Caption(_slug, clip, _showingNpcs)}    looping";
    }

    private string ClipAt(int index)
    {
        return SlugAt(_clips, index);
    }

    private static string SlugAt(ItemList list, int index)
    {
        Variant meta = list.GetItemMetadata(index);
        return meta.VariantType == Variant.Type.String ? meta.AsString() : list.GetItemText(index);
    }

    private void Frame(Node3D body)
    {
        Aabb box = new();
        bool any = false;
        foreach (MeshInstance3D mesh in MeshQuery.Find(body))
        {
            if (mesh.Mesh == null)
                continue;
            Aabb world = mesh.GlobalTransform * mesh.Mesh.GetAabb();
            box = any ? box.Merge(world) : world;
            any = true;
        }
        if (!any)
            return;
        _look = box.GetCenter();
        float size = Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z));
        _distance = Mathf.Clamp(size * 1.45f, 1.2f, 24f);
        Aim();
    }

    private void Aim()
    {
        _camera.Position = _look + new Vector3(0, _distance * 0.16f, _distance);
        _camera.LookAt(_look, Vector3.Up);
    }
}
