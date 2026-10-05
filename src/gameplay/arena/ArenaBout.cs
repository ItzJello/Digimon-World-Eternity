using System;
using System.Collections.Generic;
using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// The Arena's binding between the combat sim and the scene. Builds the two
/// combatants from the session, spawns their bodies, ticks the sim, and
/// turns state changes into clips, effects, and combat text. Position and
/// results come from <see cref="BattleSim"/>; nothing here decides a hit.
/// </summary>
public partial class ArenaBout : Node3D
{
    public const float ArenaRadius = BattleRules.ArenaRadius;

    private readonly Random _rng = new();
    private readonly List<Puppet> _puppets = new();
    private BattleSim _sim = null!;
    private CommandAi? _enemyTrainer;
    private Puppet _player = null!;
    private Puppet _enemy = null!;

    public bool Paused { get; set; }

    public BattleSim Sim => _sim;
    public string Message => _sim.Message;
    public bool Over => _sim.Over;
    public string? Winner => _sim.Winner;
    public Node3D PlayerBody => _player.Body;
    public Node3D EnemyBody => _enemy.Body;
    public Combatant Player => _sim.Player;
    public Combatant Enemy => _sim.Enemy;

    public override void _Ready()
    {
        GameSession session = GameSession.Current;
        string playerSlug = session.PartnerSlug;
        string enemySlug = PickEnemy(playerSlug, session.OpponentSlug);
        Combatant player = Make(playerSlug, -5f, 0f);
        Combatant enemy = Make(enemySlug, 5f, 0f);
        player.Command = BattleCommand.Moderate;
        enemy.Command = BattleCommand.Attack;
        enemy.Strafe = -1;
        enemy.StrafeTimer = 14f;

        _sim = new BattleSim(player, enemy, BattleRules.ArenaRadius, _rng);
        _sim.Hit += OnHit;
        _sim.Missed += OnMissed;
        _sim.Finished += OnFinished;
        _enemyTrainer = new CommandAi(_rng);

        _player = Bind(player, new Vector3(-5f, 0, 0));
        _enemy = Bind(enemy, new Vector3(5f, 0, 0));
        foreach (Puppet puppet in _puppets)
            Place(puppet, 0.016f);

        string foe = session.OpponentTrainer;
        _sim.Message = string.IsNullOrEmpty(foe)
            ? $"{player.Name} vs {enemy.Name}"
            : $"{player.Name} ({session.PlayerName}) vs {enemy.Name} ({foe})";
    }

    public override void _Process(double delta)
    {
        if (_sim.Over || Paused)
            return;
        float dt = Mathf.Clamp((float)delta, 0f, 0.05f);
        _enemyTrainer?.Tick(_sim, _sim.Enemy, dt);
        _sim.Tick(dt);
        foreach (Puppet puppet in _puppets)
        {
            Show(puppet);
            Place(puppet, dt);
        }
    }

    public void SetCommand(BattleCommand command) => _sim.SetCommand(_sim.Player, command);

    private string PickEnemy(string playerSlug, string queued)
    {
        if (!string.IsNullOrEmpty(queued) && queued != playerSlug && PartnerRoster.Find(queued) != null)
            return queued;

        var pool = new List<PartnerRecord>();
        foreach (PartnerRecord record in PartnerRoster.All)
        {
            if (record.Slug != playerSlug)
                pool.Add(record);
        }
        if (pool.Count == 0)
            return playerSlug;
        return pool[_rng.Next(pool.Count)].Slug;
    }

    private static Combatant Make(string slug, float x, float z)
    {
        PartnerRecord? record = PartnerRoster.Find(slug);
        string name = record?.DisplayName ?? slug;
        return Combatant.Create(name, DigimonKit.For(slug), x, z);
    }

