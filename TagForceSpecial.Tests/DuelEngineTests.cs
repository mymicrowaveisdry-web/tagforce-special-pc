using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tests;

public sealed class DuelEngineTests
{
    private static CardCatalog Catalog(uint firstLevel = 4) => new(new(new string('0', 64), "synthetic-stage5"),
        Enumerable.Range(1, 40).Select(i => new CardDefinition((uint)i, $"Synthetic {i}", "Synthetic test data",
            new(1000, 1000, i == 1 ? firstLevel : 4), new(0, 0, 0, 0, 0, 0, 0),
            new(i, (uint)i | 100u << 14 | 100u << 23, (i == 1 ? firstLevel : 4u) << 11, 0))));

    private static uint[] Deck() => Enumerable.Range(1, 40).Select(i => (uint)i).ToArray();
    private static Dictionary<uint, DuelCardRules> Rules() => Deck().ToDictionary(id => id,
        _ => new DuelCardRules(CardCategory.Monster, true));
    private static DuelEngine Engine() => new(Catalog(), Rules(), Deck(), Deck());

    private static DuelActionResult Ok(DuelEngine engine, DuelCommand command)
    {
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
        Assert.Equal(DuelError.None, result.Error);
        Assert.Same(engine.State, result.State);
        return result;
    }

