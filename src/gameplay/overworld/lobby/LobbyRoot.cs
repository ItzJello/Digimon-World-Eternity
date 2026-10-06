using Godot;
using System.Collections.Generic;
using System.IO;

namespace DigimonWorldEternity;

/// <summary>
/// Shinjuku Park as the lobby. Third-person walk on the park collision,
/// then the gold pad or Enter Arena starts the bout.
/// </summary>
public partial class LobbyRoot : Node3D
{
    private PlayerWalker _walker = null!;
    private PartnerFollow _partner = null!;
    private OrbitCamera _camera = null!;
    private WalkSurface _ground = null!;
    private EscapeMenu _menu = null!;
    private DialogueView _dialogue = null!;
    private LobbyChat _chat = null!;
    private readonly List<LobbyNpc> _npcs = new();
    private Label _hint = null!;
    private Label _fps = null!;
    private double _fpsTimer;
    private int _fpsFrames;
    private double _fpsWorst;
    private Vector3 _gate;
    private ulong _arrived;

    public override void _Ready()
    {
        StageLight.Add(this);
        Node3D park = GlbLoader.Load(
            RepoPaths.Map("ShinjukuPark_Waterfall.glb"),
            "Park");
        AddChild(park);
        ParkProps.Attach(park);
        CityPainter.Apply(park, Path.Combine(RepoPaths.MapsDir, "Textures"), "res://data/maps/park_albedo.json");

        _ground = new WalkSurface();
        AddChild(_ground);
        _ground.Build(park);
        ParkPlants.Attach(park);

        _camera = new OrbitCamera();
        AddChild(_camera);

        _walker = new PlayerWalker();
        AddChild(_walker);
        _walker.Attach(PlayerAvatar.SpawnCurrent(), _camera);
        _camera.Target = _walker;

        _partner = new PartnerFollow { Leader = _walker, Ground = _ground };
        AddChild(_partner);
        PlayMusic();
        RefreshPartner();
        GameSession.Current.PartnerChanged += RefreshPartner;
        GameSession.Current.PlayerChanged += RefreshPlayer;

        _dialogue = new DialogueView();
        AddChild(_dialogue);
        _chat = new LobbyChat();
        _chat.Suppressed = () => _menu.IsOpen || _dialogue.IsOpen;
        AddChild(_chat);
        _menu = new EscapeMenu();
        _menu.BeforeOpen = () =>
        {
            if (_dialogue.IsOpen)
            {
                _dialogue.Close();
                return true;
            }
            if (_chat.IsTyping)
            {
                _chat.CloseInput();
                return true;
            }
            return false;
        };
        AddChild(_menu);
        BuildHud();
        CallDeferred(MethodName.PlaceActors);
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(GameSession.Current))
        {
            GameSession.Current.PartnerChanged -= RefreshPartner;
            GameSession.Current.PlayerChanged -= RefreshPlayer;
        }
    }

    public override void _Process(double delta)
    {
        LobbyNpc? nearby = NearestNpc();
        bool onPad = _walker.GlobalPosition.DistanceTo(_gate) < 1.8f;
        if (_dialogue.IsOpen)
            _hint.Text = "Esc  Close";
        else if (nearby != null && !onPad)
            _hint.Text = $"E  Talk to {nearby.Speaker}";
        else if (_walker.GlobalPosition.DistanceTo(_gate) < 2.6f)
            _hint.Text = "E  Enter the arena";
        else
            _hint.Text = _chat.IsTyping
                ? "Enter  Send    Esc  Close chat"
                : GameSession.Current.Fly
                    ? "Fly    WASD move    Space up    Ctrl down    Shift fast    Esc menu"
                    : "WASD move    Shift run    Enter chat    /gm <map>    E talk    Esc settings";
        _fpsFrames++;
        if (delta > _fpsWorst)
            _fpsWorst = delta;
        _fpsTimer += delta;
        if (_fpsTimer >= 0.5)
        {
            double fps = _fpsFrames / _fpsTimer;
            double avgMs = (_fpsTimer / _fpsFrames) * 1000.0;
            double worstMs = _fpsWorst * 1000.0;
            _fps.Text = $"{fps:0} fps   {avgMs:0} ms   worst {worstMs:0}";
            _fpsFrames = 0;
            _fpsTimer = 0;
            _fpsWorst = 0;
        }
        bool locked = _menu.IsOpen || _dialogue.IsOpen || _chat.IsTyping;
        _walker.Frozen = locked;
        _camera.Frozen = locked;
        _partner.SetProcess(!locked);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_menu.IsOpen || _dialogue.IsOpen || @event is not InputEventKey key || !key.Pressed || key.Echo)
            return;
        if (key.Keycode != Key.E)
            return;
        bool onPad = _walker.GlobalPosition.DistanceTo(_gate) < 1.8f;
        LobbyNpc? nearby = NearestNpc();
        if (nearby != null && !onPad)
        {
            LobbyNpc talking = nearby;
            _dialogue.Open(talking.Speaker, talking.Line, talking.Options, index => OnNpcChoice(talking, index));
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_walker.GlobalPosition.DistanceTo(_gate) < 2.6f)
        {
            GameSession.Current.EnterArena();
            GetViewport().SetInputAsHandled();
        }
    }

    private async void PlaceActors()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Vector3 grounded = new Vector3(0, 1, 0);
        bool placed = false;
        foreach (Vector3 sample in _ground.Samples)
        {
            if (_ground.TryStand(sample, sample.Y, false, out grounded))
            {
                placed = true;
                break;
            }
        }
        if (!placed)
            _ground.TryStand(Vector3.Zero, 1.0f, true, out grounded);

        Vector3 spawn = grounded;
        if (GameSession.Current.HasLobbyReturn)
        {
            Vector3 back = GameSession.Current.LobbyReturn;
            GameSession.Current.ClearLobbyReturn();
            if (_ground.TryStand(back, back.Y, true, out Vector3 returned))
                spawn = returned;
        }

        _walker.GlobalPosition = spawn + new Vector3(0, 0.05f, 0);
        _gate = grounded + new Vector3(0, 0, 6);
        if (_ground.TryStand(_gate, grounded.Y, false, out Vector3 gateGround))
            _gate = gateGround;
        AddGate(_gate);
        PlaceNpcs();
        _arrived = Time.GetTicksMsec();
        LobbyPaths.Place(this, _ground, OnPath);
    }

    private void OnPath(string destination, string start, Vector3 back)
    {
        if (Time.GetTicksMsec() - _arrived < 1500)
            return;
        GameSession.Current.RememberLobbySpot(back);
        GameSession.Current.LobbyLink = destination;
        Callable.From(() => GameSession.Current.OpenWalkMap(destination, start)).CallDeferred();
    }

    private void PlaceNpcs()
    {
        // In front of the restroom, turned to face the door.
        SpawnNpc(
            "toudo.glb",
            "Kenji",
            "The restrooms are open. If you want a match, the gold pad is back toward the square.",
            new[] { "Got it" },
            new Vector3(26.2f, 0.4f, 34.2f),
            new Vector3(26.2f, 0.4f, 38.0f));

        // On the brick in front of the Star machines, turned to face them.
        SpawnNpc(
            "simmons.glb",
            "Ryo",
            "Star vending is stocked. Nothing rare, but it beats walking back into the city.",
            new[] { "Thanks" },
            new Vector3(25.4f, 0.4f, 1.85f),
            new Vector3(29.0f, 0.4f, 1.85f));

        // Middle of the square.
        SpawnNpc(
            "sumeragi.glb",
            "Haru",
            "You're in the middle of the park. Machines are one way, the restrooms the other, and the arena pad is off toward the trees.",
            new[] { "Thanks" },
            new Vector3(2.0f, 0.4f, 14.0f),
            new Vector3(-2.0f, 0.4f, 10.0f));

        // Beside the arena pad and the nearby tree, turned away from the pad.
        Vector3 tree = new Vector3(-20.5f, _gate.Y, 2.92f);
        Vector3 towardTree = tree - _gate;
        towardTree.Y = 0;
        Vector3 beside = _gate + towardTree.Normalized() * 3.1f;
        SpawnNpc(
            "hiroko.glb",
            "Mina",
            "That gold pad is the arena gate. The arena master will ask what kind of match you want.",
            new[] { "Enter the arena", "Not yet" },
            beside,
            beside + (beside - _gate));
    }

    private void SpawnNpc(string file, string speaker, string line, string[] options, Vector3 at, Vector3 lookAt)
    {
        var npc = new LobbyNpc
        {
            Name = speaker,
            Speaker = speaker,
            Line = line,
            Options = options,
        };
        ActorVisual body = ActorVisual.Spawn(RepoPaths.CharacterGlb(file), "Body");
        FaceEyes.Apply(body);
        npc.AddChild(body);

        var block = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
        block.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.28f, Height = 1.45f },
            Position = new Vector3(0, 0.78f, 0),
        });
        npc.AddChild(block);

        var tag = new Label3D
        {
            Text = speaker,
            FontSize = 48,
            PixelSize = 0.0045f,
            Position = new Vector3(0, 1.9f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(0.97f, 0.86f, 0.5f),
            OutlineSize = 10,
            OutlineModulate = new Color(0.04f, 0.08f, 0.14f),
        };
        npc.AddChild(tag);

        AddChild(npc);
        if (!_ground.TryStand(at, 0.6f, false, out Vector3 grounded))
            _ground.TryStand(at, 0.6f, true, out grounded);
        npc.GlobalPosition = grounded;
        Vector3 flat = lookAt - grounded;
        flat.Y = 0;
        if (flat.LengthSquared() > 0.0001f)
            npc.Rotation = new Vector3(0, Mathf.Atan2(-flat.X, flat.Z), 0);
        _npcs.Add(npc);
        GD.Print($"npc {speaker} at {grounded}");
    }

    private LobbyNpc? NearestNpc()
    {
        LobbyNpc? best = null;
        float bestDistance = LobbyNpc.TalkRange;
        foreach (LobbyNpc npc in _npcs)
        {
            float distance = npc.GlobalPosition.DistanceTo(_walker.GlobalPosition);
            if (distance <= bestDistance)
            {
                best = npc;
                bestDistance = distance;
            }
        }
        return best;
    }

    private void OnNpcChoice(LobbyNpc npc, int index)
    {
        if (npc.Speaker == "Mina" && index == 0)
            GameSession.Current.EnterArena();
    }

    private void RefreshPartner()
    {
        string slug = GameSession.Current.PartnerSlug;
        _partner.BodyScale = PartnerAvatar.ScaleFor(slug);
        _partner.SetBody(PartnerAvatar.SpawnCurrent());
    }

    private void RefreshPlayer()
    {
        _walker.SwapBody(PlayerAvatar.SpawnCurrent());
    }

    private void PlayMusic()
    {
        string path = RepoPaths.Music("Digimon World OST - File City (Day).wav");
        AudioStreamWav? music = WavMusic.Load(path);
        if (music == null)
        {
            GD.PrintErr($"music failed to load: {path}");
            return;
        }
        var player = new AudioStreamPlayer
        {
            Name = "FileCityDay",
            Stream = music,
            VolumeDb = -4.0f,
        };
        AddChild(player);
        player.Play();
        GD.Print($"music playing, {music.MixRate} Hz, {music.Data.Length} bytes");
    }

    private void AddGate(Vector3 at)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.85f, 0.62f, 0.18f),
            Roughness = 0.45f,
        };
        var mesh = new MeshInstance3D
        {
            Name = "gate",
            Mesh = new BoxMesh { Size = new Vector3(2.4f, 0.08f, 2.4f) },
            Position = at + new Vector3(0, 0.05f, 0),
        };
        mesh.SetSurfaceOverrideMaterial(0, material);
        AddChild(mesh);
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        _hint = new Label { Position = new Vector2(16, 12) };
        layer.AddChild(_hint);
        _fps = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            OffsetLeft = -320,
            OffsetTop = 12,
            OffsetRight = -16,
            OffsetBottom = 36,
        };
        _fps.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        layer.AddChild(_fps);
    }
}