    private Puppet Bind(Combatant fighter, Vector3 at)
    {
        ActorVisual body = PartnerAvatar.Spawn(fighter.Kit.Slug, fighter.Name);
        body.Scale = Vector3.One * PartnerAvatar.ScaleFor(fighter.Kit.Slug);
        AddChild(body);
        body.GlobalPosition = at;
        var puppet = new Puppet
        {
            Fighter = fighter,
            Body = body,
            Yaw = Mathf.Atan2(fighter.FaceX, fighter.FaceZ),
        };
        _puppets.Add(puppet);
        return puppet;
    }

    private Puppet PuppetOf(Combatant fighter) => fighter == _sim.Player ? _player : _enemy;

    private void OnHit(Combatant victim, int damage, bool guarded)
    {
        DamageNumber.Show(PuppetOf(victim).Body, damage, guarded);
    }

    private void OnMissed(Combatant target)
    {
        DamageNumber.Miss(PuppetOf(target).Body);
    }

    private void OnFinished(Combatant? winner, Combatant? loser)
    {
        if (winner == null || loser == null)
        {
            _player.Body.PlayDown();
            _enemy.Body.PlayDown();
            return;
        }
        PuppetOf(winner).Body.PlayVictory();
        PuppetOf(loser).Body.PlayDown();
    }

    /// <summary>Play the clip and effect for a state the sim just entered.</summary>
    private void Show(Puppet puppet)
    {
        Combatant self = puppet.Fighter;
        if (puppet.Shown == self.State)
            return;
        FightState entered = self.State;
        puppet.Shown = entered;
        if (entered == FightState.Charge && self.Tech >= 0)
        {
            AbilityRecord tech = self.Kit.Abilities[self.Tech];
            puppet.Body.PlayAction(tech.Clip);
            Puppet other = PuppetOf(_sim.Opponent(self));
            if (!string.IsNullOrEmpty(tech.Effect))
                SkillEffect.Play(this, puppet.Body.GlobalPosition, other.Body.GlobalPosition, tech.Effect, !tech.Melee);
            if (tech.Mp > 0)
                SkillCallout.Say(puppet.Body, tech.Name);
        }
        else if (entered == FightState.Hitstun)
            puppet.Body.PlayReaction(string.IsNullOrEmpty(self.Flinch) ? "bd01" : self.Flinch);
        else if (entered == FightState.Down)
            puppet.Body.PlayDown();
    }

    /// <summary>Move the body to the sim position and ease its facing.</summary>
    private static void Place(Puppet puppet, float dt)
    {
        Combatant self = puppet.Fighter;
        float speed = self.Speed;
        if (!self.Acting && speed > 0.7f)
            puppet.Body.SetWalking(true);
        else if (!self.Acting && speed < 0.35f)
            puppet.Body.SetWalking(false);

        puppet.Body.GlobalPosition = new Vector3(self.X, 0, self.Z);

        Vector3 look = new(self.FaceX, 0, self.FaceZ);
        Vector3 travel = new(self.VelX, 0, self.VelZ);
        if (look.LengthSquared() > 0.01f && speed > 0.6f)
        {
            float along = Mathf.Abs(travel.Normalized().Dot(look.Normalized()));
            float travelBias = Mathf.Lerp(0.72f, 0.92f, along);
            look = look.Normalized() * (1f - travelBias) + travel.Normalized() * travelBias;
        }
        if (look.LengthSquared() > 0.0001f)
        {
            float target = Mathf.Atan2(look.X, look.Z);
            float step = 4.2f * Mathf.Max(dt, 0.001f);
            puppet.Yaw += Mathf.Clamp(Mathf.AngleDifference(puppet.Yaw, target), -step, step);
            puppet.Body.Rotation = new Vector3(0, puppet.Yaw, 0);
        }
    }

    /// <summary>A combatant and the body that shows it.</summary>
    private sealed class Puppet
    {
        public Combatant Fighter = null!;
        public ActorVisual Body = null!;
        public float Yaw;
        public FightState Shown = FightState.Idle;
    }
}
