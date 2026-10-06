using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DigimonWorldEternity;

/// <summary>
/// The Time Stranger wiring that sits next to a walk map: its own collision
/// geom, start points, exits into neighbouring fields, invisible borders, and
/// the sun. Read from res://data/maps/{stem}_wiring.json.
/// </summary>
public sealed class MapWiring
{
    /// <summary>Walls and floor the player collides with.</summary>
    public const uint WalkLayer = 1;
    /// <summary>Volumes only the camera respects.</summary>
    public const uint CameraLayer = 4;

    public string Code { get; private set; } = "";
    public Dictionary<string, Start> Starts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Exit> Exits { get; } = new();
    public List<Box> Borders { get; } = new();
    public Vector3? SunFrom { get; private set; }
    public bool HasCollision => _collision != null && !string.IsNullOrEmpty(_collision.Glb);

    private CollisionFile? _collision;

    public static MapWiring? Load(string stem)
    {
        string path = ProjectSettings.GlobalizePath($"res://data/maps/{stem}_wiring.json");
        if (!File.Exists(path))
            return null;
        WiringFile? file = JsonSerializer.Deserialize<WiringFile>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (file == null)
            return null;
        var wiring = new MapWiring { Code = file.Code ?? "", _collision = file.Collision };
        foreach ((string name, StartFile start) in file.Starts)
        {
            if (start.At is { Length: 3 } at)
                wiring.Starts[name] = new Start(new Vector3(at[0], at[1], at[2]), Mathf.DegToRad(start.Yaw));
        }
        foreach (BoxFile box in file.Exits)
        {
            if (Box.From(box) is { } shape && !string.IsNullOrEmpty(box.To))
                wiring.Exits.Add(new Exit(shape, box.To, box.Start ?? ""));
        }
        foreach (BoxFile box in file.Borders)
        {
            if (Box.From(box) is { } shape)
                wiring.Borders.Add(shape);
        }
        if (file.Sun?.From is { Length: 3 } from)
            wiring.SunFrom = new Vector3(from[0], from[1], from[2]);
        return wiring;
    }

    /// <summary>
    /// Spawn the field's own collision under <paramref name="parent"/>. Floor,
    /// walls, and the move bound block the player. Camera volumes only block
    /// the camera. Returns false when the collision GLB is not on disk.
    /// </summary>
    public bool BuildCollision(Node3D parent, WalkSurface ground)
    {
        if (_collision == null || string.IsNullOrEmpty(_collision.Glb))
            return false;
        string path = Path.Combine(RepoPaths.MapsDir, "Collision", _collision.Glb + ".glb");
        if (!File.Exists(path))
        {
            GD.PushWarning($"Collision GLB missing: {path}");
            return false;
        }

        Node3D shell = GlbLoader.Load(path, _collision.Glb);
        parent.AddChild(shell);
        int walk = 0;
        int camera = 0;
        foreach (MeshInstance3D mesh in MeshQuery.Find(shell))
        {
            string role = RoleOf(mesh.Name.ToString());
            Shape3D? shape = mesh.Mesh?.CreateTrimeshShape();
            if (shape == null)
                continue;
            bool cameraOnly = role.Contains("cam");
            var body = new StaticBody3D
            {
                Name = role,
                Transform = mesh.GlobalTransform,
                CollisionLayer = cameraOnly ? CameraLayer : WalkLayer | CameraLayer,
                CollisionMask = 0,
            };
            body.AddChild(new CollisionShape3D { Shape = shape });
            ground.AddChild(body);
            if (cameraOnly)
                camera++;
            else
            {
                walk++;
                if (!role.Contains("wall") && !role.Contains("move"))
                    ground.Samples.Add(mesh.GlobalTransform * mesh.GetAabb().GetCenter());
            }
        }
        shell.QueueFree();

        foreach (Box border in Borders)
        {
            // Borders are a line in the data. Give them a wall's height.
            Vector3 size = new(Mathf.Max(border.Size.X, 0.3f), Mathf.Max(border.Size.Y, 4.0f), Mathf.Max(border.Size.Z, 0.3f));
            var body = new StaticBody3D
            {
                Name = "mapborder",
                CollisionLayer = WalkLayer,
                CollisionMask = 0,
            };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            ground.AddChild(body);
            body.GlobalTransform = border.Transform;
        }
        GD.Print($"field collision {walk} walk, {camera} camera, {Borders.Count} borders");
        return walk > 0;
    }

