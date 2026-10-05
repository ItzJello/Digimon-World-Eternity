using System.Collections.Generic;
using Godot;

namespace DigimonWorldEternity;

public enum ArenaFormat
{
    None,
    QuickBattle,
    SingleEliminationPve,
    SingleEliminationPvp,
    DoubleEliminationPvp,
}

public enum CardStep
{
    Search,
    Versus,
    Bracket,
}

public sealed class TournamentSlot
{
    public int Id;
    public string Slug = "";
    public string Digimon = "";
    public string Trainer = "";
    public bool Player;
}

public sealed class TournamentMatch
{
    public int Id;
    public string Round = "";
    public int Left = -1;
    public int Right = -1;
    public int Winner = -1;
    public int LeftSource = -1;
    public bool LeftIsLoser;
    public int RightSource = -1;
    public bool RightIsLoser;
    public bool Played;
}

/// <summary>
/// Eight-tamer bracket. Other matches resolve when the player returns
/// from a bout. Online rivals are a placeholder until the session exists.
/// </summary>
public sealed class TournamentBoard
{
    private static readonly string[] Rivals = { "Ken", "Yuki", "Ren", "Hina", "Noa", "Shun", "Aoi", "Rin" };

    public ArenaFormat Format;
    public List<TournamentSlot> Slots = new();
    public List<TournamentMatch> Matches = new();
    public bool PlayerOut;
    public bool Champion;
    public int Losses;
    private int _seed = 1;

    public int PlayerId { get; private set; }

    public static (string slug, string trainer) RollRival(string playerSlug, string playerName)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        var pool = new List<PartnerRecord>();
        foreach (PartnerRecord record in PartnerRoster.All)
        {
            if (record.Slug != playerSlug)
                pool.Add(record);
        }

