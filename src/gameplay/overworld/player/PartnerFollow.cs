using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// Keeps the partner beside the player and plays a walk cycle while catching up.
/// Digimon GLBs are human-sized, so the body is scaled down.
/// </summary>
public partial class PartnerFollow : Node3D
{
    public float BodyScale { get; set; } = PartnerAvatar.AgumonScale;

    public Node3D? Leader { get; set; }
    public WalkSurface? Ground { get; set; }

    private const double SitAfterSeconds = 30.0;

    private ActorVisual? _body;
    private double _still;

    public void SetBody(ActorVisual body)
    {
        _body?.QueueFree();
        _body = body;
        _body.Scale = Vector3.One * BodyScale;
        _still = 0;
        AddChild(_body);
    }

    public override void _Process(double delta)
    {
        if (Leader == null || _body == null)
            return;

        Vector3 side = Leader.GlobalTransform.Basis.X;
        Vector3 behind = -Leader.GlobalTransform.Basis.Z;
        float reach = 0.55f + BodyScale;
        Vector3 goal = Leader.GlobalPosition + side * reach + behind * (reach + 0.35f);
        Vector3 flat = goal - GlobalPosition;
        flat.Y = 0;
        float distance = flat.Length();

        if (distance > 12.0f)
        {
            MoveTo(goal);
            _still = 0;
            _body.SetWalking(false);
            return;
        }

        bool arrived = distance < (_body.IsWalking ? 0.35f : 0.7f);
        if (arrived)
        {
            _body.SetWalking(false);
            _still += delta;
            if (_still >= SitAfterSeconds)
                _body.BeginSit();
            return;
        }

        _still = 0;
        float speed = distance > 2.5f ? 4.5f : 2.2f;
        Vector3 step = flat.Normalized() * Mathf.Min(distance, speed * (float)delta);
        MoveTo(GlobalPosition + step);
        Rotation = new Vector3(0, Mathf.Atan2(step.X, step.Z), 0);
        _body.SetWalking(true);
    }

    private void MoveTo(Vector3 world)
    {
        float reference = Leader != null ? Leader.GlobalPosition.Y : world.Y;
        var probe = new Vector3(world.X, reference, world.Z);
        if (Ground != null && Ground.TryStand(probe, reference, false, out Vector3 grounded))
            probe = grounded;
        GlobalPosition = probe;
    }
}
