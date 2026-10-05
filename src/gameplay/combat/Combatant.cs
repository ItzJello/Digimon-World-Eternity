using System;

namespace DigimonWorldEternity;

/// <summary>
/// One side of a bout. Pure simulation state: stats, position on the X/Z
/// floor, the current command, and the action timers. No scene nodes live
/// here, so the same record can be driven by a local AI, a remote player,
/// or a server.
/// </summary>
public sealed class Combatant
{
    public string Name = "";
    public DigimonKit Kit = null!;
    public int Hp;
    public int Mp;
    public int MaxHp;
    public int MaxMp;

    public float X;
    public float Z;
    public float FaceX = 1f;
    public float FaceZ;
    public float VelX;
    public float VelZ;

    public BattleCommand Command = BattleCommand.Moderate;
    public FightState State = FightState.Idle;
    public float Timer;
    public float Commit;
    public float MpAcc;
    public float[] Cooldown = Array.Empty<float>();
    public float[] CooldownMax = Array.Empty<float>();

    /// <summary>Technique being charged or swung. -1 when none.</summary>
    public int Tech = -1;
    /// <summary>Technique the AI is setting up. Stays until it connects or patience runs out.</summary>
    public int Intent = -1;
    public float Patience;
    public float HoldRange = 4.3f;
    public int Strafe = 1;
    public float StrafeTimer = 9f;

    public bool Landed;
    public bool Guarding;
    public string Flinch = "";
    public float QueuedStun;
    public string QueuedFlinch = "";

    public static Combatant Create(string name, DigimonKit kit, float x, float z)
    {
        return new Combatant
        {
            Name = name,
            Kit = kit,
            Hp = kit.Hp,
            Mp = kit.Mp,
            MaxHp = kit.Hp,
            MaxMp = kit.Mp,
            Cooldown = new float[kit.Abilities.Length],
            CooldownMax = new float[kit.Abilities.Length],
            X = x,
            Z = z,
            Commit = 0.2f,
        };
    }

    public float Speed => MathF.Sqrt(VelX * VelX + VelZ * VelZ);

    /// <summary>True while the combatant is mid-action and should not steer.</summary>
    public bool Acting => State is FightState.Charge or FightState.Strike
        or FightState.Recover or FightState.Hitstun or FightState.Down;

    public void FaceToward(Combatant other)
    {
        FaceX = other.X - X;
        FaceZ = other.Z - Z;
    }

    public float DistanceTo(Combatant other)
    {
        float dx = X - other.X;
        float dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
