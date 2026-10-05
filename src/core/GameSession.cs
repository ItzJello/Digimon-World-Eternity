using Godot;
using System;

namespace DigimonWorldEternity;

/// <summary>
/// Lives for the whole session. Scenes read the partner from here.
/// </summary>
public partial class GameSession : Node
{
    public static GameSession Current =>
        ((SceneTree)Engine.GetMainLoop()).Root.GetNode<GameSession>("/root/GameSession");

    public const string LobbyScene = "res://scenes/overworld/lobby.tscn";
    public const string CityScene = "res://scenes/overworld/city.tscn";
    public const string ArenaScene = "res://scenes/arena/arena_match.tscn";
    public const string ViewerScene = "res://scenes/debug/digimon_viewer.tscn";
    public const string StartScene = "res://scenes/menus/start.tscn";
    public const string GateScene = "res://scenes/arena/arena_gate.tscn";
    public const string CardScene = "res://scenes/arena/arena_card.tscn";
    public const string LoadingScene = "res://scenes/boot/loading.tscn";

    public string PendingScene { get; private set; } = LobbyScene;
    public string LoadingCaption { get; private set; } = "Entering File City";

    public string PartnerSlug { get; private set; } = "agumon";

    public string PlayerName { get; private set; } = "Tamer";

    /// <summary>1 is the male body, 2 is the female body.</summary>
    public int PlayerModel { get; private set; } = 1;

    public string PlayerOutfit { get; private set; } = "a";

    public ArenaFormat Format { get; private set; } = ArenaFormat.None;

    public CardStep Step { get; private set; } = CardStep.Search;

    public TournamentBoard? Board { get; private set; }

    public string OpponentSlug { get; private set; } = "";

    public string OpponentTrainer { get; private set; } = "";

    public string OpponentDigimon { get; private set; } = "";

    /// <summary>Field opened for the match that is starting or underway.</summary>
    public string SessionField { get; private set; } = "";

    /// <summary>Debug choice for the next match. Empty means roll on session start.</summary>
    public string QueuedField { get; private set; } = "";

    public event Action? PartnerChanged;
    public event Action? PlayerChanged;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>
    /// Shows the loading screen, then opens the destination once that screen has drawn.
    /// </summary>
    public void Travel(string scenePath, string caption)
    {
        if (string.IsNullOrEmpty(scenePath) || scenePath == LoadingScene)
            return;
        PendingScene = scenePath;
        LoadingCaption = string.IsNullOrWhiteSpace(caption) ? "Loading" : caption;
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(LoadingScene);
    }

    public void EnterArena()
    {
        Board = null;
        Format = ArenaFormat.None;
        OpponentSlug = "";
        OpponentTrainer = "";
        OpponentDigimon = "";
        CloseFieldSession();
        Travel(GateScene, "Entering the Arena");
    }

    public void ChooseFormat(ArenaFormat format)
    {
        Format = format;
        Board = null;
        if (format == ArenaFormat.SingleEliminationPve)
        {
            Board = TournamentBoard.Create(format, PartnerSlug, PlayerName, null, null);
            Step = CardStep.Bracket;
            Travel(CardScene, "Drawing the bracket");
            return;
        }

        Step = CardStep.Search;
        Travel(CardScene, "Searching for an opponent");
    }

    /// <summary>
    /// Placeholder matchmaking. A real session will replace the rival later.
    /// </summary>
    public void FinishSearch()
    {
        (string slug, string trainer) rival = TournamentBoard.RollRival(PartnerSlug, PlayerName);
        PartnerRecord? record = PartnerRoster.Find(rival.slug);
        OpponentSlug = rival.slug;
        OpponentTrainer = rival.trainer;
        OpponentDigimon = record?.DisplayName ?? rival.slug;
        if (Format == ArenaFormat.QuickBattle)
        {
            Step = CardStep.Versus;
            return;
        }

        Board = TournamentBoard.Create(Format, PartnerSlug, PlayerName, rival.slug, rival.trainer);
        Step = CardStep.Bracket;
    }

