using System;

namespace DigimonWorldEternity;

/// <summary>
/// Floor movement on the X/Z plane. Combatants ease toward the range their
/// command wants, arc a little while crossing open ground, and stay inside
/// the ring.
/// </summary>
public static class BattleSteering
{
    /// <summary>
    /// Ease toward the committed range. The stop band sits inside the
    /// technique's reach, so a basic can actually connect.
    /// </summary>
    public static void Steer(Combatant self, Combatant other, float desired, float radius, float dt, Random rng)
    {
        float distance = self.DistanceTo(other);
        float error = distance - desired;
        bool chasing = self.Command == BattleCommand.Attack && error > 0.6f;
        float maxSpeed = 2.7f + self.Kit.Speed * 0.008f;
        if (chasing)
            maxSpeed += 1.3f;
        if (self.Command == BattleCommand.Defend)
            maxSpeed *= 0.4f;
        // Wide enough to avoid jitter, narrow enough that a melee preferred
        // of 0.8 still stops inside the 1.075 strike range.
        const float dead = 0.14f;
        float gain = chasing ? 2.2f : 1.4f;

        float radial = 0f;
        if (error > dead)
            radial = Math.Min(maxSpeed, (error - dead) * gain);
        else if (error < -dead)
            radial = -Math.Min(maxSpeed, (-error - dead) * gain);

        // A small arc while crossing open ground. Inside the range band they
        // hold still and swing, instead of sliding past each other.
        float circle = 0f;
        float outside = Math.Abs(error) - dead;
        if (outside > 0f)
        {
            float open = Math.Clamp(outside / 4f, 0f, 1f);
            circle = (0.4f + self.Kit.Speed * 0.003f) * open;
            if (self.Command == BattleCommand.Defend)
                circle *= 0.25f;
            if (chasing)
                circle *= 0.45f;
        }

        self.StrafeTimer -= dt;
        if (self.StrafeTimer <= 0f)
        {
            self.Strafe = -self.Strafe;
            self.StrafeTimer = 9f + rng.NextSingle() * 4f;
        }

        float dx = other.X - self.X;
        float dz = other.Z - self.Z;
        float len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < 0.001f)
        {
            dx = 1f;
            dz = 0f;
            len = 1f;
        }
        dx /= len;
        dz /= len;

        float wantX = dx * radial + -dz * self.Strafe * circle;
        float wantZ = dz * radial + dx * self.Strafe * circle;

        float radiusNow = MathF.Sqrt(self.X * self.X + self.Z * self.Z);
        if (radiusNow > radius - 0.4f && radiusNow > 0.001f)
        {
            float outward = (wantX * self.X + wantZ * self.Z) / radiusNow;
            if (outward > 0f)
            {
                wantX -= self.X / radiusNow * outward;
                wantZ -= self.Z / radiusNow * outward;
                float slide = 1.5f * self.Strafe;
                wantX += -self.Z / radiusNow * slide;
                wantZ += self.X / radiusNow * slide;
            }
        }

        float accel = (chasing ? 9f : 5.5f) * dt;
        float vx = wantX - self.VelX;
        float vz = wantZ - self.VelZ;
        float vlen = MathF.Sqrt(vx * vx + vz * vz);
        if (vlen > accel)
        {
            vx = vx / vlen * accel;
            vz = vz / vlen * accel;
        }
        self.VelX += vx;
        self.VelZ += vz;
        self.X += self.VelX * dt;
        self.Z += self.VelZ * dt;
        Clamp(self, radius);

        float r = MathF.Sqrt(self.X * self.X + self.Z * self.Z);
        if (r > radius - 0.05f && r > 0.001f)
        {
            float outward = (self.VelX * self.X + self.VelZ * self.Z) / r;
            if (outward > 0f)
            {
                self.VelX -= self.X / r * outward;
                self.VelZ -= self.Z / r * outward;
            }
        }
    }

    /// <summary>Bleed velocity while recovering from a swing.</summary>
    public static void Brake(Combatant self, float rate, float dt)
    {
        self.VelX = MoveToward(self.VelX, 0f, rate * dt);
        self.VelZ = MoveToward(self.VelZ, 0f, rate * dt);
    }

    public static void Stop(Combatant self)
    {
        self.VelX = 0f;
        self.VelZ = 0f;
    }

    /// <summary>Shove the victim straight away from the attacker.</summary>
    public static void Knockback(Combatant victim, Combatant attacker, float amount, float radius)
    {
        float ax = victim.X - attacker.X;
        float az = victim.Z - attacker.Z;
        float len = MathF.Sqrt(ax * ax + az * az);
        if (len * len < 0.01f)
        {
            ax = 1f;
            az = 0f;
            len = 1f;
        }
        victim.X += ax / len * amount;
        victim.Z += az / len * amount;
        Clamp(victim, radius);
    }

    public static void Clamp(Combatant self, float radius)
    {
        float r = MathF.Sqrt(self.X * self.X + self.Z * self.Z);
        if (r > radius)
        {
            float k = radius / r;
            self.X *= k;
            self.Z *= k;
        }
    }

    /// <summary>Keep two bodies from overlapping. Both give way equally.</summary>
    public static void Separate(Combatant a, Combatant b, float contact, float radius)
    {
        float dx = b.X - a.X;
        float dz = b.Z - a.Z;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance >= contact)
            return;
        if (distance < 0.05f)
        {
            dx = 1f;
            dz = 0f;
            distance = 1f;
        }
        float push = (contact - distance) * 0.5f;
        a.X -= dx / distance * push;
        a.Z -= dz / distance * push;
        b.X += dx / distance * push;
        b.Z += dz / distance * push;
        Clamp(a, radius);
        Clamp(b, radius);
    }

    private static float MoveToward(float from, float to, float delta)
    {
        float gap = to - from;
        if (Math.Abs(gap) <= delta)
            return to;
        return from + MathF.Sign(gap) * delta;
    }
}
