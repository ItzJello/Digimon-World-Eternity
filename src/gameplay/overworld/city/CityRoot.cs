using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace DigimonWorldEternity;

public partial class CityRoot : Node3D
{
    private WalkSurface _ground = null!;
    private PlayerWalker _walker = null!;
    private OrbitCamera _camera = null!;
    private DialogueView _dialogue = null!;
    private EscapeMenu _menu = null!;
    private LobbyChat _chat = null!;
    private PartnerFollow _partner = null!;
    private Label _hint = null!;
    private MapWiring? _wiring;
    private ulong _spawnedAt;
    private bool _leaving;
    private string _notice = "";
    private double _noticeLeft;
    private bool _failed;

    public override void _Ready()
    {
        StageLight.Add(this);
        string stem = GameSession.Current.WalkMap;
        if (string.IsNullOrEmpty(stem))
            stem = WalkMaps.Plaza;
        Node3D city;
        try
        {
            city = GlbLoader.Load(RepoPaths.Map(stem + ".glb"), stem);
        }
        catch (System.Exception error)
        {
            // A bad name or a broken file must not take the game down.
            GD.PushWarning($"Walk map {stem} did not load: {error.Message}");
            _failed = true;
            CallDeferred(MethodName.LeaveBrokenMap);
            return;
        }
        AddChild(city);
        string? albedo = AlbedoFor(stem);
        if (albedo != null)
            CityPainter.Apply(city, Path.Combine(RepoPaths.MapsDir, "Textures"), albedo, stem);
        HideClutter(city);
        // Borders go on after the texture pass so untextured shells can hide
        // without taking the street edges with them.
        MapCollision.Place(city, stem);
        _wiring = MapWiring.Load(stem);
        if (_wiring?.SunFrom is { } sunFrom)
            StageLight.AimSun(this, sunFrom);

        _ground = new WalkSurface();
        AddChild(_ground);
        // Time Stranger's own collision geom when it was exported. Otherwise
        // fall back to the visual shells and the border list.
        bool wired = _wiring != null && _wiring.BuildCollision(this, _ground);
        if (!wired)
        {
            IReadOnlySet<string>? solid = MapCollision.Solids(stem);
            if (solid != null)
                _ground.BuildConfigured(city, solid);
            else
                _ground.Build(city);
        }


        _camera = new OrbitCamera();
        AddChild(_camera);
        _camera.Configure(5.6f, -0.3f, 50.0f);
        _camera.ClipMask = MapWiring.WalkLayer | MapWiring.CameraLayer;

        _walker = new PlayerWalker();
        AddChild(_walker);
        ActorVisual player = ActorVisual.Spawn(RepoPaths.CharacterGlb("pc001a_city.glb"), "Player");
        _walker.Attach(player, _camera);
        _camera.Target = _walker;

        _partner = new PartnerFollow { Leader = _walker, Ground = _ground };
        AddChild(_partner);
        RefreshPartner();
        GameSession.Current.PartnerChanged += RefreshPartner;

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
        if (_failed)
            return;
        if (_noticeLeft > 0)
            _noticeLeft -= delta;
        _hint.Text = _chat.IsTyping
            ? "Enter  Send    Esc  Close chat"
            : _noticeLeft > 0
                ? _notice
                : GameSession.Current.Fly
                    ? "Fly    WASD move    Space up    Ctrl down    Shift fast    Esc menu"
                    : "WASD walk    Shift sprint    Enter chat    /gm <map>    Esc menu";
        bool locked = _dialogue.IsOpen || _menu.IsOpen || _chat.IsTyping;
        _walker.Frozen = locked;
        _camera.Frozen = locked;
    }

    private void LeaveBrokenMap()
    {
        if (GameSession.Current.WalkMap != WalkMaps.Plaza && File.Exists(RepoPaths.Map(WalkMaps.Plaza + ".glb")))
            GameSession.Current.OpenWalkMap(WalkMaps.Plaza);
        else
            GameSession.Current.ReturnToLobby();
    }

    private async void PlaceActors()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Vector3 grounded = new Vector3(0, 1, 0);
        float yaw = 0;
        bool placed = false;

        // Time Stranger's start point for this visit, when the map has one.
        MapWiring.Start? start = _wiring?.PickStart(GameSession.Current.WalkStart);
        if (start is { } entry)
        {
            yaw = entry.Yaw;
            Vector3 at = _wiring!.StepInside(entry.At, yaw);
            placed = _ground.TryStand(at, at.Y, false, out grounded)
                || _ground.TryStand(at, at.Y, true, out grounded);
            if (!placed)
            {
                GD.PushWarning($"No floor under start point {at}");
                grounded = at;
                placed = true;
            }
            GD.Print($"spawn {GameSession.Current.WalkStart} at {grounded} yaw {Mathf.RadToDeg(yaw):0}");
        }
        if (!placed)
        {
            foreach (Vector3 sample in _ground.Samples)
            {
                if (_ground.TryStand(sample, sample.Y, false, out grounded))
                {
                    placed = true;
                    break;
                }
            }
        }
        if (!placed)
            _ground.TryStand(Vector3.Zero, 1.0f, true, out grounded);

        _walker.GlobalPosition = grounded + new Vector3(0, 0.05f, 0);
        _walker.Rotation = new Vector3(0, yaw, 0);
        _camera.SetHeading(yaw);
        _spawnedAt = Time.GetTicksMsec();
        if (GameSession.Current.LobbyLink.Equals(GameSession.Current.WalkMap, StringComparison.OrdinalIgnoreCase))
        {
            GameSession.Current.LobbyLink = "";
            PlaceLobbyReturn(grounded, yaw);
        }

