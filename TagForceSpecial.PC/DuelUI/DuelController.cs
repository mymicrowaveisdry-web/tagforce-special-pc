using TagForceSpecial.AI;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;

namespace TagForceSpecial.PC.DuelUI;

/// <summary>Coordinates human/AI actions through one authorized adapter on the game thread.</summary>
public sealed class DuelController
{
    private readonly DuelActionSource source;
    private readonly PlayerId viewer, opponent;
    private readonly OpponentDriver driver;
    private readonly List<ReplayEntry> replay = [];
    private readonly IReadOnlyList<uint> firstDeck, secondDeck;
    private readonly bool stagedBattle, replayFromStart;
    public DuelView View { get; private set; }
    public string Notice { get; private set; } = "Choose an action";
    public OpponentStatus? LastOpponentStatus { get; private set; }
    public bool OpponentStopped { get; private set; }
    public bool Paused { get => driver.Paused; set => driver.Paused = value; }
    public uint Seed => driver.Seed;

    public DuelController(DuelEngine engine, CardCatalog catalog, PlayerId viewer = PlayerId.One,
        uint seed = 0, int opponentStepBudget = 12000)
    {
        if (!Enum.IsDefined(viewer)) throw new ArgumentOutOfRangeException(nameof(viewer));
        this.viewer = viewer;
        opponent = viewer == PlayerId.One ? PlayerId.Two : PlayerId.One;
        source = new(engine, catalog);
        driver = new(opponent, seed, opponentStepBudget);
        replayFromStart = engine.State.Revision == 0;
        firstDeck = PlayerProjection.Read(engine.State.GetPlayer(PlayerId.One).Hand.Concat(engine.State.GetPlayer(PlayerId.One).Deck).Select(c => c.GameCardId));
        secondDeck = PlayerProjection.Read(engine.State.GetPlayer(PlayerId.Two).Hand.Concat(engine.State.GetPlayer(PlayerId.Two).Deck).Select(c => c.GameCardId));
        stagedBattle = engine.UsesStagedBattle;
        View = Build();
    }

    public bool Choose(long revision, int choiceId)
    {
        if (replay.Count >= 24000) { Notice = "Duel command budget exhausted"; return false; }
        if (Paused || revision != View.Revision) { Notice = Paused ? "Duel paused" : "That choice has expired"; return false; }
        var before = source.Observe(viewer);
        var result = source.Execute(viewer, revision, choiceId);
        Notice = result.Accepted ? before.Actions[choiceId].Label : $"Unavailable: {result.Error}";
        if (result.Accepted) Record(before, before.Actions[choiceId], result.Revision);
        View = Build();
        return result.Accepted;
    }

    public bool StepOpponent()
    {
        if (OpponentStopped) return false;
        if (replay.Count >= 24000) { OpponentStopped = true; Notice = "Duel command budget exhausted"; return false; }
        var before = source.Observe(opponent);
        var step = driver.Step(source);
        LastOpponentStatus = step.Status;
        if (step.Status == OpponentStatus.Advanced)
        {
            Record(before, before.Actions[step.Choice!.Token], step.Revision);
            // Card names in private hand actions must not enter player-visible notifications.
            Notice = step.Choice.Kind switch
            {
                ActionKind.Draw => source.Observe(opponent).Self.Hand.Count > before.Self.Hand.Count ? "Opponent: drew a card" : "Opponent: began the turn",
                ActionKind.Summon => "Opponent: summoned a monster",
                ActionKind.SetMonster or ActionKind.SetSpellTrap => "Opponent: set a card",
                ActionKind.ActivateSpell or ActionKind.ActivateSetSpell or ActionKind.ChainResponse or ActionKind.BattleResponse => "Opponent: activated a card",
                ActionKind.Attack or ActionKind.ReplayAttack => "Opponent: attacked",
                ActionKind.OrderTriggers => "Opponent: activated an effect",
                ActionKind.ChainPass or ActionKind.BattlePass or ActionKind.DeclineTriggers => "Opponent: passed",
                ActionKind.ResolveChain => "Opponent: resolving the chain",
                ActionKind.AdvanceBattle => "Opponent: continuing the battle",
                ActionKind.Phase => "Opponent: changed phase",
                ActionKind.EndTurn => "Opponent: ended the turn",
                ActionKind.Discard => "Opponent: discarded a card",
                ActionKind.Position => "Opponent: changed a position",
                _ => "Opponent: made a choice"
            };
        }
        else if (step.Status is OpponentStatus.Unsupported or OpponentStatus.BudgetExceeded)
        {
            OpponentStopped = true;
            Notice = "Opponent stopped: no supported continuation";
        }
        else if (step.Status == OpponentStatus.Rejected) Notice = "Opponent refreshing its choices";
        View = Build();
        return step.Status == OpponentStatus.Advanced;
    }

    public DuelReplay ExportReplay(string deckManifest)
    {
        if (!replayFromStart) throw new InvalidOperationException("Replay export requires a session started at revision zero.");
        if (View.Outcome is null) throw new InvalidOperationException("Private replay export is available only after the duel finishes.");
        return new(driver.PolicyVersion, Seed, deckManifest, firstDeck, secondDeck, stagedBattle,
            PlayerProjection.Read(replay), View.Outcome);
    }

    private void Record(DuelObservation before, LegalAction action, long after)
    {
        replay.Add(new(before.Revision, after, before.Player, action.Key, action.Kind, before.Decision?.Id));
    }

    private DuelView Build()
    {
        var v = source.Observe(viewer);
        string prompt = v.Outcome is { } outcome ? outcome.IsDraw ? "DRAW" : outcome.Winner == viewer ? "YOU WIN" : "YOU LOSE"
            : v.Availability == ActionAvailability.Unsupported ? "Unsupported decision"
            : v.Decision is { } d ? $"Choose {d.Kind}"
            : v.Availability == ActionAvailability.Waiting ? "Opponent choosing" : "Choose an action";
        return new(v.Revision, viewer, v.ActivePlayer, v.Turn, v.Phase, v.Self, v.Opponent, prompt,
            PlayerProjection.Read(v.Actions.Select(a => new ChoiceView(a.Token, a.Label, a.CardInstanceId))), v.Outcome);
    }
}