    /// <summary>
    /// Exit volumes. <paramref name="onExit"/> gets the field code and start
    /// name when the player steps in.
    /// </summary>
    public void PlaceExits(Node3D parent, Action<string, string> onExit)
    {
        foreach (Exit exit in Exits)
        {
            var area = new Area3D
            {
                Name = $"exit_{exit.To}_{exit.Start}",
                CollisionLayer = 0,
                CollisionMask = 2,
                Monitoring = true,
            };
            Vector3 size = new(Mathf.Max(exit.Box.Size.X, 0.5f), Mathf.Max(exit.Box.Size.Y, 2.0f), Mathf.Max(exit.Box.Size.Z, 0.5f));
            area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            parent.AddChild(area);
            area.GlobalTransform = exit.Box.Transform;
            string to = exit.To;
            string start = exit.Start;
            area.BodyEntered += body =>
            {
                if (body is PlayerWalker)
                    onExit(to, start);
            };
        }
    }

    /// <summary>
    /// A start point sits just outside the border wall and faces into the map.
    /// Step through that wall so the player is not boxed out on arrival.
    /// </summary>
    public Vector3 StepInside(Vector3 at, float yaw)
    {
        Vector3 forward = new(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
        if (forward.LengthSquared() < 0.01f)
            return at;
        // Past the wall face, clear of the player's capsule.
        const float clearance = 1.5f;
        foreach (Box border in Borders)
        {
            Vector3 half = new Vector3(
                Mathf.Max(border.Size.X, 0.3f),
                Mathf.Max(border.Size.Y, 4.0f),
                Mathf.Max(border.Size.Z, 0.3f)) * 0.5f;
            Transform3D inverse = border.Transform.AffineInverse();
            Vector3 local = inverse * at;
            Vector3 localForward = inverse.Basis * forward;
            if (Mathf.Abs(local.X) > half.X + 1.0f || Mathf.Abs(local.Y) > half.Y + 2.0f)
                continue;
            if (Mathf.Abs(localForward.Z) < 0.2f)
                continue;
            float inside = Mathf.Sign(localForward.Z) * (half.Z + clearance);
            bool alreadyIn = Mathf.Abs(local.Z) > half.Z
                && Mathf.Sign(local.Z) == Mathf.Sign(localForward.Z)
                && Mathf.Abs(local.Z) >= Mathf.Abs(inside);
            if (alreadyIn)
                continue;
            float travel = (inside - local.Z) / localForward.Z;
            if (travel <= 0.05f || travel > 8.0f)
                continue;
            at += forward * travel;
        }
        return at;
    }

    /// <summary>The start to use for this visit, or null when the map has none.</summary>
    public Start? PickStart(string wanted)
    {
        if (!string.IsNullOrEmpty(wanted) && Starts.TryGetValue(wanted, out Start chosen))
            return chosen;
        if (Starts.TryGetValue("start_00", out Start first))
            return first;
        foreach (Start any in Starts.Values)
            return any;
        return null;
    }

    private string RoleOf(string meshName)
    {
        // Collision meshes come out of the GLB as mesh_N, in the nlst order.
        string name = meshName;
        int at = name.LastIndexOf('@');
        if (at > 0)
            name = name[..at];
        List<string>? names = _collision?.Meshes;
        if (name.StartsWith("mesh_", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(5), out int index))
        {
            return names != null && index < names.Count
                ? names[index].ToLowerInvariant()
                : "col";
        }
        return name.ToLowerInvariant();
    }

    public readonly record struct Start(Vector3 At, float Yaw);

    public sealed record Exit(Box Box, string To, string Start);

    public sealed class Box
    {
        public Vector3 At { get; init; }
        public Quaternion Rotation { get; init; } = Quaternion.Identity;
        public Vector3 Size { get; init; }

        public Transform3D Transform => new(new Basis(Rotation), At);

        public static Box? From(BoxFile file)
        {
            if (file.At is not { Length: 3 } at || file.Size is not { Length: 3 } size)
                return null;
            var rotation = file.Rotation is { Length: 4 } r
                ? new Quaternion(r[0], r[1], r[2], r[3]).Normalized()
                : Quaternion.Identity;
            return new Box
            {
                At = new Vector3(at[0], at[1], at[2]),
                Rotation = rotation,
                Size = new Vector3(size[0], size[1], size[2]),
            };
        }
    }

    private sealed class WiringFile
    {
        public string? Code { get; set; }
        public CollisionFile? Collision { get; set; }
        public Dictionary<string, StartFile> Starts { get; set; } = new();
        public List<BoxFile> Exits { get; set; } = new();
        public List<BoxFile> Borders { get; set; } = new();
        public SunFile? Sun { get; set; }
    }

    private sealed class CollisionFile
    {
        public string? Glb { get; set; }
        public List<string> Meshes { get; set; } = new();
    }

    private sealed class StartFile
    {
        public float[]? At { get; set; }
        public float Yaw { get; set; }
    }

    public sealed class BoxFile
    {
        public float[]? At { get; set; }
        public float[]? Rotation { get; set; }
        public float[]? Size { get; set; }
        public string? To { get; set; }
        public string? Start { get; set; }
    }

    private sealed class SunFile
    {
        public float[]? From { get; set; }
    }
}
