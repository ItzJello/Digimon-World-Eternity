using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// Third-person orbit. The camera looks along the same +Z-forward axis the actors use.
/// </summary>
public partial class OrbitCamera : Node3D, IWalkHeading
{
    public Node3D? Target { get; set; }
    public bool Frozen { get; set; }

    private Camera3D _camera = null!;
    private float _yaw;
    private float _pitch = -0.35f;
    private float _distance = 4.5f;

    public override void _Ready()
    {
        _camera = new Camera3D { Current = true };
        AddChild(_camera);
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
        _camera.GlobalPosition = focus - look * _distance;
        _camera.LookAt(focus, Vector3.Up);
    }
}