    public void ReturnToBracket()
    {
        CloseFieldSession();
        Step = CardStep.Bracket;
        Travel(CardScene, "The bracket");
    }

    public void ArmBout(string slug, string trainer, string digimon)
    {
        OpponentSlug = slug;
        OpponentTrainer = trainer;
        OpponentDigimon = digimon;
        Step = CardStep.Versus;
    }

    public void QueueField(string stem) => QueuedField = stem ?? "";

    /// <summary>Keep the open match on this field while a debug bout swaps maps.</summary>
    public void PinField(string stem)
    {
        if (!string.IsNullOrEmpty(stem))
            SessionField = stem;
    }

    /// <summary>
    /// Skip the arena master and the versus card. Two roster Digimon are rolled
    /// and the bout opens on the first field, which the match screen can step through.
    /// </summary>
    public void DebugBout()
    {
        Board = null;
        Format = ArenaFormat.QuickBattle;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        System.Collections.Generic.IReadOnlyList<PartnerRecord> all = PartnerRoster.All;
        int left = all.Count == 0 ? 0 : rng.RandiRange(0, all.Count - 1);
        int right = all.Count <= 1 ? left : rng.RandiRange(0, all.Count - 2);
        if (all.Count > 1 && right >= left)
            right++;
        if (all.Count > 0)
        {
            SetPartner(all[left].Slug);
            OpponentSlug = all[right].Slug;
            OpponentTrainer = "Debug";
            OpponentDigimon = all[right].DisplayName;
        }
        Step = CardStep.Versus;
        CloseFieldSession();
        System.Collections.Generic.List<ArenaMaps.Entry> maps = ArenaMaps.Available();
        if (maps.Count > 0)
            SessionField = maps[0].Stem;
        Travel(ArenaScene, "Arena debug");
    }

    /// <summary>Opens the Digimon model and animation viewer.</summary>
    public void DebugViewer()
    {
        Travel(ViewerScene, "Model viewer");
    }

    /// <summary>
    /// Locks the field for this match. A second call does not roll again,
    /// so the versus card and the bout stay on the same ground.
    /// </summary>
    public void OpenFieldSession()
    {
        if (!string.IsNullOrEmpty(SessionField))
            return;
        if (!string.IsNullOrEmpty(QueuedField) && ArenaMaps.Exists(QueuedField))
        {
            SessionField = QueuedField;
            QueuedField = "";
            return;
        }
        SessionField = ArenaMaps.Roll();
    }

    public void CloseFieldSession() => SessionField = "";

    public void StartBout()
    {
        OpenFieldSession();
        string field = ArenaMaps.Caption(SessionField);
        Travel(ArenaScene, string.IsNullOrEmpty(field) ? "The match begins" : field);
    }

    public void SetPartner(string slug)
    {
        if (string.IsNullOrEmpty(slug) || slug == PartnerSlug)
            return;
        if (PartnerRoster.Find(slug) == null)
            return;
        PartnerSlug = slug;
        PartnerChanged?.Invoke();
    }

    public void UseCharacter(string name, int model, string outfit = "a")
    {
        PlayerName = string.IsNullOrWhiteSpace(name) ? "Tamer" : name.Trim();
        PlayerModel = model == 2 ? 2 : 1;
        PlayerOutfit = string.IsNullOrEmpty(outfit) ? "a" : outfit;
        PlayerChanged?.Invoke();
    }

    public void SetPlayerModel(int model)
    {
        int next = model == 2 ? 2 : 1;
        if (next == PlayerModel)
            return;
        PlayerModel = next;
        PlayerChanged?.Invoke();
    }

    public void SetPlayerOutfit(string outfit)
    {
        if (string.IsNullOrEmpty(outfit) || outfit == PlayerOutfit)
            return;
        PlayerOutfit = outfit;
        PlayerChanged?.Invoke();
    }
}
