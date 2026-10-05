using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// High three-quarter view. W moves toward the top of the screen.
/// </summary>
public partial class TopDownCamera : Node3D, IWalkHeading
{
    public Node3D? Target { get; set; }

    private Camera3D _camera = null!;

    public override void _Ready()
    {
        _camera = new Camera3D { Current = true };
        AddChild(_camera);
    }

    public override void _Process(double delta)
    {
        if (Target == null)
            return;
        Vector3 focus = Target.GlobalPosition;
        _camera.GlobalPosition = focus + new Vector3(0, 14.0f, 10.0f);
        _camera.LookAt(focus, Vector3.Up);
    }

    public Vector3 FlatForward()
    {
        return new Vector3(0, 0, 1);
    }
}
