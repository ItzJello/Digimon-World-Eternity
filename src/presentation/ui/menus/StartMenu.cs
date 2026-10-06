using Godot;
using System.IO;

namespace DigimonWorldEternity;

/// <summary>
/// Title screen. The background is the key art. Create shows the male or
/// female body inside a Time Stranger frame, then the chosen outfit.
/// </summary>
public partial class StartMenu : Control
{
    private Control _home = null!;
    private Control _create = null!;
    private Control _load = null!;
    private Control _menuColumn = null!;
    private LineEdit _name = null!;
    private Label _createStatus = null!;
    private Label _lookLabel = null!;
    private Label _outfitLabel = null!;
    private int _model = 1;
    private string _outfit = "a";

    private SubViewport? _preview;
    private SubViewportContainer? _previewBox;
    private Node3D? _previewRoot;
    private Camera3D? _previewCamera;
    private ActorVisual? _previewBody;
    private readonly System.Collections.Generic.Dictionary<string, Node> _previewTemplates = new();
    private Node _previewHold = null!;

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

        _menuColumn = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _menuColumn.SetAnchorsPreset(LayoutPreset.CenterRight);
        _menuColumn.OffsetLeft = -460;
        _menuColumn.OffsetTop = -230;
        _menuColumn.OffsetRight = -48;
        _menuColumn.OffsetBottom = 290;
        _menuColumn.AddThemeConstantOverride("separation", 12);
        AddChild(_menuColumn);

        _home = Page(_menuColumn);
        _home.AddChild(UiChrome.Button("Create Character", ShowCreate));
        _home.AddChild(UiChrome.Button("Load Character", ShowLoad));
        _home.AddChild(UiChrome.Button("Arena debug", () => GameSession.Current.DebugBout()));
        _home.AddChild(UiChrome.Button("Model viewer", () => GameSession.Current.DebugViewer()));
        _home.AddChild(UiChrome.Button("Quit", () => GetTree().Quit()));

        _load = Page(_menuColumn);
        _load.Visible = false;