        _wiring?.PlaceExits(this, OnExit);
    }

    /// <summary>The edge the player just stepped through. Walking back returns to the park.</summary>
    private void PlaceLobbyReturn(Vector3 at, float yaw)
    {
        Vector3 back = new Vector3(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw));
        var area = new Area3D
        {
            Name = "return_lobby",
            Position = at + back * 3.0f + new Vector3(0, 1.2f, 0),
            CollisionLayer = 0,
            CollisionMask = 2,
            Monitoring = true,
        };
        area.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(8, 3, 4) },
        });
        AddChild(area);
        area.BodyEntered += body =>
        {
            if (body is not PlayerWalker || _leaving || Time.GetTicksMsec() - _spawnedAt < 1500)
                return;
            _leaving = true;
            Callable.From(() => GameSession.Current.ReturnToLobby()).CallDeferred();
        };
    }

    private void OnExit(string code, string start)
    {
        // The start point can sit inside its own exit box. Do not bounce straight back.
        if (_leaving || Time.GetTicksMsec() - _spawnedAt < 1500)
            return;
        if (WalkMaps.IsLobbyCode(code))
        {
            _leaving = true;
            GameSession.Current.ReturnToLobby();
            return;
        }
        string? stem = WalkMaps.StemForCode(code);
        if (stem == null)
        {
            _notice = $"{code} is not exported yet";
            _noticeLeft = 3.0;
            return;
        }
        _leaving = true;
        GameSession.Current.OpenWalkMap(stem, start);
    }

    private void RefreshPartner()
    {
        _partner.SetBody(PartnerAvatar.SpawnCurrent());
    }

    private static string? AlbedoFor(string stem)
    {
        string generated = $"res://data/maps/{stem}_albedo.json";
        if (File.Exists(ProjectSettings.GlobalizePath(generated)))
            return generated;
        if (stem is WalkMaps.Plaza or "t0101f")
        {
            const string plaza = "res://data/maps/higashi_albedo.json";
            if (File.Exists(ProjectSettings.GlobalizePath(plaza)))
                return plaza;
        }
        return null;
    }


    private static void HideClutter(Node3D city)
    {
        List<MeshInstance3D> meshes = MeshQuery.Find(city);
        var afternoon = new Dictionary<string, MeshInstance3D>();
        var night = new List<(string Key, MeshInstance3D Mesh)>();
        foreach (MeshInstance3D mesh in meshes)
        {
            string name = mesh.Name.ToString();
            int at = name.LastIndexOf('@');
            if (at > 0)
                name = name[..at];
            string low = name.ToLowerInvariant();
            if (low.Contains("shadow") || low.Contains("outline"))
            {
                mesh.Visible = false;
                continue;
            }
            // The sky picker decides which dome stays. A B_ sky is the dome, not a night copy.
            if (IsSky(low))
                continue;
            if (low.StartsWith("a_") && !afternoon.ContainsKey(low[2..]))
                afternoon[low[2..]] = mesh;
            else if (low.StartsWith("b_"))
                night.Add((low[2..], mesh));
        }

        int hidden = 0;
        foreach ((string key, MeshInstance3D mesh) in night)
        {
            if (!afternoon.TryGetValue(key, out MeshInstance3D? day))
                continue;
            if (!SameBox(day, mesh))
                continue;
            mesh.Visible = false;
            hidden++;
        }
        if (hidden > 0)
            GD.Print($"hid stacked night copies {hidden}");
        HideUnusedSkies(meshes);
    }

    /// <summary>
    /// A field ships one sky per time of day, stacked. Keep the daytime dome
    /// and the clouds that belong to it.
    /// </summary>
    private static void HideUnusedSkies(List<MeshInstance3D> meshes)
    {
        var bases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clouds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MeshInstance3D mesh in meshes)
        {
            string name = Bare(mesh.Name.ToString());
            if (!IsSky(name))
                continue;
            string prefix = SkyPrefix(name);
            if (name.Contains("cloud"))
                clouds.Add(prefix);
            else
                bases.Add(prefix);
        }
        var pool = bases.Count > 0 ? bases : clouds;
        string chosen = "";
        foreach (string prefix in DaySky)
        {
            if (pool.Contains(prefix))
            {
                chosen = prefix;
                break;
            }
        }
        if (chosen.Length == 0)
        {
            foreach (string prefix in pool)
            {
                chosen = prefix;
                break;
            }
        }
        string cloudPrefix = clouds.Contains(chosen) ? chosen : clouds.Contains("b") ? "b" : chosen;
        foreach (MeshInstance3D mesh in meshes)
        {
            string name = Bare(mesh.Name.ToString());
            if (!IsSky(name))
                continue;
            string prefix = SkyPrefix(name);
            bool keep = name.Contains("cloud") ? prefix == cloudPrefix : prefix == chosen;
            if (!keep)
                mesh.Visible = false;
        }
    }

    private static readonly string[] DaySky =
    {
        "aa", "a", "ca", "c", "ba", "b", "da", "d", "dm", "sky",
    };

    private static bool IsSky(string name) => name.Contains("sky") || name.Contains("cloud");

    private static string SkyPrefix(string name)
    {
        int split = name.IndexOf('_');
        return split > 0 ? name[..split] : name;
    }

    private static string Bare(string name)
    {
        int at = name.LastIndexOf('@');
        return (at > 0 ? name[..at] : name).ToLowerInvariant();
    }

    private static bool SameBox(MeshInstance3D day, MeshInstance3D night)
    {
        Aabb left = day.GlobalTransform * day.GetAabb();
        Aabb right = night.GlobalTransform * night.GetAabb();
        return left.Position.DistanceTo(right.Position) < 0.35f
            && left.Size.DistanceTo(right.Size) < 0.35f;
    }
}
