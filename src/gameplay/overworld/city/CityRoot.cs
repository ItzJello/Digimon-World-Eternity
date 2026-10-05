using Godot;
using System.IO;

namespace DigimonWorldEternity;

public partial class CityRoot : Node3D
{
    private WalkSurface _ground = null!;
    private PlayerWalker _walker = null!;
    private OrbitCamera _camera = null!;
    private ArenaClerk _clerk = null!;
    private DialogueView _dialogue = null!;
    private EscapeMenu _menu = null!;
    private PartnerFollow _partner = null!;
    private Label _hint = null!;

    public override void _Ready()
    {
        StageLight.Add(this);
        Node3D city = GlbLoader.Load(
            RepoPaths.Map("HigashiShinjuku_VisionPlaza.glb"),
            "City");
        AddChild(city);
        CityPainter.Apply(city, Path.Combine(RepoPaths.MapsDir, "Textures"), "res://data/maps/higashi_albedo.json");
        HideUntexturedSky(city);

        _ground = new WalkSurface();
        AddChild(_ground);
        _ground.Build(city);

        _camera = new OrbitCamera();
        AddChild(_camera);

        _walker = new PlayerWalker();
        AddChild(_walker);
        ActorVisual player = ActorVisual.Spawn(RepoPaths.CharacterGlb("pc001a_city.glb"), "Player");
        _walker.Attach(player, _camera);
        _camera.Target = _walker;

        _clerk = new ArenaClerk { Name = "ArenaClerk" };
        _clerk.AddChild(ActorVisual.Spawn(RepoPaths.CharacterGlb("kuga.glb"), "Clerk"));
        AddChild(_clerk);

        _partner = new PartnerFollow { Leader = _walker, Ground = _ground };
        AddChild(_partner);
        RefreshPartner();
        GameSession.Current.PartnerChanged += RefreshPartner;

        _dialogue = new DialogueView();
        AddChild(_dialogue);
        _menu = new EscapeMenu();
        _menu.BeforeOpen = () =>
        {
            if (!_dialogue.IsOpen)
                return false;
            _dialogue.Close();
            return true;
        };
        AddChild(_menu);

        var layer = new CanvasLayer();
        AddChild(layer);
        _hint = new Label { Position = new Vector2(16, 12) };
        layer.AddChild(_hint);

        CallDeferred(MethodName.PlaceActors);
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(GameSession.Current))
            GameSession.Current.PartnerChanged -= RefreshPartner;
    }

    public override void _Process(double delta)
    {
        bool near = _clerk.InRangeOf(_walker.GlobalPosition);
        _hint.Text = near
            ? "E  Talk    Esc  Menu"
            : "WASD walk    Shift sprint    Right-drag camera    Esc menu";
        bool locked = _dialogue.IsOpen || _menu.IsOpen;
        _walker.Frozen = locked;
        _camera.Frozen = locked;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;
        if (key.Keycode == Key.E && !_menu.IsOpen && !_dialogue.IsOpen && _clerk.InRangeOf(_walker.GlobalPosition))
        {
            _dialogue.Open(ArenaClerk.Speaker, ArenaClerk.Line, ArenaClerk.Options, OnClerkChoice);
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnClerkChoice(int index)
    {
        if (index != 0)
            return;
        GameSession.Current.EnterArena();
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

        _walker.GlobalPosition = grounded;
        Vector3 clerkAt = grounded + new Vector3(0, 0, 5.5f);
        if (_ground.TryStand(clerkAt, grounded.Y + 2.0f, true, out Vector3 clerkGround))
            clerkAt = clerkGround;
        _clerk.GlobalPosition = clerkAt;
        _clerk.Rotation = new Vector3(0, Mathf.Pi, 0);
    }

    private void RefreshPartner()
    {
        _partner.SetBody(PartnerAvatar.SpawnCurrent());
    }

    private static void HideUntexturedSky(Node3D city)
    {
        foreach (MeshInstance3D mesh in MeshQuery.Find(city))
        {
            if (mesh.Name.ToString().ToLowerInvariant().Contains("sky"))
                mesh.Visible = false;
        }
    }
}