        _create = new Control { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _create.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_create);
        _previewHold = new Node { Name = "PreviewCache" };
        AddChild(_previewHold);
        BuildCreate();
        PlayMenuMusic();
        OpenStartupMap();
    }

    /// <summary>
    /// <c>-- --walk t0101f [start_00]</c> on the command line skips the title and
    /// drops the player onto that walk map. Debug only.
    /// </summary>
    private void OpenStartupMap()
    {
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--walk")
                continue;
            string stem = args[i + 1];
            string start = i + 2 < args.Length && !args[i + 2].StartsWith("--") ? args[i + 2] : "";
            Callable.From(() => GameSession.Current.OpenWalkMap(stem, start)).CallDeferred();
            return;
        }
    }

    private void BuildCreate()
    {
        var column = new VBoxContainer();
        column.SetAnchorsPreset(LayoutPreset.CenterRight);
        column.OffsetLeft = -440;
        column.OffsetTop = -140;
        column.OffsetRight = -40;
        column.OffsetBottom = 220;
        column.AddThemeConstantOverride("separation", 12);
        _create.AddChild(column);

        _name = new LineEdit
        {
            PlaceholderText = "Character name",
            MaxLength = 16,
            CustomMinimumSize = new Vector2(0, 48),
        };
        _name.AddThemeFontSizeOverride("font_size", 18);
        _name.AddThemeColorOverride("font_color", Colors.White);
        _name.AddThemeColorOverride("font_placeholder_color", new Color(0.75f, 0.8f, 0.86f));
        var field = UiChrome.ButtonBox();
        _name.AddThemeStyleboxOverride("normal", field);
        _name.AddThemeStyleboxOverride("focus", field);
        column.AddChild(_name);

        _createStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _createStatus.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.7f));
        _createStatus.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.04f, 0.08f));
        _createStatus.AddThemeConstantOverride("outline_size", 6);
        column.AddChild(_createStatus);
        column.AddChild(UiChrome.Button("Start", ConfirmCreate));
        column.AddChild(UiChrome.Button("Back", ShowHome));

        var stage = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stage.AddThemeConstantOverride("separation", 8);
        stage.SetAnchorsPreset(LayoutPreset.Center);
        stage.OffsetLeft = -520;
        stage.OffsetTop = -420;
        stage.OffsetRight = 160;
        stage.OffsetBottom = 420;
        _create.AddChild(stage);

        _lookLabel = Caption();
        stage.AddChild(_lookLabel);

        var genderRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        genderRow.AddThemeConstantOverride("separation", 4);
        stage.AddChild(genderRow);

        genderRow.AddChild(ArrowButton("arrow_left.png", () => ChooseBody(1), 88));

        var slot = new Control
        {
            CustomMinimumSize = new Vector2(440, 704),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        genderRow.AddChild(slot);

        var framed = new Control { MouseFilter = MouseFilterEnum.Ignore };
        framed.SetAnchorsPreset(LayoutPreset.FullRect);
        framed.OffsetTop = -10;
        framed.OffsetBottom = -10;
        slot.AddChild(framed);

        var frame = new NinePatchRect
        {
            Texture = UiChrome.FrameTexture,
            PatchMarginLeft = 22,
            PatchMarginTop = 22,
            PatchMarginRight = 22,
            PatchMarginBottom = 22,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        frame.SetAnchorsPreset(LayoutPreset.FullRect);
        framed.AddChild(frame);

        _preview = new SubViewport
        {
            Name = "Preview",
            Size = new Vector2I(400, 668),
            TransparentBg = false,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            HandleInputLocally = false,
        };
        _previewBox = new SubViewportContainer
        {
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _previewBox.SetAnchorsPreset(LayoutPreset.FullRect);
        _previewBox.AddChild(_preview);

        var canvas = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        canvas.SetAnchorsPreset(LayoutPreset.FullRect);
        canvas.OffsetLeft = 16;
        canvas.OffsetTop = 16;
        canvas.OffsetRight = -16;
        canvas.OffsetBottom = -16;
        canvas.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.07f, 0.12f),
            BorderColor = new Color(0.93f, 0.72f, 0.28f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 2,
            ContentMarginTop = 2,
            ContentMarginRight = 2,
            ContentMarginBottom = 2,
        });
        canvas.AddChild(_previewBox);
        framed.AddChild(canvas);

        _previewRoot = new Node3D();
        _preview.AddChild(_previewRoot);
        _previewRoot.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.04f, 0.07f, 0.12f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.85f, 0.88f, 0.95f),
                AmbientLightEnergy = 1.2f,
            },
        });
        _previewRoot.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-28, -36, 0),
            LightEnergy = 1.6f,
            ShadowEnabled = false,
        });
        _previewRoot.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-10, 150, 0),
            LightEnergy = 0.45f,
            ShadowEnabled = false,
        });
        _previewCamera = new Camera3D
        {
            Position = new Vector3(0, 0.83f, 2.62f),
            Fov = 38,
            Current = true,
        };
        _previewCamera.LookAt(new Vector3(0, 0.77f, 0), Vector3.Up);
        _previewRoot.AddChild(_previewCamera);

        genderRow.AddChild(ArrowButton("arrow_right.png", () => ChooseBody(2), 88));

        var outfitRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        outfitRow.AddThemeConstantOverride("separation", 8);
        stage.AddChild(outfitRow);
        outfitRow.AddChild(ArrowButton("arrow_left.png", () => StepOutfit(-1), 56));
        _outfitLabel = Caption();
        _outfitLabel.CustomMinimumSize = new Vector2(140, 0);
        outfitRow.AddChild(_outfitLabel);
        outfitRow.AddChild(ArrowButton("arrow_right.png", () => StepOutfit(1), 56));
    }

    private static Label Caption()
    {
        var label = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 20);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.05f, 0.1f));
        label.AddThemeConstantOverride("outline_size", 8);
        return label;
    }

    private void PlayMenuMusic()
    {
        string path = RepoPaths.Music("Digimon_World_Next_Order_OST_21_-_Digital_Grit_ne0_Version.wav");
        AudioStreamWav? music = WavMusic.Load(path);
        if (music == null)
        {
            GD.PrintErr($"menu music failed to load: {path}");
            return;
        }
        var player = new AudioStreamPlayer
        {
            Name = "MenuMusic",
            Stream = music,
            VolumeDb = -6.0f,
        };
        AddChild(player);
        player.Play();
    }

    private void ShowHome()
    {
        ReleasePreview();
        _create.Visible = false;
        _load.Visible = false;
        _home.Visible = true;
        _menuColumn.Visible = true;
    }

    private void ShowCreate()
    {
        _home.Visible = false;
        _load.Visible = false;
        _menuColumn.Visible = false;
        _create.Visible = true;
        _createStatus.Text = "";
        _name.GrabFocus();
        ChooseBody(1);
    }

    private void ShowLoad()
    {
        ReleasePreview();
        _create.Visible = false;
        _home.Visible = false;
        _menuColumn.Visible = true;
        _load.Visible = true;
        foreach (Node child in _load.GetChildren())
        {
            _load.RemoveChild(child);
            child.QueueFree();
        }
        System.Collections.Generic.List<SavedCharacter> saved = CharacterLibrary.LoadAll();
        if (saved.Count == 0)
        {
            var empty = new Label { Text = "No characters saved yet." };
            empty.AddThemeColorOverride("font_color", Colors.White);
            empty.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.05f, 0.1f));
            empty.AddThemeConstantOverride("outline_size", 6);
            _load.AddChild(empty);
        }
        foreach (SavedCharacter record in saved)
        {
            SavedCharacter chosen = record;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            Button load = UiChrome.Button($"{chosen.Name}   {chosen.BodyLabel}", () =>
                EnterLobby(chosen.Name, chosen.Model, chosen.Outfit));
            load.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(load);
            var trash = new TextureButton
            {
                TextureNormal = UiChrome.Load("trash.png"),
                CustomMinimumSize = new Vector2(48, 48),
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                TooltipText = "Delete",
            };
            trash.Pressed += () =>
            {
                CharacterLibrary.Delete(chosen.Name);
                ShowLoad();
            };
            row.AddChild(trash);
            _load.AddChild(row);
        }
        _load.AddChild(UiChrome.Button("Back", ShowHome));
    }

    private void ChooseBody(int model)
    {
        _model = model == 2 ? 2 : 1;
        string[] outfits = CharacterRoster.Outfits(_model);
        if (outfits.Length == 0)
            _outfit = "a";
        else if (System.Array.IndexOf(outfits, _outfit) < 0)
            _outfit = outfits[0];
        ShowPreview();
    }

    private void StepOutfit(int direction)
    {
        string[] outfits = CharacterRoster.Outfits(_model);
        if (outfits.Length == 0)
            return;
        int index = System.Array.IndexOf(outfits, _outfit);
        if (index < 0)
            index = 0;
        index = (index + direction) % outfits.Length;
        if (index < 0)
            index += outfits.Length;
        _outfit = outfits[index];
        ShowPreview();
    }

    private void ShowPreview()
    {
        if (_previewRoot == null || _preview == null)
            return;
        _previewBody?.QueueFree();
        _previewBody = null;
        string path = CharacterRoster.PreviewPath(_model, _outfit);
        if (!File.Exists(path))
        {
            _createStatus.Text = "That outfit file is missing.";
            GD.PrintErr($"preview missing {path}");
            return;
        }
        _previewBody = ActorVisual.Adopt(PreviewModel(path), "Body");
        FaceEyes.Apply(_previewBody);
        _previewRoot.AddChild(_previewBody);
        _previewCamera?.MakeCurrent();
        _preview.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        if (_lookLabel != null)
            _lookLabel.Text = _model == 2 ? "Female" : "Male";
        if (_outfitLabel != null)
            _outfitLabel.Text = $"Outfit {_outfit.ToUpperInvariant()}";
        GD.Print($"preview {_lookLabel?.Text} {_outfit} {path}");
    }

    private void ReleasePreview()
    {
        _previewBody?.QueueFree();
        _previewBody = null;
        if (_preview != null)
            _preview.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    }

    private void ConfirmCreate()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0)
        {
            _createStatus.Text = "Enter a name.";
            return;
        }
        if (CharacterLibrary.Exists(name))
        {
            _createStatus.Text = "That name is already saved.";
            return;
        }
        Error saved = CharacterLibrary.Save(name, _model, _outfit);
        if (saved != Error.Ok)
        {
            _createStatus.Text = "Could not save the character.";
            return;
        }
        EnterLobby(name, _model, _outfit);
    }

    private void EnterLobby(string name, int model, string outfit)
    {
        ReleasePreview();
        GameSession.Current.UseCharacter(name, model, outfit);
        GameSession.Current.Travel(GameSession.LobbyScene, "Entering File City");
    }

    private Node PreviewModel(string path)
    {
        if (!_previewTemplates.TryGetValue(path, out Node? template))
        {
            template = GlbLoader.Load(path, "Model");
            template.ProcessMode = ProcessModeEnum.Disabled;
            _previewHold.AddChild(template);
            _previewTemplates[path] = template;
        }
        Node copy = template.Duplicate();
        copy.ProcessMode = ProcessModeEnum.Inherit;
        return copy;
    }

    private TextureButton ArrowButton(string fileName, global::System.Action pressed, int size)
    {
        var button = new TextureButton
        {
            TextureNormal = UiChrome.Load(fileName),
            CustomMinimumSize = new Vector2(size, size),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
        };
        button.Pressed += pressed;
        return button;
    }

    private static VBoxContainer Page(Control parent)
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 12);
        parent.AddChild(page);
        return page;
    }
}