        PartnerRecord mon = pool[rng.RandiRange(0, pool.Count - 1)];
        string trainer = Rivals[rng.RandiRange(0, Rivals.Length - 1)];
        if (trainer == playerName)
            trainer = "Kai";
        return (mon.Slug, trainer);
    }

    public static TournamentBoard Create(
        ArenaFormat format,
        string playerSlug,
        string playerName,
        string? rivalSlug,
        string? rivalTrainer)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        var board = new TournamentBoard
        {
            Format = format,
            _seed = rng.RandiRange(1, 999983),
        };

        PartnerRecord? playerMon = PartnerRoster.Find(playerSlug);
        board.Slots.Add(new TournamentSlot
        {
            Id = 0,
            Slug = playerSlug,
            Digimon = playerMon?.DisplayName ?? playerSlug,
            Trainer = playerName,
            Player = true,
        });
        board.PlayerId = 0;

        var used = new HashSet<string> { playerSlug };
        if (!string.IsNullOrEmpty(rivalSlug))
        {
            PartnerRecord? rivalMon = PartnerRoster.Find(rivalSlug);
            board.Slots.Add(new TournamentSlot
            {
                Id = 1,
                Slug = rivalSlug,
                Digimon = rivalMon?.DisplayName ?? rivalSlug,
                Trainer = string.IsNullOrEmpty(rivalTrainer) ? "Kai" : rivalTrainer,
            });
            used.Add(rivalSlug);
        }

        var pool = new List<PartnerRecord>();
        foreach (PartnerRecord record in PartnerRoster.All)
        {
            if (!used.Contains(record.Slug))
                pool.Add(record);
        }

        Shuffle(pool, rng);
        int nameCursor = 0;
        while (board.Slots.Count < 8)
        {
            PartnerRecord record = pool[board.Slots.Count % pool.Count];
            string trainer = Rivals[nameCursor % Rivals.Length];
            nameCursor++;
            if (trainer == playerName)
                trainer = "Kai";
            board.Slots.Add(new TournamentSlot
            {
                Id = board.Slots.Count,
                Slug = record.Slug,
                Digimon = record.DisplayName,
                Trainer = trainer,
            });
        }

        board.BuildMatches();
        return board;
    }

    public string Title => Format switch
    {
        ArenaFormat.DoubleEliminationPvp => "Double Elimination",
        ArenaFormat.SingleEliminationPvp => "Single Elimination",
        _ => "Single Elimination — Practice",
    };

    public TournamentSlot Slot(int id) => Slots[id];

    /// <summary>
    /// Fills any side whose earlier match is already decided, so the tree
    /// can show who advanced.
    /// </summary>
    public void FillKnown()
    {
        for (int pass = 0; pass < 8; pass++)
        {
            foreach (TournamentMatch match in Matches)
                Pull(match);
        }
    }

    public TournamentMatch? PlayerMatch()
    {
        foreach (TournamentMatch match in Matches)
        {
            if (match.Played)
                continue;
            Pull(match);
            if (match.Left < 0 || match.Right < 0)
                continue;
            if (match.Left == PlayerId || match.Right == PlayerId)
                return match;
        }

        return null;
    }

    public void Report(bool playerWon)
    {
        TournamentMatch? match = PlayerMatch();
        if (match == null)
            return;

        int foe = match.Left == PlayerId ? match.Right : match.Left;
        match.Winner = playerWon ? PlayerId : foe;
        match.Played = true;
        if (!playerWon)
        {
            Losses++;
            if (Format != ArenaFormat.DoubleEliminationPvp || Losses >= 2)
                PlayerOut = true;
        }

        Advance();
        Champion = WonTheBracket();
    }

    private void BuildMatches()
    {
        AddSeeded(0, 1, "Quarterfinal");
        AddSeeded(2, 3, "Quarterfinal");
        AddSeeded(4, 5, "Quarterfinal");
        AddSeeded(6, 7, "Quarterfinal");
        AddFed(0, false, 1, false, "Semifinal");
        AddFed(2, false, 3, false, "Semifinal");
        AddFed(4, false, 5, false, "Final");
        if (Format != ArenaFormat.DoubleEliminationPvp)
            return;

        AddFed(0, true, 1, true, "Losers Round 1");
        AddFed(2, true, 3, true, "Losers Round 1");
        AddFed(7, false, 5, true, "Losers Round 2");
        AddFed(8, false, 4, true, "Losers Round 2");
        AddFed(9, false, 10, false, "Losers Round 3");
        AddFed(11, false, 6, true, "Losers Final");
        AddFed(6, false, 12, false, "Grand Final");
        Matches.Add(new TournamentMatch { Id = Matches.Count, Round = "Grand Final (Reset)" });
    }

    private void AddSeeded(int left, int right, string round)
    {
        Matches.Add(new TournamentMatch
        {
            Id = Matches.Count,
            Round = round,
            Left = left,
            Right = right,
        });
    }

    private void AddFed(int leftSource, bool leftLoser, int rightSource, bool rightLoser, string round)
    {
        Matches.Add(new TournamentMatch
        {
            Id = Matches.Count,
            Round = round,
            LeftSource = leftSource,
            LeftIsLoser = leftLoser,
            RightSource = rightSource,
            RightIsLoser = rightLoser,
        });
    }

    private void Advance()
    {
        for (int pass = 0; pass < 24; pass++)
        {
            bool moved = false;
            foreach (TournamentMatch match in Matches)
            {
                Pull(match);
                if (match.Played || match.Left < 0 || match.Right < 0)
                    continue;
                if (match.Left == PlayerId || match.Right == PlayerId)
                    continue;
                match.Winner = CpuWinner(match);
                match.Played = true;
                moved = true;
            }

            if (ArmReset())
                moved = true;
            if (!moved)
                break;
        }
    }

    private bool ArmReset()
    {
        if (Format != ArenaFormat.DoubleEliminationPvp || Matches.Count < 15)
            return false;
        TournamentMatch grand = Matches[13];
        TournamentMatch reset = Matches[14];
        if (!grand.Played || reset.Left >= 0 || Matches[12].Winner < 0)
            return false;
        if (grand.Winner != Matches[12].Winner)
            return false;
        reset.Left = grand.Left;
        reset.Right = grand.Right;
        return true;
    }

    private bool WonTheBracket()
    {
        if (PlayerOut)
            return false;
        if (Format != ArenaFormat.DoubleEliminationPvp)
            return Matches.Count > 6 && Matches[6].Played && Matches[6].Winner == PlayerId;

        TournamentMatch grand = Matches[13];
        TournamentMatch reset = Matches[14];
        if (reset.Left >= 0)
            return reset.Played && reset.Winner == PlayerId;
        return grand.Played && grand.Winner == PlayerId;
    }

    private void Pull(TournamentMatch match)
    {
        if (match.Left < 0)
            match.Left = Take(match.LeftSource, match.LeftIsLoser);
        if (match.Right < 0)
            match.Right = Take(match.RightSource, match.RightIsLoser);
    }

    private int Take(int source, bool loser)
    {
        if (source < 0 || source >= Matches.Count || !Matches[source].Played)
            return -1;
        TournamentMatch match = Matches[source];
        if (!loser)
            return match.Winner;
        return match.Winner == match.Left ? match.Right : match.Left;
    }

    private int CpuWinner(TournamentMatch match)
    {
        DigimonKit left = DigimonKit.For(Slots[match.Left].Slug);
        DigimonKit right = DigimonKit.For(Slots[match.Right].Slug);
        var rng = new RandomNumberGenerator { Seed = (ulong)(_seed + match.Id * 97) };
        int roll = rng.RandiRange(0, left.Offense + right.Offense);
        return roll < left.Offense ? match.Left : match.Right;
    }

    private static void Shuffle(List<PartnerRecord> pool, RandomNumberGenerator rng)
    {
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.RandiRange(0, i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
    }
}
