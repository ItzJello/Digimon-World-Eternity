using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// Third-person orbit. The camera looks along the same +Z-forward axis the actors use.
/// </summary>
public partial class OrbitCamera : Node3D, IWalkHeading
{
    public Node3D? Target { get; set; }
    public bool Frozen { get; set; }

    /// <summary>
    /// Physics layers the camera may not pass through. Zero leaves the
    /// camera free, which is how the park lobby runs.
    /// </summary>
    public uint ClipMask { get; set; }

    private const float ClipMargin = 0.3f;
    private const float ClipNearest = 0.7f;

    private Camera3D _camera = null!;
    private float _yaw;
    private float _pitch = -0.35f;
    private float _distance = 4.5f;
    private float _clipped = 4.5f;

    public override void _Ready()
    {
        _camera = new Camera3D { Current = true };
        AddChild(_camera);
    }

    /// <summary>Field framing: how far back, how steep, and the lens.</summary>
    public void Configure(float distance, float pitch, float fovDegrees)
    {
        _distance = distance;
        _clipped = distance;
        _pitch = pitch;
        _camera.Fov = fovDegrees;
    }

    /// <summary>Face the camera the way the player faces, like a map entry.</summary>
    public void SetHeading(float yaw)
    {
        _yaw = yaw;
        _clipped = _distance;
        Place();
    }

    public override void _Process(double delta)
    {
        if (Frozen || Target == null)
        {
            Place();
            return;
        }

        float dt = (float)delta;
        if (Input.IsPhysicalKeyPressed(Key.Q))
            _yaw += 1.6f * dt;
        if (Input.IsPhysicalKeyPressed(Key.E))
            _yaw -= 1.6f * dt;
        Place();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Frozen)
            return;
        if (@event is InputEventMouseMotion motion && Input.IsMouseButtonPressed(MouseButton.Right))
        {
            _yaw -= motion.Relative.X * 0.006f;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * 0.004f, -1.05f, -0.05f);
        }
        else if (@event is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.WheelUp)
                _distance = Mathf.Max(2.2f, _distance - 0.6f);
            else if (button.ButtonIndex == MouseButton.WheelDown)
                _distance = Mathf.Min(18.0f, _distance + 0.6f);
            _clipped = Mathf.Min(_clipped, _distance);
        }
    }

    public Vector3 FlatForward()
    {
        return new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw));
    }

    private void Place()
    {
        if (Target == null)
            return;
        Vector3 look = new Vector3(
            Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
            Mathf.Sin(_pitch),
            Mathf.Cos(_yaw) * Mathf.Cos(_pitch));
        Vector3 focus = Target.GlobalPosition + new Vector3(0, 1.35f, 0);
        float distance = ClipMask == 0 ? _distance : Clip(focus, look);
        _camera.GlobalPosition = focus - look * distance;
        _camera.LookAt(focus, Vector3.Up);
    }

    /// <summary>
    /// Pull the camera in front of whatever sits between it and the player.
    /// It snaps in and eases back out so a wall never pops the view.
    /// </summary>
    private float Clip(Vector3 focus, Vector3 look)
    {
        float wanted = _distance;
        var space = GetWorld3D()?.DirectSpaceState;
        if (space != null)
        {
            Vector3 end = focus - look * (_distance + ClipMargin);
            var query = PhysicsRayQueryParameters3D.Create(focus, end);
            query.CollisionMask = ClipMask;
            query.HitBackFaces = true;
            var hit = space.IntersectRay(query);
            if (hit.Count > 0)
            {
                float toWall = focus.DistanceTo((Vector3)hit["position"]);
                wanted = Mathf.Clamp(toWall - ClipMargin, ClipNearest, _distance);
            }
        }
        if (wanted < _clipped)
            _clipped = wanted;
        else
            _clipped = Mathf.MoveToward(_clipped, wanted, (float)GetProcessDeltaTime() * 6.0f);
        return _clipped;
    }
}
