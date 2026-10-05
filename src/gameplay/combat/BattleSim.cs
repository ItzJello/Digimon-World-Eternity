using System;

namespace DigimonWorldEternity;

/// <summary>
/// A Digimon World 1 bout between two combatants. Owns position, hits,
/// damage, and the result. The Digimon close in, hold range, and use
/// techniques on their own; a trainer only changes the command.
///
/// This class has no scene nodes. The Arena binds it to bodies, camera,
/// and HUD. A server or a story battle can tick the same sim.
/// </summary>
public sealed class BattleSim
{
    private readonly Random _rng;
    private readonly Combatant[] _order;

    public Combatant Player { get; }
    public Combatant Enemy { get; }
    public float Radius { get; }

    public bool Over { get; private set; }
    public string? Winner { get; private set; }
    public string Message { get; set; } = "The match begins.";

    /// <summary>A technique connected. Victim, damage, guarded.</summary>
    public event Action<Combatant, int, bool>? Hit;
    /// <summary>A technique missed its target.</summary>
    public event Action<Combatant>? Missed;
    /// <summary>The bout ended. Winner and loser are null on a draw.</summary>
    public event Action<Combatant?, Combatant?>? Finished;

    public BattleSim(Combatant player, Combatant enemy, float radius = BattleRules.ArenaRadius, Random? rng = null)
    {
        Player = player;
        Enemy = enemy;
        Radius = radius;
        _rng = rng ?? new Random();
        _order = new[] { player, enemy };
        player.FaceToward(enemy);
        enemy.FaceToward(player);
    }

    public Combatant Opponent(Combatant of) => of == Player ? Enemy : Player;

    public void Tick(float dt)
    {
        if (Over)
            return;
        foreach (Combatant self in _order)
            TickOne(self, Opponent(self), dt);
        BattleSteering.Separate(Player, Enemy, BattleRules.Contact, Radius);
        if (Player.Hp <= 0 || Enemy.Hp <= 0)
            Finish();
    }

    public void SetCommand(Combatant who, BattleCommand command, bool announce = true)
    {
        if (Over || who.Command == command)
            return;
        who.Command = command;
        who.Intent = -1;
        who.Commit = 0f;
        if (announce)
            Message = $"{who.Name}: {command.Label()}";
    }

    private void TickOne(Combatant self, Combatant other, float dt)
    {
        // A swing that has not connected yet still gets to land, even if the
        // other Digimon already hit them this frame.
        if (self.Hp <= 0 && !(self.State == FightState.Strike && !self.Landed))
        {
            self.State = FightState.Down;
            return;
        }

        for (int i = 0; i < self.Cooldown.Length; i++)
        {
            if (self.Cooldown[i] > 0f)
                self.Cooldown[i] = Math.Max(0f, self.Cooldown[i] - dt);
        }
        Regen(self, dt);

        switch (self.State)
        {
            case FightState.Hitstun:
                BattleSteering.Stop(self);
                self.Timer -= dt;
                if (self.Timer <= 0f)
                {
                    // Stand up, then wait. Firing on the exit frame is what let
                    // the first Digimon in the update order chain the next hit.
                    self.State = FightState.Idle;
                    self.Intent = -1;
                    self.Patience = 0f;
                    self.Commit = 0.4f;
                }
                return;
            case FightState.Charge:
                BattleSteering.Stop(self);
                self.FaceToward(other);
                self.Timer -= dt;
                if (self.Timer <= 0f && self.Tech >= 0)
                {
                    AbilityRecord tech = self.Kit.Abilities[self.Tech];
                    self.State = FightState.Strike;
                    self.Timer = BattleRules.StrikeTime;
                    self.Landed = false;
                    self.Mp = Math.Max(0, self.Mp - tech.Mp);
                }
                return;
            case FightState.Strike:
                BattleSteering.Stop(self);
                self.FaceToward(other);
                if (!self.Landed && self.Tech >= 0)
                {
                    self.Landed = true;
                    AbilityRecord tech = self.Kit.Abilities[self.Tech];
                    if (self.DistanceTo(other) <= tech.Range * 1.1f && Rolls(DamageRules.Accuracy(self, tech)))
                        ApplyHit(self, other, tech);
                    else
                    {
                        Message = $"{tech.Name} — {self.Name} missed";
                        Missed?.Invoke(other);
                    }
                }
                self.Timer -= dt;
                if (self.Timer <= 0f && self.Tech >= 0)
                {
                    if (self.Hp <= 0)
                    {
                        self.State = FightState.Down;
                    }
                    else if (self.QueuedStun > 0f)
                    {
                        self.Flinch = self.QueuedFlinch;
                        self.Timer = self.QueuedStun;
                        self.QueuedStun = 0f;
                        self.State = FightState.Hitstun;
                    }
                    else
                    {
                        self.State = FightState.Recover;
                        self.Timer = BattleRules.RecoverTime(self.Kit.Abilities[self.Tech]);
                    }
                }
                return;
            case FightState.Recover:
                BattleSteering.Brake(self, 8f, dt);
                self.Timer -= dt;
                if (self.Timer <= 0f)
                {
                    self.State = FightState.Idle;
                    // Step back to the command's spacing before the next choice.
                    self.Intent = -1;
                    self.HoldRange = TechniquePlanner.RestRange(self.Command);
                    self.Patience = 0f;
                    self.Commit = 0.45f;
                }
                return;
        }

        // A chosen technique stays until it connects. Re-rolling every half
        // second was what made them stare, then walk back out of range.
        if (self.Intent >= 0)
        {
            AbilityRecord aimed = self.Kit.Abilities[self.Intent];
            bool ready = self.Cooldown[self.Intent] <= 0f && (aimed.Mp <= 0 || self.Mp >= aimed.Mp);
            if (!ready)
            {
                self.Intent = -1;
            }
            else if (TechniquePlanner.CanFire(self, other, aimed))
            {
                BeginTech(self, other, self.Intent);
            }
            else
            {
                self.Patience -= dt;
                if (self.Patience <= 0f)
                    self.Intent = -1;
            }
        }

        if (self.State != FightState.Idle)
            return;

        if (self.Intent < 0)
        {
            self.Commit -= dt;
            if (self.Commit <= 0f)
                Decide(self, other);
        }

        if (self.State != FightState.Idle)
            return;

        float desired = self.HoldRange > 0.1f ? self.HoldRange : TechniquePlanner.RestRange(self.Command);
        BattleSteering.Steer(self, other, desired, Radius, dt, _rng);
        self.FaceToward(other);
    }

