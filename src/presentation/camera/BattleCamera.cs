using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// The same three battle shots as the C arena camera: a high three-quarter
/// view, then over each Digimon's shoulder. It cycles on its own.
/// </summary>
public partial class BattleCamera : Camera3D
{
    private enum Shot
    {
        Overview,
        SideA,
        SideB,
    }

    private struct Pose
    {
        public Vector3 Eye;
        public Vector3 Target;
        public float Fov;

        public static Pose Blend(Pose a, Pose b, float t)
        {
            return new Pose
            {
                Eye = a.Eye.Lerp(b.Eye, t),
                Target = a.Target.Lerp(b.Target, t),
                Fov = Mathf.Lerp(a.Fov, b.Fov, t),
            };
        }
    }

    private const float HoldSeconds = 2.8f;
    private const float BlendSeconds = 0.45f;

    public Node3D? Left { get; set; }
    public Node3D? Right { get; set; }
    public bool Frozen { get; set; }

    private Shot _shot = Shot.Overview;
    private Shot _pending = Shot.Overview;
    private Pose _from;
    private Pose _to;
    private Pose _current;
    private float _blend;
    private float _holdUntil = HoldSeconds;
    private float _now;
    private bool _placed;

    public override void _Ready()
    {
        Current = true;
        Fov = 40f;
        GlobalPosition = new Vector3(0f, 5.2f, 9f);
        LookAt(new Vector3(0f, 0.8f, 0f), Vector3.Up);
    }

    public override void _Process(double delta)
    {
        if (Left == null || Right == null)
            return;

        float dt = (float)delta;
        if (!_placed)
        {
            _current = Compute(Shot.Overview);
            _shot = Shot.Overview;
            _placed = true;
            _holdUntil = HoldSeconds;
            Apply(_current);
            return;
        }

        if (Frozen)
        {
            Apply(_current);
            return;
        }

        _now += dt;
        if (_blend <= 0f)
        {
            Pose goal = Compute(_shot);
            float follow = 1f - Mathf.Exp(-12f * dt);
            _current = Pose.Blend(_current, goal, follow);
            if (_now >= _holdUntil)
            {
                _from = _current;
                _pending = Next(_shot);
                _to = Compute(_pending);
                _blend = 0.001f;
            }
        }
        else
        {
            _to = Compute(_pending);
            float t = Mathf.Clamp(_blend, 0f, 1f);
            float u = t * t * (3f - 2f * t);
            _current = Pose.Blend(_from, _to, u);
            _blend += dt / BlendSeconds;
            if (_blend >= 1f)
            {
                _blend = 0f;
                _shot = _pending;
                _current = _to;
                _holdUntil = _now + HoldSeconds;
            }
        }

        Apply(_current);
    }

    private static Shot Next(Shot shot)
    {
        return shot switch
        {
            Shot.Overview => Shot.SideA,
            Shot.SideA => Shot.SideB,
            _ => Shot.Overview,
        };
    }

    private Pose Compute(Shot shot)
    {
        Vector3 a = Left!.GlobalPosition;
        Vector3 b = Right!.GlobalPosition;
        Vector3 mid = (a + b) * 0.5f;
        Vector3 axis = b - a;
        axis.Y = 0f;
        float separation = axis.Length();
        if (separation < 0.4f)
            axis = Vector3.Right;
        else
            axis /= separation;
        separation = Mathf.Clamp(separation, 1.6f, 14f);

        if (shot == Shot.Overview)
        {
            float yaw = 0.62f;
            float c = Mathf.Cos(yaw);
            float s = Mathf.Sin(yaw);
            var back = new Vector3(-(axis.X * c - axis.Z * s), 0f, -(axis.Z * c + axis.X * s));
            float dist = 6.6f + separation * 0.36f;
            float eyeY = 3.8f + separation * 0.12f;
            return new Pose
            {
                Eye = mid + back * dist + Vector3.Up * eyeY,
                Target = mid + Vector3.Up * 0.75f,
                Fov = 44f,
            };
        }

        bool sideA = shot == Shot.SideA;
        Vector3 self = sideA ? a : b;
        Vector3 other = sideA ? b : a;
        Vector3 forward = sideA ? axis : -axis;
        float backDist = 3.7f + separation * 0.22f;
        float side = 1.45f;
        float height = 2.25f + separation * 0.06f;
        var shoulder = new Vector3(-forward.Z, 0f, forward.X);
        return new Pose
        {
            Eye = self - forward * backDist + shoulder * side + Vector3.Up * height,
            Target = self.Lerp(other, 0.65f) + Vector3.Up * 0.7f,
            Fov = 40f,
        };
    }

    private void Apply(Pose pose)
    {
        // Shoulder shots sit behind a fighter. If that point is past the rocks,
        // the eye falls into the mesh and the frame goes black. Stay inside
        // the bowl and rise instead.
        Vector3 eye = pose.Eye;
        float radius = Mathf.Sqrt(eye.X * eye.X + eye.Z * eye.Z);
        const float limit = 5.6f;
        if (radius > limit && radius > 0.001f)
        {
            float pull = limit / radius;
            float lift = (radius - limit) * 0.55f;
            eye = new Vector3(eye.X * pull, eye.Y + lift, eye.Z * pull);
        }
        GlobalPosition = eye;
        Fov = pose.Fov;
        Vector3 to = pose.Target - pose.Eye;
        if (Mathf.Abs(to.Normalized().Dot(Vector3.Up)) > 0.98f)
            pose.Target += Vector3.Forward * 0.25f;
        LookAt(pose.Target, Vector3.Up);
    }
}