    private static void Reject(DuelEngine engine, DuelCommand command, DuelError expected)
    {
        var before = engine.State;
        var result = engine.Execute(command);
        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Error);
        Assert.Same(before, engine.State);
        Assert.Same(before, result.State);
        Assert.Empty(result.Events);
    }

    private static void EnterMainPhase(DuelEngine engine)
    {
        var actor = engine.State.ActivePlayer;
        Ok(engine, new ResolveDrawStep(actor));
        Ok(engine, new ChangePhase(actor, DuelPhase.Main1));
    }

    private static void Finish(DuelEngine engine)
    {
        var actor = engine.State.ActivePlayer;
        Ok(engine, new ChangePhase(actor, DuelPhase.End));
        while (engine.State.GetPlayer(actor).Hand.Count > 6)
            Ok(engine, new DiscardForHandLimit(actor, engine.State.GetPlayer(actor).Hand[0].InstanceId));
        Ok(engine, new EndTurn(actor));
    }

    [Fact]
    public void SetupDealsFiveAndKeepsDistinctInstancesAndInputSnapshots()
    {
        var deck = Deck();
        deck[1] = 1;
        var rules = Rules();
        var engine = new DuelEngine(Catalog(), rules, deck, deck);
        deck[0] = 999;
        rules.Clear();
        var one = engine.State.GetPlayer(PlayerId.One);
        Assert.Equal(8000, one.LifePoints);
        Assert.Equal(5, one.Hand.Count);
        Assert.Equal(35, one.Deck.Count);
        Assert.Equal(6u, one.Deck[0].GameCardId);
        Assert.Equal(one.Hand[0].GameCardId, one.Hand[1].GameCardId);
        Assert.NotEqual(one.Hand[0].InstanceId, one.Hand[1].InstanceId);
        Assert.Equal(80, engine.State.Players.SelectMany(p => p.Deck.Concat(p.Hand)).Select(c => c.InstanceId).Distinct().Count());
        Assert.All(engine.State.Players, p =>
        {
            Assert.All(p.Deck.Concat(p.Hand), c => Assert.Equal(p.Id, c.Owner));
            Assert.Equal(5, p.MonsterZones.Count);
            Assert.All(p.MonsterZones, zone => Assert.Null(zone));
            Assert.Equal(5, p.SpellTrapZones.Count);
            Assert.All(p.SpellTrapZones, zone => Assert.Null(zone));
            Assert.Null(p.FieldZone);
            Assert.Empty(p.Graveyard);
        });
        Assert.Throws<NotSupportedException>(() => ((IList<CardInstance>)one.Hand).Clear());
        EnterMainPhase(engine);
        Ok(engine, new NormalSummon(PlayerId.One, one.Hand[0].InstanceId, 2));
        Assert.Equal(5, one.Hand.Count); // Previously published state remains unchanged.
        Assert.Null(one.MonsterZones[2]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(39)]
    [InlineData(61)]
    public void RejectsUnsupportedDeckSizes(int count) => Assert.Throws<ArgumentException>(() =>
        new DuelEngine(Catalog(), Rules(), Enumerable.Repeat(1u, count), Deck()));

    [Theory]
    [InlineData(40)]
    [InlineData(60)]
    public void AcceptsDeckSizeBoundariesWithoutClaimingFullDeckLegality(int count)
    {
        var engine = new DuelEngine(Catalog(), Rules(), Enumerable.Repeat(1u, count), Deck());
        Assert.Equal(count - 5, engine.State.GetPlayer(PlayerId.One).Deck.Count);
    }

    [Fact]
    public void RejectsUnknownIdsInvalidStarterAndInvalidRules()
    {
        var deck = Deck(); deck[0] = 999;
        Assert.Throws<ArgumentException>(() => new DuelEngine(Catalog(), Rules(), deck, Deck()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DuelEngine(Catalog(), Rules(), Deck(), Deck(), (PlayerId)9));
        var rules = Rules(); rules[1] = new(CardCategory.Spell, true);
        Assert.Throws<ArgumentException>(() => new DuelEngine(Catalog(), rules, Deck(), Deck()));
    }

    [Theory]
    [InlineData(PlayerId.One)]
    [InlineData(PlayerId.Two)]
    public void StartingPlayerSkipsOnlyTheOpeningDraw(PlayerId starter)
    {
        var engine = new DuelEngine(Catalog(), Rules(), Deck(), Deck(), starter);
        var skipped = Ok(engine, new ResolveDrawStep(starter));
        Assert.IsType<FirstDrawSkipped>(skipped.Events[0]);
        Assert.Equal(5, engine.State.GetPlayer(starter).Hand.Count);
        Assert.Equal(35, engine.State.GetPlayer(starter).Deck.Count);
        Assert.Equal(DuelPhase.Standby, engine.State.Phase);
        Reject(engine, new ResolveDrawStep(starter), DuelError.WrongPhase);
        Ok(engine, new ChangePhase(starter, DuelPhase.Main1));
        Finish(engine);
        var opponent = engine.State.ActivePlayer;
        var drawn = Ok(engine, new ResolveDrawStep(opponent));
        Assert.Equal(6u, Assert.IsType<CardDrawn>(drawn.Events[0]).Card.GameCardId);
        Assert.Equal(6, engine.State.GetPlayer(opponent).Hand.Count);
        Assert.Equal(34, engine.State.GetPlayer(opponent).Deck.Count);
    }

    [Fact]
    public void CompleteSequenceDrawsSummonsAndResetsOnlyAtTheNextTurn()
    {
        var engine = Engine();
        EnterMainPhase(engine);
        var card = engine.State.GetPlayer(PlayerId.One).Hand[0];
        var result = Ok(engine, new NormalSummon(PlayerId.One, card.InstanceId, 3));
        var summoned = Assert.IsType<MonsterNormalSummoned>(Assert.Single(result.Events));
        Assert.Equal(card, summoned.Monster.Card);
        Assert.Equal(BattlePosition.FaceUpAttack, summoned.Monster.Position);
        Assert.Equal(4, engine.State.GetPlayer(PlayerId.One).Hand.Count);
        Assert.True(engine.State.NormalSummonUsed);
        Reject(engine, new NormalSummon(PlayerId.One, card.InstanceId + 1, 0), DuelError.NormalSummonLimitReached);
        Finish(engine);
        Assert.Equal(PlayerId.Two, engine.State.ActivePlayer);
        Assert.Equal(2, engine.State.TurnNumber);
        Assert.Equal(DuelPhase.Draw, engine.State.Phase);
        Assert.False(engine.State.NormalSummonUsed);
        EnterMainPhase(engine);
        Ok(engine, new NormalSummon(PlayerId.Two, engine.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 0));
        Finish(engine);
        EnterMainPhase(engine);
        Ok(engine, new NormalSummon(PlayerId.One, engine.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0));
        Assert.NotNull(engine.State.GetPlayer(PlayerId.One).MonsterZones[3]);
        Assert.NotNull(engine.State.GetPlayer(PlayerId.One).MonsterZones[0]);
    }

    [Fact]
    public void Main2RequiresBattleAndSharesTheNormalSummonAllowance()
    {
        var engine = Engine(); EnterMainPhase(engine);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle), DuelError.FirstTurnBattleNotAllowed);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2), DuelError.InvalidPhaseTransition);
        Finish(engine); EnterMainPhase(engine);
        Ok(engine, new NormalSummon(PlayerId.Two, engine.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 0));
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Main2));
        Reject(engine, new NormalSummon(PlayerId.Two, engine.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 1),
            DuelError.NormalSummonLimitReached);
        Finish(engine); EnterMainPhase(engine);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2));
        Ok(engine, new NormalSummon(PlayerId.One, engine.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0));
    }

    [Fact]
    public void RejectsOutOfTurnInvalidPhaseAndUnsupportedCommandsAtomically()
    {
        var engine = Engine();
        Reject(engine, new ResolveDrawStep(PlayerId.Two), DuelError.NotActivePlayer);
        Reject(engine, new ResolveDrawStep((PlayerId)42), DuelError.InvalidPlayer);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.Main1), DuelError.InvalidPhaseTransition);
        Reject(engine, new EndTurn(PlayerId.One), DuelError.WrongPhase);
        Reject(engine, new NormalSummon(PlayerId.One, 1, 0), DuelError.WrongPhase);
        Reject(engine, new DiscardForHandLimit(PlayerId.One, 1), DuelError.WrongPhase);
        Reject(engine, new Unsupported(PlayerId.One), DuelError.UnsupportedCommand);
        EnterMainPhase(engine);
        Reject(engine, new ChangePhase(PlayerId.One, (DuelPhase)99), DuelError.InvalidPhaseTransition);
        Reject(engine, new ResolveDrawStep(PlayerId.One), DuelError.WrongPhase);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void RejectsInvalidMonsterZones(int zone)
    {
        var engine = Engine(); EnterMainPhase(engine);
        Reject(engine, new NormalSummon(PlayerId.One, 1, zone), DuelError.InvalidMonsterZone);
    }

    [Fact]
    public void CannotSummonOpponentsCardsOrCardsStillInDeck()
    {
        var engine = Engine(); EnterMainPhase(engine);
        Reject(engine, new NormalSummon(PlayerId.One, engine.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 0), DuelError.CardNotInHand);
        Reject(engine, new NormalSummon(PlayerId.One, engine.State.GetPlayer(PlayerId.One).Deck[0].InstanceId, 0), DuelError.CardNotInHand);
    }

    [Theory]
    [InlineData(CardCategory.Unknown, false, DuelError.UnknownCardRules)]
    [InlineData(CardCategory.Spell, false, DuelError.NotMonster)]
    [InlineData(CardCategory.Trap, false, DuelError.NotMonster)]
    [InlineData(CardCategory.Monster, false, DuelError.NormalSummonNotAllowed)]
    public void RejectsUnclassifiedAndIneligibleCards(CardCategory category, bool allowed, DuelError error)
    {
        var rules = Rules(); rules[1] = new(category, allowed);
        var engine = new DuelEngine(Catalog(), rules, Deck(), Deck()); EnterMainPhase(engine);
        Reject(engine, new NormalSummon(PlayerId.One, 1, 0), error);
    }

    [Fact]
    public void MissingRuleMetadataDoesNotGuessFromRawPropertyCodes()
    {
        var engine = new DuelEngine(Catalog(), new Dictionary<uint, DuelCardRules>(), Deck(), Deck()); EnterMainPhase(engine);
        Reject(engine, new NormalSummon(PlayerId.One, 1, 0), DuelError.UnknownCardRules);
    }

    [Theory]
    [InlineData(0u, DuelError.UnsupportedMonsterLevel)]
    [InlineData(5u, DuelError.TributeSummonNotSupported)]
    [InlineData(8u, DuelError.TributeSummonNotSupported)]
    public void RejectsUnsupportedLevelsAndTributeSummons(uint level, DuelError error)
    {
        var engine = new DuelEngine(Catalog(level), Rules(), Deck(), Deck()); EnterMainPhase(engine);
        Reject(engine, new NormalSummon(PlayerId.One, 1, 0), error);
    }

    [Fact]
    public void OccupiedAndFullFieldsCannotLoseOrOverwriteCards()
    {
        var engine = Engine();
        for (int zone = 0; zone < 5; zone++)
        {
            EnterMainPhase(engine);
            Ok(engine, new NormalSummon(PlayerId.One, engine.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, zone));
            Finish(engine); EnterMainPhase(engine); Finish(engine);
        }
        EnterMainPhase(engine);
        Reject(engine, new NormalSummon(PlayerId.One, engine.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0),
            DuelError.MonsterZoneOccupied);
        Assert.All(engine.State.GetPlayer(PlayerId.One).MonsterZones, monster => Assert.NotNull(monster));
        Assert.False(engine.State.NormalSummonUsed);
        AssertConservation(engine.State);
    }

    [Fact]
    public void EndPhaseRequiresChosenDiscardsAndPreservesGraveyardOrder()
    {
        var engine = Engine();
        EnterMainPhase(engine); Finish(engine); EnterMainPhase(engine); Finish(engine);
        EnterMainPhase(engine); Finish(engine); EnterMainPhase(engine); // Player two now has seven cards.
        Assert.Equal(7, engine.State.GetPlayer(PlayerId.Two).Hand.Count);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.End));
        Reject(engine, new EndTurn(PlayerId.Two), DuelError.HandLimitExceeded);
        Reject(engine, new DiscardForHandLimit(PlayerId.Two, -1), DuelError.CardNotInHand);
        var card = engine.State.GetPlayer(PlayerId.Two).Hand[2];
        Ok(engine, new DiscardForHandLimit(PlayerId.Two, card.InstanceId));
        Assert.Equal(card, Assert.Single(engine.State.GetPlayer(PlayerId.Two).Graveyard));
        Reject(engine, new DiscardForHandLimit(PlayerId.Two, engine.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId), DuelError.DiscardNotRequired);
        Ok(engine, new EndTurn(PlayerId.Two));
        EnterMainPhase(engine); Finish(engine); EnterMainPhase(engine);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.End));
        var next = engine.State.GetPlayer(PlayerId.Two).Hand[0];
        Ok(engine, new DiscardForHandLimit(PlayerId.Two, next.InstanceId));
        Assert.Equal(new[] { card, next }, engine.State.GetPlayer(PlayerId.Two).Graveyard);
        AssertConservation(engine.State);
    }

    [Fact]
    public void EmptyDeckLosesOnlyOnTheRequiredDrawAndTerminalStateRejectsActions()
    {
        var engine = Engine();
        while (engine.State.TurnNumber < 71) { EnterMainPhase(engine); Finish(engine); AssertConservation(engine.State); }
        EnterMainPhase(engine);
        Assert.Empty(engine.State.GetPlayer(PlayerId.One).Deck);
        Assert.Null(engine.State.Outcome); // Successfully drawing the last card is not a loss.
        Finish(engine);
        var result = Ok(engine, new ResolveDrawStep(PlayerId.Two));
        Assert.IsType<DuelEnded>(Assert.Single(result.Events));
        Assert.Equal(new DuelOutcome(PlayerId.One, PlayerId.Two, DuelEndReason.UnableToDraw), engine.State.Outcome);
        Reject(engine, new ResolveDrawStep(PlayerId.Two), DuelError.DuelFinished);
        Reject(engine, new EndTurn(PlayerId.Two), DuelError.DuelFinished);
        Reject(engine, new NormalSummon(PlayerId.Two, 42, 0), DuelError.DuelFinished);
        Reject(engine, new ChangePhase(PlayerId.Two, DuelPhase.End), DuelError.DuelFinished);
        Reject(engine, new DiscardForHandLimit(PlayerId.Two, 42), DuelError.DuelFinished);
        AssertConservation(engine.State);
    }

    [Fact]
    public void SameOrderedInputsAndCommandsReplayTheSameStatesAndEvents()
    {
        var first = Engine(); var second = Engine();
        DuelCommand[] commands = [new ResolveDrawStep(PlayerId.One), new ChangePhase(PlayerId.One, DuelPhase.Main1),
            new NormalSummon(PlayerId.One, 1, 2), new NormalSummon(PlayerId.One, 2, 1),
            new ChangePhase(PlayerId.One, DuelPhase.End), new EndTurn(PlayerId.One),
            new ResolveDrawStep(PlayerId.Two), new ChangePhase(PlayerId.Two, DuelPhase.Main1),
            new ChangePhase(PlayerId.Two, DuelPhase.Battle), new ChangePhase(PlayerId.Two, DuelPhase.Main2),
            new NormalSummon(PlayerId.Two, 41, 0)];
        foreach (var command in commands)
        {
            var a = first.Execute(command); var b = second.Execute(command);
            Assert.Equal(a.Succeeded, b.Succeeded);
            Assert.Equal(a.Error, b.Error);
            Assert.Equal(a.Events, b.Events);
            Assert.Equal(JsonSerializer.Serialize(a.State), JsonSerializer.Serialize(b.State));
        }
    }

    private static void AssertConservation(DuelState state)
    {
        var all = state.Players.SelectMany(p => p.Deck.Concat(p.Hand).Concat(p.Graveyard)
            .Concat(p.MonsterZones.OfType<FieldMonster>().Select(m => m.Card))).ToArray();
        Assert.Equal(80, all.Length);
        Assert.Equal(80, all.Select(c => c.InstanceId).Distinct().Count());
        Assert.Equal(40, all.Count(c => c.Owner == PlayerId.One));
        Assert.Equal(40, all.Count(c => c.Owner == PlayerId.Two));
    }

    private sealed record Unsupported(PlayerId Actor) : DuelCommand(Actor);
}
