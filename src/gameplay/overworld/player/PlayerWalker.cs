using Godot;

namespace DigimonWorldEternity;

public partial class PlayerWalker : CharacterBody3D
{
    private const float WalkSpeed = 1.6f;
    private const float RunSpeed = 3.0f;
    private const float Gravity = 28.0f;

    public bool Frozen { get; set; }
    public ActorVisual? Body { get; private set; }

    private IWalkHeading? _camera;
    private Label3D? _nameTag;

    public override void _Ready()
    {
        CollisionLayer = 2;
        CollisionMask = 1;
        FloorSnapLength = 0.3f;
        FloorMaxAngle = Mathf.DegToRad(58.0f);
        FloorStopOnSlope = true;
        var capsule = new CapsuleShape3D { Radius = 0.28f, Height = 1.5f };
        AddChild(new CollisionShape3D
        {
            Shape = capsule,
            Position = new Vector3(0, 0.78f, 0),
        });
    }

    public void Attach(ActorVisual body, IWalkHeading camera)
    {
        Body = body;
        _camera = camera;
        AddChild(body);
        EnsureNameTag();
    }

    private void EnsureNameTag()
    {
        if (_nameTag != null)
            return;
        _nameTag = new Label3D
        {
            Text = GameSession.Current.PlayerName,
            FontSize = 48,
            PixelSize = 0.0045f,
            Position = new Vector3(0, 1.92f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(0.97f, 0.9f, 0.72f),
            OutlineSize = 10,
            OutlineModulate = new Color(0.04f, 0.08f, 0.14f),
        };
        AddChild(_nameTag);
    }

    public void SwapBody(ActorVisual body)
    {
        Body?.QueueFree();
        Attach(body, _camera!);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Body == null || _camera == null)
            return;

        Vector3 velocity = Velocity;
        if (IsOnFloor())
            velocity.Y = 0;
        else
            velocity.Y -= Gravity * (float)delta;

        Vector3 wish = Frozen ? Vector3.Zero : ReadWish();
        if (wish.LengthSquared() > 0.0001f)
        {
            wish = wish.Normalized();
            bool running = Input.IsPhysicalKeyPressed(Key.Shift);
            float speed = running ? RunSpeed : WalkSpeed;
            velocity.X = wish.X * speed;
            velocity.Z = wish.Z * speed;
            Rotation = new Vector3(0, Mathf.Atan2(wish.X, wish.Z), 0);
            Body.SetPace(true, running);
        }
        else
        {
            velocity.X = 0;
            velocity.Z = 0;
            Body.SetWalking(false);
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private Vector3 ReadWish()
    {
        Vector3 forward = _camera!.FlatForward();
        Vector3 right = new Vector3(-forward.Z, 0, forward.X);
        Vector3 wish = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W))
            wish += forward;
        if (Input.IsPhysicalKeyPressed(Key.S))
            wish -= forward;
        if (Input.IsPhysicalKeyPressed(Key.A))
            wish -= right;
        if (Input.IsPhysicalKeyPressed(Key.D))
            wish += right;
        return wish;
    }
}
