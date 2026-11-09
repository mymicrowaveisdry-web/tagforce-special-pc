using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tests;

/// <summary>
/// Compares Emergency Teleport with recorded projections, plus guard, event and identity tests. Deck order differs
/// because of the seeded shuffle.
/// </summary>
public sealed class EmergencyTeleportTests
{
    internal static EffectDefinition Definition => Assert.Single(EventEffectScenario.ChainCards(ChainCardFamily.EmergencyTeleport));

    private static DuelEngine Engine(bool enabled = true, bool candidate = true, bool extraSpell = false,
        PlayerId starter = PlayerId.One)
    {
        uint[] ids = [1, 9979, 7716, 6151, 7747, 9723, 5217];
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-spell-reference-projection"),
            ids.Select(id => new CardDefinition(id, $"Synthetic reference input {id}", "",
                new(id == 7716 ? 1200 : 1000, id == 7716 ? 400 : 1000, id == 7716 ? 2u : 4u),
                new(0, 0, 0, 0, 0, 0, 0), new((int)id, id, (id == 7716 ? 2u : 4u) << 11, 0))));
        var rules = ids.ToDictionary(id => id, id => id is 7747 or 5217
            ? new DuelCardRules(CardCategory.Spell, AllowsSpellTrapSet: true)
            : new DuelCardRules(CardCategory.Monster, AllowsNormalSummon: true));
        uint[] deck = [extraSpell ? 7747u : 9979u, 7716, 9979, 6151, 7747,
            candidate ? 7716u : 1u, 9723, 5217, 1, candidate ? 7716u : 1u, .. Enumerable.Repeat(1u, 30)];
        return new(catalog, rules, deck, deck, starter, effectDefinitions: enabled ? [Definition] : [], shuffleSeed: 7);
    }

    private static DuelActionResult Ok(DuelEngine engine, DuelCommand command)
    {
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
        Assert.Same(engine.State, result.State);
        return result;
    }

    private static void Reject(DuelEngine engine, DuelCommand command, DuelError error)
    {
        var before = engine.State;
        string serialized = JsonSerializer.Serialize(before);
        var result = engine.Execute(command);
        Assert.False(result.Succeeded);
        Assert.Equal(error, result.Error);
        Assert.Same(before, engine.State);
        Assert.Same(before, result.State);
        Assert.Equal(serialized, JsonSerializer.Serialize(engine.State));
        Assert.Empty(result.Events);
    }

    private static void EnterMain(DuelEngine engine)
    {
        var actor = engine.State.ActivePlayer;
        Ok(engine, new ResolveDrawStep(actor));
        Ok(engine, new ChangePhase(actor, DuelPhase.Main1));
    }

    private static void NextMain(DuelEngine engine)
    {
        var actor = engine.State.ActivePlayer;
        Ok(engine, new ChangePhase(actor, DuelPhase.End));
        while (engine.State.GetPlayer(actor).Hand.Count > 6)
            Ok(engine, new DiscardForHandLimit(actor, engine.State.GetPlayer(actor).Hand[^1].InstanceId));
        Ok(engine, new EndTurn(actor));
        EnterMain(engine);
    }

    private static CardInstance Spell(DuelEngine engine) => engine.State.GetPlayer(engine.State.ActivePlayer)
        .Hand.First(c => c.GameCardId == ChainCardFamily.EmergencyTeleport);

    private static void Activate(DuelEngine engine, int zone = 2) =>
        Ok(engine, new ActivateSpell(engine.State.ActivePlayer, Spell(engine).InstanceId, zone));

    /// <summary>Closes the response window and resolves to the summon choice.</summary>
    internal static PendingDefinitionSummon ReachChoice(DuelEngine engine)
    {
        for (int i = 0; i < 2; i++) Ok(engine, new PassChainResponse(engine.State.PendingChain!.Responder, engine.State.PendingChain.DecisionId));
        var paused = Ok(engine, new ResolveNextChainLink(engine.State.ActivePlayer, engine.State.PendingChain!.DecisionId));
        return Assert.IsType<DefinitionSummonRequested>(paused.Events[^1]).Decision;
    }

    /// <summary>Activates and resolves, choosing the first Deck copy.</summary>
    private static DuelActionResult Resolve(DuelEngine engine, int zone = 1)
    {
        Activate(engine);
        return Finish(engine, zone);
    }

    /// <summary>Resolves an activation already on the chain, choosing the first Deck copy.</summary>
    private static DuelActionResult Finish(DuelEngine engine, int zone)
    {
        var choice = ReachChoice(engine);
        var deckCopy = choice.Options.First(o => o.Source == DecisionCardSource.Deck).Card;
        return Ok(engine, new ChooseDefinitionSummon(choice.Player, choice.DecisionId, deckCopy.InstanceId, zone, BattlePosition.FaceUpAttack));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("Category", "LocalReference")]
    public void ActivationAndResolutionMatchRepeatedOriginalProjection(int repeat)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "emergency-teleport.json")));
        var fixture = doc.RootElement;
        Assert.Equal(2, fixture.GetProperty("repeatCount").GetInt32());
        Assert.False(fixture.GetProperty("nativeMatchClaim").GetBoolean());
        var pair = fixture.GetProperty("captures")[repeat];
        var snapshots = fixture.GetProperty("snapshots");
        JsonElement Projection(string stage) => snapshots.GetProperty(pair.GetProperty(stage).GetString()!).GetProperty("projection");
        var engine = Engine();
        EnterMain(engine);
        while (engine.State.TurnNumber < 7)
        {
            if (engine.State.ActivePlayer == PlayerId.One && engine.State.TurnNumber is 1 or 3)
            {
                uint id = engine.State.TurnNumber == 1 ? 7716u : 6151u;
                var card = engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == id);
                Ok(engine, new NormalSummon(PlayerId.One, card.InstanceId, engine.State.TurnNumber == 1 ? 2 : 3));
            }
            NextMain(engine);
        }
        var before = engine.State;
        AssertProjection(Projection("before"), before);
        var spell = Spell(engine);
        var handKrebons = before.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == 7716);
        var originalDeck = before.GetPlayer(PlayerId.One).Deck;
        var activated = Ok(engine, new ActivateSpell(PlayerId.One, spell.InstanceId, 2));
        Assert.Collection(activated.Events, e => Assert.Equal(spell, Assert.IsType<SpellActivated>(e).Card),
            e => Assert.IsType<ActivationCommitted>(e), e => Assert.IsType<ChainLinkAdded>(e), e => Assert.IsType<ChainResponseRequested>(e));
        AssertProjection(Projection("placed"), engine.State);
        Assert.True(engine.State.GetPlayer(PlayerId.One).SpellTrapFields[2]!.IsFaceUp);
        Assert.False(before.NormalSummonUsed);
        var p = ReachChoice(engine);
        AssertProjection(Projection("placed"), engine.State);
        // Hand and Deck copies are both eligible; the original capture took the Deck branch.
        Assert.Equal([handKrebons], p.Options.Where(o => o.Source == DecisionCardSource.Hand).Select(o => o.Card));
        var chosen = Assert.Single(p.Options, o => o.Source == DecisionCardSource.Deck).Card;
        Assert.Equal(7716u, chosen.GameCardId);
        var result = Ok(engine, new ChooseDefinitionSummon(PlayerId.One, p.DecisionId, chosen.InstanceId, 1, BattlePosition.FaceUpAttack));
        AssertProjection(Projection("resolved"), engine.State);
        Assert.Collection(result.Events, e => Assert.Same(chosen, Assert.IsType<MonsterSpecialSummoned>(e).Monster.Card),
            e => Assert.Equal(chosen, Assert.IsType<EndPhaseBanishmentScheduled>(e).Banishment.Monster),
            e => Assert.IsType<DeckShuffled>(e), e => Assert.IsType<ChainLinkResolved>(e),
            e => Assert.Same(spell, Assert.IsType<SpellSentToGraveyard>(e).Card), e => Assert.IsType<ChainCompleted>(e));
        var player = engine.State.GetPlayer(PlayerId.One);
        Assert.Same(chosen, player.MonsterZones[1]!.Card);
        Assert.Equal(BattlePosition.FaceUpAttack, player.MonsterZones[1]!.Position);
        Assert.Equal(engine.State.TurnNumber, player.MonsterZones[1]!.SummonedOnTurn);
        Assert.Contains(handKrebons, player.Hand);
        Assert.Same(spell, player.Graveyard[^1]);
        // Same remaining cards, in the seeded shuffle's order.
        Assert.Equal(originalDeck.Where(c => c.InstanceId != chosen.InstanceId).OrderBy(c => c.InstanceId), player.Deck.OrderBy(c => c.InstanceId));
        Assert.Same(before.GetPlayer(PlayerId.Two), engine.State.GetPlayer(PlayerId.Two));
        Assert.Equal(before.GetPlayer(PlayerId.One).LifePoints, player.LifePoints);
        Assert.Equal(before.NormalSummonUsed, engine.State.NormalSummonUsed);
        Assert.Equal(Projection("before").GetProperty("players")[0].GetProperty("lifePoints").GetInt32(),
            Projection("resolved").GetProperty("players")[0].GetProperty("lifePoints").GetInt32());
        Assert.Null(engine.State.PendingDecision);
        Assert.Null(engine.State.PendingChain);
        Assert.False(engine.State.ResearchSliceComplete);
        Assert.Null(engine.State.Outcome);
        AssertConservation(engine.State);
    }

    private static void AssertProjection(JsonElement original, DuelState managed)
    {
        Assert.Equal(original.GetProperty("activePlayer").GetInt32(), (int)managed.ActivePlayer);
        Assert.Equal(original.GetProperty("turnNumber").GetInt32(), managed.TurnNumber);
        Assert.Equal(original.GetProperty("phase").GetString(), managed.Phase.ToString());
        var p = original.GetProperty("players")[0];
        var q = managed.GetPlayer(PlayerId.One);
        IEnumerable<uint> Ids(string field) => p.GetProperty(field).EnumerateArray().Select(x => x.GetUInt32());
        Assert.Equal(p.GetProperty("handCount").GetInt32(), q.Hand.Count);
        Assert.Equal(p.GetProperty("deckCount").GetInt32(), q.Deck.Count);
        Assert.Equal(Ids("handCardIds"), q.Hand.Select(c => c.GameCardId));
        Assert.Equal(Ids("monsterCardIds"), q.MonsterZones.Select(m => m?.Card.GameCardId ?? 0));
        Assert.Equal(Ids("spellTrapCardIds"), q.SpellTrapZones.Select(c => c?.GameCardId ?? 0));
        Assert.Equal(Ids("graveyardCardIds"), q.Graveyard.Select(c => c.GameCardId));
        Assert.Equal(0, original.GetProperty("endLatchRaw").GetInt32());
        Assert.Equal(0, original.GetProperty("outcomeRaw").GetInt32());
        Assert.Null(managed.Outcome);
        // Absolute LP and opponent contents come from earlier effects.
    }

    [Fact]
    public void NoDefinitionMeansNoActivationEvenWithSettingAndSummoningRules()
    {
        var engine = Engine(enabled: false);
        EnterMain(engine);
        Reject(engine, new ActivateSpell(PlayerId.One, Spell(engine).InstanceId, 2), DuelError.UnsupportedSpellEffect);
        Assert.Null(engine.State.PendingChain);
    }

    [Fact]
    public void InvalidActivationAndMissingCandidateRejectBeforeCommitting()
    {
        var engine = Engine();
        Reject(engine, new ActivateSpell(PlayerId.One, Spell(engine).InstanceId, 2), DuelError.WrongPhase);
        EnterMain(engine);
        var spell = Spell(engine);
        Reject(engine, new ActivateSpell(PlayerId.Two, spell.InstanceId, 2), DuelError.NotActivePlayer);
        Reject(engine, new ActivateSpell(PlayerId.One, spell.InstanceId, -1), DuelError.InvalidSpellTrapZone);
        Reject(engine, new ActivateSpell(PlayerId.One, spell.InstanceId, 5), DuelError.InvalidSpellTrapZone);
        Reject(engine, new ActivateSpell(PlayerId.One, int.MaxValue, 2), DuelError.CardNotInHand);
        Reject(engine, new ActivateSpell(PlayerId.One, engine.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 2), DuelError.UnsupportedSpellEffect);
        Reject(engine, new BeginDecisionSequence(PlayerId.One, "activation:" + Definition.EffectId), DuelError.UnsupportedDecisionSequence);
        // Without Deck copies, the hand copy is the only candidate; once it is summoned nothing is eligible.
        engine = Engine(candidate: false);
        EnterMain(engine);
        Ok(engine, new NormalSummon(PlayerId.One, engine.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == 7716).InstanceId, 0));
        Reject(engine, new ActivateSpell(PlayerId.One, Spell(engine).InstanceId, 2), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void SetCardsStayFaceDownWhenAnotherSpellActivatesAndResolves()
    {
        var engine = Engine(extraSpell: true);
        EnterMain(engine);
        var set = Spell(engine);
        Ok(engine, new SetSpellTrap(PlayerId.One, set.InstanceId, 2));
        var before = engine.State;
        Reject(engine, new ActivateSpell(PlayerId.One, set.InstanceId, 0), DuelError.CardNotInHand);
        Reject(engine, new ActivateSpell(PlayerId.One, Spell(engine).InstanceId, 2), DuelError.SpellTrapZoneOccupied);
        Activate(engine, 0);
        var placed = engine.State;
        Assert.False(placed.GetPlayer(PlayerId.One).SpellTrapFields[2]!.IsFaceUp);
        Assert.True(placed.GetPlayer(PlayerId.One).SpellTrapFields[0]!.IsFaceUp);
        var choice = ReachChoice(engine);
        Ok(engine, new ChooseDefinitionSummon(PlayerId.One, choice.DecisionId, choice.Options[0].Card.InstanceId, 1, BattlePosition.FaceUpAttack));
        Assert.Same(set, engine.State.GetPlayer(PlayerId.One).SpellTrapZones[2]);
        Assert.False(engine.State.GetPlayer(PlayerId.One).SpellTrapFields[2]!.IsFaceUp);
        Assert.Null(engine.State.GetPlayer(PlayerId.One).SpellTrapFields[0]);
        Assert.Null(before.GetPlayer(PlayerId.One).SpellTrapFields[0]);
        Assert.True(placed.GetPlayer(PlayerId.One).SpellTrapFields[0]!.IsFaceUp);
        AssertConservation(engine.State);
    }

    [Fact]
    public void ResolutionChoiceRejectsForeignCardsPositionsZonesAndOtherCommandsAtomically()
    {
        var engine = Engine();
        EnterMain(engine);
        Activate(engine);
        var choice = ReachChoice(engine);
        var option = choice.Options[0].Card;
        var foreign = engine.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == 7716);
        Reject(engine, new ChooseDefinitionSummon(PlayerId.One, choice.DecisionId, foreign.InstanceId, 1, BattlePosition.FaceUpAttack), DuelError.InvalidDecisionChoice);
        Reject(engine, new ChooseDefinitionSummon(PlayerId.Two, choice.DecisionId, option.InstanceId, 1, BattlePosition.FaceUpAttack), DuelError.NotActivePlayer);
        Reject(engine, new ChooseDefinitionSummon(PlayerId.One, choice.DecisionId - 1, option.InstanceId, 1, BattlePosition.FaceUpAttack), DuelError.StaleDecision);
        Reject(engine, new ChooseDefinitionSummon(PlayerId.One, choice.DecisionId, option.InstanceId, 5, BattlePosition.FaceUpAttack), DuelError.InvalidMonsterZone);
        Reject(engine, new ChooseDefinitionSummon(PlayerId.One, choice.DecisionId, option.InstanceId, 1, BattlePosition.FaceDownDefense), DuelError.InvalidBattlePosition);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.DecisionPending);
        Reject(engine, new PassChainResponse(PlayerId.One, engine.State.PendingChain!.DecisionId), DuelError.DecisionPending);
        Assert.Empty(engine.State.PendingEndPhaseBanishments);
        var result = Ok(engine, new ChooseDefinitionSummon(PlayerId.One, choice.DecisionId, option.InstanceId, 1, BattlePosition.FaceUpDefense));
        Assert.Equal(BattlePosition.FaceUpDefense, engine.State.GetPlayer(PlayerId.One).MonsterZones[1]!.Position);
        Assert.Empty(result.Events.OfType<DeckShuffled>()); // a hand summon does not shuffle
        Assert.Single(engine.State.PendingEndPhaseBanishments);
    }

    [Theory]
    [InlineData(PlayerId.One)]
    [InlineData(PlayerId.Two)]
    public void SpecialSummonPreservesUsedAllowanceAndTheDuelContinues(PlayerId actor)
    {
        var engine = Engine(starter: actor);
        EnterMain(engine);
        var normal = engine.State.GetPlayer(actor).Hand.First(c => c.GameCardId == 7716);
        Ok(engine, new NormalSummon(actor, normal.InstanceId, 2));
        Activate(engine);
        var choice = ReachChoice(engine);
        Assert.DoesNotContain(2, choice.MonsterZoneOptions);
        var card = choice.Options[0].Card;
        Reject(engine, new ChooseDefinitionSummon(actor, choice.DecisionId, card.InstanceId, 2, BattlePosition.FaceUpAttack), DuelError.InvalidMonsterZone);
        Ok(engine, new ChooseDefinitionSummon(actor, choice.DecisionId, card.InstanceId, 1, BattlePosition.FaceUpAttack));
        Assert.True(engine.State.NormalSummonUsed);
        Assert.Null(engine.State.Outcome);
        AssertConservation(engine.State);
        Ok(engine, new ChangePhase(actor, DuelPhase.End));
        Assert.Equal(card, Assert.Single(engine.State.GetPlayer(actor).Banished));
        Ok(engine, new EndTurn(actor));
    }

    [Fact]
    public void FullMonsterFieldRejectsBeforeSpellLeavesHand()
    {
        var engine = Engine();
        EnterMain(engine);
        for (int zone = 0; zone < 5; zone++)
        {
            var card = engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId is not (7747 or 5217));
            Ok(engine, new NormalSummon(PlayerId.One, card.InstanceId, zone));
            if (zone < 4) { NextMain(engine); NextMain(engine); }
        }
        Reject(engine, new ActivateSpell(PlayerId.One, Spell(engine).InstanceId, 2), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void EffectStateAndEventsReplayDeterministically()
    {
        var a = Engine();
        var b = Engine();
        EnterMain(a); EnterMain(b);
        Activate(a); Activate(b);
        Assert.Equal(JsonSerializer.Serialize(a.State), JsonSerializer.Serialize(b.State));
        var first = Finish(a, 1);
        var second = Finish(b, 1);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public void DefinitionsRequireASeedSettableSpellRulesAndRuleBackedCandidates()
    {
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-effect-metadata"),
            new uint[] { 1, 7716, 7747 }.Select(id => new CardDefinition(id, $"Synthetic {id}", "",
                new(0, 0, 2), new(0, 0, 0, 0, 0, 0, 0), new((int)id, id, 2u << 11, 0))));
        uint[] deck = [7747, 7716, .. Enumerable.Repeat(1u, 38)];
        Dictionary<uint, DuelCardRules> Rules(bool settable, bool krebonsRules)
        {
            var rules = new Dictionary<uint, DuelCardRules>
            {
                [1] = new(CardCategory.Monster, true), [7747] = new(CardCategory.Spell, AllowsSpellTrapSet: settable)
            };
            if (krebonsRules) rules[7716] = new(CardCategory.Monster, true);
            return rules;
        }
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, Rules(true, true), deck, deck, effectDefinitions: [Definition]));
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, Rules(false, true), deck, deck, effectDefinitions: [Definition], shuffleSeed: 1));
        // A listed card without explicit Monster rules is never offered.
        var engine = new DuelEngine(catalog, Rules(true, false), deck, deck, effectDefinitions: [Definition], shuffleSeed: 1);
        EnterMain(engine);
        Reject(engine, new ActivateSpell(PlayerId.One, Spell(engine).InstanceId, 0), DuelError.DecisionUnavailable);
        var ruled = new DuelEngine(catalog, Rules(true, true), deck, deck, effectDefinitions: [Definition], shuffleSeed: 1);
        EnterMain(ruled);
        Ok(ruled, new ActivateSpell(PlayerId.One, Spell(ruled).InstanceId, 0));
    }

    private static void AssertConservation(DuelState state)
    {
        var cards = state.Players.SelectMany(p => p.Hand.Concat(p.Deck).Concat(p.Graveyard).Concat(p.Banished)
            .Concat(p.MonsterZones.OfType<FieldMonster>().Select(m => m.Card))
            .Concat(p.SpellTrapZones.OfType<CardInstance>())).ToArray();
        Assert.Equal(80, cards.Length);
        Assert.Equal(80, cards.Select(c => c.InstanceId).Distinct().Count());
    }
}
