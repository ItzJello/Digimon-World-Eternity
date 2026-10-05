using System;
using System.Collections.Generic;
using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// Arena match scene. Loads the field, places the battle camera, runs the
/// bout, and lets the trainer pick a command from the HUD. The field can be
/// stepped from the HUD while the match is a debug session.
/// </summary>
public partial class ArenaMatch : Node3D
{
    private ArenaBout? _bout;
    private BattleCamera _camera = null!;
    private BattleHud _hud = null!;
    private EscapeMenu _menu = null!;
    private LobbyChat _chat = null!;
    private string? _setupError;
    private bool _recorded;
    private Node3D? _map;
    private string _mapStem = "";

    public override void _Ready()
    {
        StageLight.Add(this);
        if (GetNodeOrNull<DirectionalLight3D>("Sun") is DirectionalLight3D sun)
        {
            sun.ShadowBias = 0.1f;
            sun.ShadowNormalBias = 1.1f;
        }
        GameSession.Current.OpenFieldSession();
        LoadMap(GameSession.Current.SessionField);

        _camera = new BattleCamera();
        AddChild(_camera);

        try
        {
            _bout = new ArenaBout();
            AddChild(_bout);
            _camera.Left = _bout.PlayerBody;
            _camera.Right = _bout.EnemyBody;
        }
        catch (Exception ex)
        {
            _setupError = ex.Message;
            GD.PushError($"Arena bout failed: {ex}");
        }

        _chat = new LobbyChat { DockRight = true };
        _chat.Suppressed = () => _menu.IsOpen;
        AddChild(_chat);
        _menu = new EscapeMenu();
        _menu.BeforeOpen = () =>
        {
            if (_chat.IsTyping)
            {
                _chat.CloseInput();
                return true;
            }
            return false;
        };
        AddChild(_menu);

        _hud = new BattleHud
        {
            StepField = CycleMap,
            Leave = LeaveBout,
            ResultShown = RecordResult,
        };
        AddChild(_hud);
        _hud.Build(_bout, ArenaMaps.Caption(_mapStem));
        PlayMusic();
    }

    public override void _Process(double delta)
    {
        if (_bout == null)
        {
            if (_setupError != null)
                _hud.SetMessage(_setupError);
            return;
        }
        bool locked = _menu.IsOpen || _chat.IsTyping;
        _bout.Paused = locked;
        _camera.Frozen = locked;
        _hud.Refresh((float)delta);
    }

    private void LoadMap(string stem)
    {
        if (_map != null && IsInstanceValid(_map))
        {
            _map.QueueFree();
            _map = null;
        }

        _mapStem = string.IsNullOrEmpty(stem) ? "d0172b" : stem;
        Node3D map = ArenaField.Build(_mapStem);
        AddChild(map);
        SilenceShadows(map);
        _map = map;

        if (_hud != null)
            _hud.SetFieldCaption(ArenaMaps.Caption(_mapStem));
        ArenaField.ApplySky(this, _mapStem);
        GameSession.Current.PinField(_mapStem);
        GD.Print($"Arena field {_mapStem}");
    }

    private void CycleMap(int step)
    {
        List<ArenaMaps.Entry> maps = ArenaMaps.Available();
        if (maps.Count == 0)
            return;
        int index = 0;
        for (int i = 0; i < maps.Count; i++)
        {
            if (maps[i].Stem == _mapStem)
            {
                index = i;
                break;
            }
        }
        index = (index + step % maps.Count + maps.Count) % maps.Count;
        LoadMap(maps[index].Stem);
    }

    private static void SilenceShadows(Node node)
    {
        if (node is GeometryInstance3D geo)
            geo.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        foreach (Node child in node.GetChildren())
            SilenceShadows(child);
    }

    private void PlayMusic()
    {
        string path = RepoPaths.Music("Major_Battle.wav");
        AudioStreamWav? music = WavMusic.Load(path);
        if (music == null)
        {
            GD.PrintErr($"battle music failed to load: {path}");
            return;
        }
        var player = new AudioStreamPlayer
        {
            Name = "MajorBattle",
            Stream = music,
            VolumeDb = -4f,
        };
        AddChild(player);
        player.Play();
    }

    private void RecordResult()
    {
        if (_recorded || _bout == null)
            return;
        _recorded = true;
        TournamentBoard? board = GameSession.Current.Board;
        if (board == null)
            return;
        board.Report(_bout.Winner == _bout.Player.Name);
        _hud.SetLeaveLabel(board.Champion || board.PlayerOut ? "See the bracket" : "Back to the bracket");
    }

    private void LeaveBout()
    {
        GameSession session = GameSession.Current;
        if (session.Board != null)
        {
            session.ReturnToBracket();
            return;
        }

        session.CloseFieldSession();
        session.Travel(GameSession.LobbyScene, "Returning to File City");
    }
}
