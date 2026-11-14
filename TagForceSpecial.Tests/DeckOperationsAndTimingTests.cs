using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>
/// Seeded shuffle, activation timing by kind, chain-response speed and the non-turn-player open window.
/// </summary>
public sealed class DeckOperationsAndTimingTests
{
    private const uint Filler = 10, Burn = 20, QuickPlay = 21, NormalTrap = 22, CounterTrap = 23, Shuffler = 24;

    private static CardCatalog Catalog() => new(new(null, "synthetic-deck-and-timing"),
        new uint[] { Filler, Burn, QuickPlay, NormalTrap, CounterTrap, Shuffler }
            .Select(id => new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, id == Filler ? 4u : 0u))));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second, ulong? seed = null) =>
        new(Catalog(), new Dictionary<uint, DuelCardRules>
            {
                [Filler] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true),
                [Burn] = new(CardCategory.Spell, AllowsSpellTrapSet: true),
                [QuickPlay] = new(CardCategory.Spell, AllowsSpellTrapSet: true),
                [NormalTrap] = new(CardCategory.Trap, AllowsSpellTrapSet: true),
                [CounterTrap] = new(CardCategory.Trap, AllowsSpellTrapSet: true),
                [Shuffler] = new(CardCategory.Spell, AllowsSpellTrapSet: true)
            }, first, second,
            effectDefinitions:
            [
                new("burn-100", Burn, EffectKind.NormalSpell, [new DealDamageStep(100)]),
                new("quick-200", QuickPlay, EffectKind.QuickPlaySpell, [new DealDamageStep(200)]),
                new("trap-300", NormalTrap, EffectKind.NormalTrap, [new DealDamageStep(300)]),
                new("counter-400", CounterTrap, EffectKind.CounterTrap, [new DealDamageStep(400)]),
                .. seed is null ? Array.Empty<EffectDefinition>() : [new("shuffle", Shuffler, EffectKind.NormalSpell, [new ShuffleDeckStep()])]
            ],
            shuffleSeed: seed);

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(Filler, 40 - top.Length)];
    private static CardInstance Hand(DuelEngine e, PlayerId p, uint id) => e.State.GetPlayer(p).Hand.First(c => c.GameCardId == id);
    private static void Set(DuelEngine e, PlayerId p, uint id, int zone) => Ok(e, new SetSpellTrap(p, Hand(e, p, id).InstanceId, zone));
    private static long ChainId(DuelEngine e) => e.State.PendingChain!.DecisionId;

    /// <summary>Phase change that passes any open window the non-turn player is offered.</summary>
    private static void Phase(DuelEngine e, DuelPhase target)
    {
        Ok(e, new ChangePhase(e.State.ActivePlayer, target));
        if (e.State.OpenWindow is { } window) Ok(e, new PassChainResponse(window.Responder, window.DecisionId));
        Assert.Equal(target, e.State.Phase);
    }

    private static void OpenMain(DuelEngine e)
    {
        Ok(e, new ResolveDrawStep(e.State.ActivePlayer));
        Phase(e, DuelPhase.Main1);
    }

    private static void NextTurn(DuelEngine e)
    {
        Phase(e, DuelPhase.End);
        Ok(e, new EndTurn(e.State.ActivePlayer));
        OpenMain(e);
    }

    /// <summary>Player Two sets the given cards on turn 2; returns at Player One's turn 3 Main Phase 1.</summary>
    private static DuelEngine WithOpponentSet(params uint[] set)
    {
        var e = Engine(Deck(Burn, QuickPlay, NormalTrap, CounterTrap), Deck(set));
        OpenMain(e);
        NextTurn(e);
        for (int i = 0; i < set.Length; i++) Set(e, PlayerId.Two, set[i], i);
        NextTurn(e);
        return e;
    }

    [Fact]
    public void GeneratorMatchesTheXoshiro128StarStarReferenceAndSeedsDeterministically()
    {
        var state = new DuelRandomState(1, 2, 3, 4);
        List<uint> outputs = [];
        for (int i = 0; i < 4; i++) { (uint value, state) = state.NextUInt32(); outputs.Add(value); }
        Assert.Equal([11520u, 0u, 5927040u, 70819200u], outputs);
        Assert.Equal(DuelRandomState.FromSeed(42), DuelRandomState.FromSeed(42));
        Assert.NotEqual(DuelRandomState.FromSeed(42), DuelRandomState.FromSeed(43));
    }

    [Fact]
    public void ShuffleStepPermutesOnlyTheDeckDeterministicallyPerSeedAndRevealsNoOrder()
    {
        IReadOnlyList<CardInstance> ShuffledDeck(ulong seed, out DuelEngine engine)
        {
            uint[] first = [Shuffler, Filler, Filler, Filler, Filler, .. Enumerable.Range(0, 35).Select(i => i % 2 == 0 ? Burn : Filler)];
            engine = Engine(first, Deck(), seed);
            OpenMain(engine);
            var before = engine.State.GetPlayer(PlayerId.One).Deck;
            var randomBefore = engine.State.Random;
            Ok(engine, new ActivateSpell(PlayerId.One, Hand(engine, PlayerId.One, Shuffler).InstanceId, 0));
            var events = Resolve(engine);
            Assert.Equal(new DeckShuffled(PlayerId.One, "shuffle"), Assert.Single(events.OfType<DeckShuffled>()));
            var after = engine.State.GetPlayer(PlayerId.One).Deck;
            Assert.Equal(before.OrderBy(c => c.InstanceId), after.OrderBy(c => c.InstanceId));
            Assert.NotEqual(randomBefore, engine.State.Random);
            return after;
        }
        var a = ShuffledDeck(7, out var engine);
        Assert.Equal(a, ShuffledDeck(7, out _));
        Assert.NotEqual(a, ShuffledDeck(8, out _));
        var view = new DuelActionSource(engine, Catalog()).Observe(PlayerId.Two);
        Assert.Equal(a.Count, view.Opponent.DeckCount); // the opponent's view exposes the count only
    }

    [Fact]
    public void ActivationTimingFollowsTheKind()
    {
        var e = Engine(Deck(QuickPlay, QuickPlay, NormalTrap, CounterTrap, Burn), Deck());
        OpenMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, NormalTrap).InstanceId, 0), DuelError.ActivationTimingNotAllowed);
        Set(e, PlayerId.One, QuickPlay, 1);
        Set(e, PlayerId.One, NormalTrap, 2);
        Set(e, PlayerId.One, CounterTrap, 3);
        Reject(e, new ActivateSetSpell(PlayerId.One, 1), DuelError.SetThisTurn);
        Reject(e, new ActivateSetSpell(PlayerId.One, 2), DuelError.SetThisTurn);
        Reject(e, new ActivateSetSpell(PlayerId.One, 3), DuelError.ActivationTimingNotAllowed);
        // Quick-Play from hand in the own Main Phase is legal.
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, QuickPlay).InstanceId, 0));
        Resolve(e);
        Assert.Equal(7800, e.State.GetPlayer(PlayerId.Two).LifePoints);
        NextTurn(e); NextTurn(e); // Player One's set cards offer windows on Player Two's turn; they are passed
        Ok(e, new ActivateSetSpell(PlayerId.One, 2)); // the Normal Trap, a turn after it was set
        Resolve(e);
        Assert.Equal(7500, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Reject(e, new ActivateSetSpell(PlayerId.One, 3), DuelError.ActivationTimingNotAllowed); // Counter Traps only respond
    }

    [Fact]
    public void ChainResponsesRespectSetTurnAndSpellSpeedAndResolveLastFirst()
    {
        var e = WithOpponentSet(NormalTrap, CounterTrap, QuickPlay);
        Set(e, PlayerId.One, QuickPlay, 1); // set this turn: cannot respond yet
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Burn).InstanceId, 0));
        Ok(e, new ActivateChainResponse(PlayerId.Two, ChainId(e), 1)); // Counter Trap, Spell Speed 3
        Reject(e, new ActivateChainResponse(PlayerId.One, ChainId(e), 1), DuelError.SetThisTurn);
        var e2 = WithOpponentSet(NormalTrap);
        Ok(e2, new ActivateSpell(PlayerId.One, Hand(e2, PlayerId.One, Burn).InstanceId, 0));
        Ok(e2, new ActivateChainResponse(PlayerId.Two, ChainId(e2), 0)); // Normal Trap, Spell Speed 2
        var events = Resolve(e2);
        var damage = events.OfType<EffectDamageDealt>().ToArray();
        Assert.Equal([300, 100], damage.Select(d => d.Damage)); // last in, first resolved
        Assert.Equal([PlayerId.One, PlayerId.Two], damage.Select(d => d.Player));
        Assert.Contains(events, ev => ev is ChainTrapSentToGraveyard t && t.Card.GameCardId == NormalTrap);
        Assert.Contains(events, ev => ev is SpellSentToGraveyard s && s.Card.GameCardId == Burn);
    }

    [Fact]
    public void SpellSpeedTwoCannotAnswerACounterTrapAndNormalSpellsCannotRespond()
    {
        // Player One sets a Quick-Play and a Normal Spell on turn 1; Player Two sets a Counter Trap on turn 2.
        var p1 = Engine(Deck(QuickPlay, Burn, Burn), Deck(CounterTrap, Burn));
        OpenMain(p1);
        Set(p1, PlayerId.One, QuickPlay, 0);
        Set(p1, PlayerId.One, Burn, 1);
        NextTurn(p1);
        Set(p1, PlayerId.Two, CounterTrap, 0);
        NextTurn(p1);
        Ok(p1, new ActivateSpell(PlayerId.One, Hand(p1, PlayerId.One, Burn).InstanceId, 2));
        Ok(p1, new ActivateChainResponse(PlayerId.Two, ChainId(p1), 0)); // Counter Trap (3)
        Reject(p1, new ActivateChainResponse(PlayerId.One, ChainId(p1), 0), DuelError.ChainResponseSpeedNotAllowed);
        Reject(p1, new ActivateChainResponse(PlayerId.One, ChainId(p1), 1), DuelError.ActivationTimingNotAllowed);
    }

    [Fact]
    public void PhaseChangeOpensAWindowOnlyForALegalOpponentActivationAndPassAppliesIt()
    {
        var e = WithOpponentSet(NormalTrap);
        var request = Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        var window = e.State.OpenWindow!;
        Assert.Equal(new OpenWindowRequested(PlayerId.Two, window.DecisionId, DuelPhase.End), Assert.Single(request.Events));
        Assert.Equal(DuelPhase.Main1, e.State.Phase);
        Reject(e, new PassChainResponse(PlayerId.One, window.DecisionId), DuelError.NotChainResponder);
        Reject(e, new PassChainResponse(PlayerId.Two, window.DecisionId + 1), DuelError.StaleChainDecision);
        Reject(e, new NormalSummon(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0), DuelError.OpenWindowPending);
        Reject(e, new ActivateChainResponse(PlayerId.Two, window.DecisionId, 1), DuelError.UnsupportedChainResponse);
        var passed = Ok(e, new PassChainResponse(PlayerId.Two, window.DecisionId));
        Assert.IsType<OpenWindowPassed>(passed.Events[0]);
        Assert.Equal(new PhaseChanged(DuelPhase.Main1, DuelPhase.End), passed.Events[1]);
        Assert.Null(e.State.OpenWindow);
        Assert.Equal(DuelPhase.End, e.State.Phase);
    }

    [Fact]
    public void OpponentActivationInTheWindowStartsAChainAndTheRequestLapses()
    {
        var e = WithOpponentSet(NormalTrap);
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        var source = new DuelActionSource(e, Catalog());
        Assert.Equal(ActionAvailability.Waiting, source.Observe(PlayerId.One).Availability);
        var view = source.Observe(PlayerId.Two);
        Assert.Equal(ActionAvailability.Ready, view.Availability);
        Assert.Equal("OpenWindow", view.Decision!.Kind);
        Assert.Equal([ActionKind.ChainResponse, ActionKind.ChainPass], view.Actions.Select(a => a.Kind));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.OpenWindow!.DecisionId, 0));
        Assert.Null(e.State.OpenWindow);
        Assert.Equal(PlayerId.One, e.State.PendingChain!.Responder);
        Resolve(e);
        Assert.Equal(7700, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(DuelPhase.Main1, e.State.Phase); // the request lapsed
        // The Trap is gone, so a new request applies immediately.
        var again = Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Equal(new PhaseChanged(DuelPhase.Main1, DuelPhase.End), Assert.Single(again.Events));
    }

    [Fact]
    public void NoWindowWithoutALegalActivationAndInvalidPhaseChangesStillReject()
    {
        // The opponent's only set card is a Counter Trap, which cannot open a chain.
        var e = WithOpponentSet(CounterTrap);
        Reject(e, new ChangePhase(PlayerId.One, DuelPhase.Main2), DuelError.InvalidPhaseTransition);
        var request = Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Equal(new PhaseChanged(DuelPhase.Main1, DuelPhase.End), Assert.Single(request.Events));
    }
}
