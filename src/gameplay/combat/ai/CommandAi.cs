using System;

namespace DigimonWorldEternity;

/// <summary>
/// A computer trainer. Rolls a new command for its Digimon every several
/// seconds, backing off when hurt and guarding when the other side winds up.
/// A human or remote trainer replaces this with SetCommand calls.
/// </summary>
public sealed class CommandAi
{
    private readonly Random _rng;
    private float _timer;

    public CommandAi(Random rng, float firstDecisionIn = 6f)
    {
        _rng = rng;
        _timer = firstDecisionIn;
    }

    public void Tick(BattleSim sim, Combatant self, float dt)
    {
        _timer -= dt;
        if (_timer > 0f || self.Hp <= 0)
            return;
        _timer = 7f + _rng.NextSingle() * 3f;
        Combatant other = sim.Opponent(self);
        int hpPct = self.MaxHp > 0 ? self.Hp * 100 / self.MaxHp : 0;
        int roll = _rng.Next(100);
        BattleCommand next;
        if (hpPct < 30 && roll < 40)
            next = BattleCommand.Distance;
        else if (other.State == FightState.Charge && roll < 25)
            next = BattleCommand.Defend;
        else if (roll < 50)
            next = BattleCommand.Attack;
        else if (roll < 80)
            next = BattleCommand.Moderate;
        else
            next = BattleCommand.Distance;
        sim.SetCommand(self, next, announce: false);
    }
}