    private void Decide(Combatant self, Combatant other)
    {
        self.Commit = TechniquePlanner.DecisionWindow(self, _rng);
        self.Guarding = self.Command == BattleCommand.Defend;
        self.Tech = -1;
        if (self.Command == BattleCommand.Defend)
        {
            self.Intent = -1;
            self.HoldRange = TechniquePlanner.RestRange(self.Command);
            self.State = FightState.Idle;
            return;
        }

        int pick = TechniquePlanner.Pick(self, other, _rng);
        if (pick < 0)
        {
            self.Intent = -1;
            self.Patience = 0f;
            self.HoldRange = TechniquePlanner.RestRange(self.Command);
            self.State = FightState.Idle;
            return;
        }

        AbilityRecord tech = self.Kit.Abilities[pick];
        self.Intent = pick;
        self.Patience = BattleRules.IntentPatience;
        self.HoldRange = TechniquePlanner.Spacing(self, tech);
        if (TechniquePlanner.CanFire(self, other, tech))
            BeginTech(self, other, pick);
        else
            self.State = FightState.Idle;
    }

    private static void BeginTech(Combatant self, Combatant other, int index)
    {
        AbilityRecord tech = self.Kit.Abilities[index];
        float cool = BattleRules.Cooldown(tech);
        self.Cooldown[index] = cool;
        self.CooldownMax[index] = Math.Max(self.CooldownMax[index], cool);
        if (tech.Mp > 0)
        {
            for (int i = 0; i < self.Kit.Abilities.Length; i++)
            {
                if (i == index || self.Kit.Abilities[i].Mp <= 0)
                    continue;
                if (self.Cooldown[i] < BattleRules.SpecialLockout)
                {
                    self.Cooldown[i] = BattleRules.SpecialLockout;
                    self.CooldownMax[i] = Math.Max(self.CooldownMax[i], BattleRules.SpecialLockout);
                }
            }
        }

        self.Tech = index;
        self.State = FightState.Charge;
        self.Timer = BattleRules.ChargeTime(tech);
        self.FaceToward(other);
    }

    private void ApplyHit(Combatant atk, Combatant vic, AbilityRecord tech)
    {
        if (vic.Hp <= 0)
            return;
        int damage = DamageRules.Damage(atk, vic, tech, _rng.Next(90, 111));
        vic.Hp = Math.Max(0, vic.Hp - damage);
        Hit?.Invoke(vic, damage, vic.Guarding);
        const string flinch = "bd01";
        float stun = DamageRules.FlinchTime(damage, vic.Guarding);
        // Both swings finish, then both flinch for the same length. Stunning
        // the first one immediately is what made one Digimon always recover first.
        bool swinging = vic.State == FightState.Strike;
        if (swinging && vic.Hp > 0)
        {
            if (stun >= vic.QueuedStun)
                vic.QueuedFlinch = flinch;
            vic.QueuedStun = Math.Max(vic.QueuedStun, stun);
            if (atk.QueuedStun > 0f)
            {
                float shared = Math.Max(atk.QueuedStun, vic.QueuedStun);
                atk.QueuedStun = shared;
                vic.QueuedStun = shared;
            }
        }
        else if (vic.Hp > 0)
        {
            vic.Flinch = flinch;
            vic.Timer = stun;
            vic.State = FightState.Hitstun;
        }
        else if (!swinging)
        {
            vic.State = FightState.Down;
            vic.Timer = 0f;
        }
        if (!swinging)
            BattleSteering.Knockback(vic, atk, DamageRules.Knockback(vic.Guarding), Radius);
        Message = vic.Guarding
            ? $"{tech.Name}! {vic.Name} guards {damage}"
            : $"{tech.Name}! {vic.Name} takes {damage}";
    }

    private static void Regen(Combatant self, float dt)
    {
        if (self.Mp >= self.MaxMp)
            return;
        float period = BattleRules.RegenPeriod(self.Command);
        self.MpAcc += dt;
        while (self.MpAcc >= period && self.Mp < self.MaxMp)
        {
            self.MpAcc -= period;
            self.Mp++;
        }
    }

    private void Finish()
    {
        Over = true;
        if (Player.Hp <= 0 && Enemy.Hp <= 0)
        {
            Player.State = FightState.Down;
            Enemy.State = FightState.Down;
            BattleSteering.Stop(Player);
            BattleSteering.Stop(Enemy);
            Winner = "";
            Message = "Draw";
            Finished?.Invoke(null, null);
            return;
        }
        Combatant winner = Player.Hp > 0 ? Player : Enemy;
        Combatant loser = Opponent(winner);
        winner.State = FightState.Idle;
        BattleSteering.Stop(winner);
        loser.State = FightState.Down;
        BattleSteering.Stop(loser);
        Winner = winner.Name;
        Message = $"{winner.Name} wins";
        Finished?.Invoke(winner, loser);
    }

    private bool Rolls(int chanceOutOf100) => _rng.Next(100) < chanceOutOf100;
}
