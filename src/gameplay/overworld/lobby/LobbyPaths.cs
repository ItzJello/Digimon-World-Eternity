using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DigimonWorldEternity;

/// <summary>
/// Visible paths out of the park. Each one opens a connected map.
/// Positions live in res://data/maps/lobby_paths.json so they can be moved.
/// </summary>
public static class LobbyPaths
{
    public static void Place(Node3D lobby, WalkSurface ground, Action<string, string, Vector3> onEnter)
    {
        string path = ProjectSettings.GlobalizePath("res://data/maps/lobby_paths.json");
        if (!File.Exists(path))
            return;
        var spots = JsonSerializer.Deserialize<List<PathSpot>>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new List<PathSpot>();
        foreach (PathSpot spot in spots)
        {
            if (string.IsNullOrEmpty(spot.To) || spot.At is not { Length: 3 } at)
                continue;
            var here = new Vector3(at[0], at[1], at[2]);
            if (!ground.TryStand(here, here.Y, true, out Vector3 stood))
            {
                GD.PushWarning($"No floor under lobby path {spot.Name} at {here}");
                continue;
            }
            Vector3 inward = Vector3.Forward;
            if (spot.Inward is { Length: 3 } inn)
                inward = new Vector3(inn[0], 0, inn[2]);
            if (inward.LengthSquared() < 0.01f)
                inward = Vector3.Forward;
            inward = inward.Normalized();

            Mark(lobby, stood, spot.Name);
            Vector3 size = new Vector3(3.2f, 2.4f, 3.2f);
            if (spot.Size is { Length: 3 } sz)
                size = new Vector3(sz[0], sz[1], sz[2]);
            var area = new Area3D
            {
                Name = "path_" + spot.To,
                Position = stood + new Vector3(0, 1.0f, 0),
                CollisionLayer = 0,
                CollisionMask = 2,
                Monitoring = true,
            };
            area.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = size },
            });
            lobby.AddChild(area);
            string destination = spot.To;
            string start = spot.Start ?? "";
            Vector3 back = stood + inward * 4.0f;
            area.BodyEntered += body =>
            {
                if (body is PlayerWalker)
                    onEnter(destination, start, back);
            };
            GD.Print($"lobby path {spot.Name} at {stood} -> {spot.To}");
        }
    }

    private static void Mark(Node3D lobby, Vector3 at, string name)
    {
        var sign = new Label3D
        {
            Text = name,
            FontSize = 72,
            PixelSize = 0.014f,
            Position = at + new Vector3(0, 2.2f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(0.98f, 0.96f, 0.82f),
            OutlineSize = 16,
            OutlineModulate = new Color(0.08f, 0.12f, 0.05f),
        };
        lobby.AddChild(sign);
    }

    private sealed class PathSpot
    {
        public string Name { get; set; } = "";
        public float[]? At { get; set; }
        public string To { get; set; } = "";
        public string Start { get; set; } = "";
        public float[]? Inward { get; set; }
        public float[]? Size { get; set; }
    }
}
