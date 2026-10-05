using Godot;
using System.Collections.Generic;

namespace DigimonWorldEternity;

/// <summary>
/// Solid city collision. Every mesh except the sky and outline shells blocks movement.
/// </summary>
public partial class WalkSurface : Node3D
{
    public List<Vector3> Samples { get; } = new();

    public void Build(Node3D city)
    {
        int count = 0;
        foreach (MeshInstance3D mesh in MeshQuery.Find(city))
        {
            if (!mesh.Visible)
                continue;
            string name = mesh.Name.ToString().ToLowerInvariant();
            if (name.Contains("sky") || name.Contains("outline") || name.Contains("shadow"))
                continue;
            Shape3D? shape = mesh.Mesh?.CreateTrimeshShape();
            if (shape == null)
                continue;
            var body = new StaticBody3D
            {
                Transform = mesh.GlobalTransform,
                CollisionLayer = 1,
                CollisionMask = 1,
            };
            body.AddChild(new CollisionShape3D { Shape = shape });
            AddChild(body);
            if (name.Contains("sidewalk") || name.Contains("ground") || name.Contains("stair"))
                Samples.Add(mesh.GlobalTransform * mesh.GetAabb().GetCenter());
            count++;
        }
        GD.Print($"collision shapes {count}");
    }

    public bool TryStand(Vector3 xz, float nearY, bool anyHeight, out Vector3 grounded)
    {
        grounded = new Vector3(xz.X, nearY, xz.Z);
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null)
            return false;

        float up = anyHeight ? 30.0f : 1.1f;
        float down = anyHeight ? 40.0f : 1.6f;
        var query = PhysicsRayQueryParameters3D.Create(
            new Vector3(xz.X, nearY + up, xz.Z),
            new Vector3(xz.X, nearY - down, xz.Z));
        query.CollisionMask = 1;
        var hit = space.IntersectRay(query);
        if (hit.Count == 0)
            return false;

        var point = (Vector3)hit["position"];
        if (!anyHeight && point.Y > nearY + 0.45f)
            return false;
        grounded = point;
        return true;
    }
}
