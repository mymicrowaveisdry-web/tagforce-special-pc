using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Draw, search and mill family wiring through the production definitions.</summary>
public sealed class DeckOperationFamilyTests
{
    private const uint Filler = 4012, Pot = 4844, Jar = 5210, Upstart = 4895, Gift = 7234, Burial = 5236, Needle = 7769,
        Sage = 4872, Art = 7220, Inviting = 3505, Terraform = 5537, Poly = 4837, Stray = 3500, Forest = 4336, Big = 4007;

    private static CardCatalog Catalog() => new(new(null, "synthetic-deck-operations"),
        DeckOperationFamily.Cards.Select(c => c.CardId).Concat([Filler, Poly, Stray, Forest, Big, ChainCardFamily.MagicJammer])
            .Select(id => new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, id == Big ? 6u : id is Filler or Stray ? 4u : 0u))));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint>? second = null)
    {
        var rules = DeckOperationFamily.Rules.ToDictionary();
        foreach (uint monster in new[] { Filler, Stray, Big })
            rules[monster] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true);
        rules[Poly] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[Forest] = new(CardCategory.Spell);
        rules[ChainCardFamily.MagicJammer] = ChainCardFamily.Rules[ChainCardFamily.MagicJammer];
        return new(Catalog(), rules, first, second ?? Deck(), effectDefinitions: [.. DeckOperationFamily.Definitions,
            .. ChainCardFamily.Definitions.Where(d => d.CardId == ChainCardFamily.MagicJammer)], shuffleSeed: 11);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(Filler, 40 - top.Length)];
    private static CardInstance Hand(DuelEngine e, PlayerId p, uint id) => e.State.GetPlayer(p).Hand.First(c => c.GameCardId == id);

    private static void Activate(DuelEngine e, uint id, int zone = 0) =>
        Ok(e, new ActivateSpell(e.State.ActivePlayer, Hand(e, e.State.ActivePlayer, id).InstanceId, zone));

    /// <summary>Closes the window and resolves to the Deck card choice.</summary>
    private static PendingDefinitionCardChoice ReachChoice(DuelEngine e)
    {
        for (int i = 0; i < 2; i++) Ok(e, new PassChainResponse(e.State.PendingChain!.Responder, e.State.PendingChain.DecisionId));
        var paused = Ok(e, new ResolveNextChainLink(e.State.ActivePlayer, e.State.PendingChain!.DecisionId));
        return Assert.IsType<DefinitionCardChoiceRequested>(paused.Events[^1]).Decision;
    }

    [Fact]
    public void PotOfGreedDrawsTwoAndNeedsTwoCardsInTheDeck()
    {
        var e = Engine(Enumerable.Repeat(Pot, 40));
        EnterMain(e);
        var hand = e.State.GetPlayer(PlayerId.One).Hand.Count;
        Activate(e, Pot);
        var events = Resolve(e);
        Assert.Equal(2, events.OfType<CardDrawn>().Count());
        Assert.Equal(hand + 1, e.State.GetPlayer(PlayerId.One).Hand.Count); // one Pot used, two drawn
        while (e.State.GetPlayer(PlayerId.One).Deck.Count >= 2) { Activate(e, Pot); Resolve(e); }
        Assert.Single(e.State.GetPlayer(PlayerId.One).Deck);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Pot).InstanceId, 0), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void ADrawThatRunsOutAtResolutionLosesTheDuel()
    {
        var e = Engine([Jar, .. Enumerable.Repeat(Pot, 39)]);
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Jar).InstanceId, 4));
        while (e.State.GetPlayer(PlayerId.One).Deck.Count > 3) { Activate(e, Pot); Resolve(e); }
        Assert.Equal(3, e.State.GetPlayer(PlayerId.One).Deck.Count);
        Next(e); Next(e); // turn 3 draw leaves 2 cards
        Activate(e, Pot);
        Ok(e, new PassChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId));
        Ok(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 4)); // Jar of Greed draws first
        var events = Resolve(e);
        Assert.Equal(2, events.OfType<CardDrawn>().Count()); // Jar's card, then Pot's first card
        Assert.Equal(new DuelOutcome(PlayerId.Two, PlayerId.One, DuelEndReason.UnableToDraw), e.State.Outcome);
    }

    [Fact]
    public void UpstartGoblinDrawsBeforeTheOpponentGainsAndGiftCardOnlyGives()
    {
        var e = Engine(Deck(Upstart));
        EnterMain(e);
        Activate(e, Upstart);
        var events = Resolve(e);
        Assert.True(events.FindIndex(ev => ev is CardDrawn) < events.FindIndex(ev => ev is LifePointsGained));
        Assert.Equal((8000, 9000), (e.State.GetPlayer(PlayerId.One).LifePoints, e.State.GetPlayer(PlayerId.Two).LifePoints));

        var g = Engine(Deck(Gift));
        EnterMain(g);
        Ok(g, new SetSpellTrap(PlayerId.One, Hand(g, PlayerId.One, Gift).InstanceId, 0));
        Next(g); Next(g);
        Ok(g, new ActivateSetSpell(PlayerId.One, 0));
        Resolve(g);
        Assert.Equal(11000, g.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Theory]
    [InlineData(Sage, Poly)]
    [InlineData(Inviting, Stray)]
    [InlineData(Art, Big)]
    [InlineData(Terraform, Forest)]
    public void SearchesRevealOneMatchingCardAddItToTheHandAndShuffle(uint spell, uint target)
    {
        var e = Engine(Deck(spell, Filler, Filler, Filler, Filler, target, target));
        EnterMain(e);
        var deck = e.State.GetPlayer(PlayerId.One).Deck;
        Activate(e, spell);
        var choice = ReachChoice(e);
        Assert.All(choice.Options, c => Assert.Equal(target, c.GameCardId));
        Assert.Equal(2, choice.Options.Count);
        var source = new DuelActionSource(e, Catalog());
        Assert.Equal("DefinitionCard", source.Observe(PlayerId.One).Decision!.Kind);
        Assert.Null(source.Observe(PlayerId.Two).Decision); // Deck options stay private
        var filler = deck.First(c => c.GameCardId == Filler);
        Reject(e, new ChooseDefinitionCard(PlayerId.One, choice.DecisionId, filler.InstanceId), DuelError.InvalidDecisionChoice);
        Reject(e, new ChooseDefinitionCard(PlayerId.Two, choice.DecisionId, choice.Options[0].InstanceId), DuelError.NotActivePlayer);
        Reject(e, new ChooseDefinitionCard(PlayerId.One, choice.DecisionId - 1, choice.Options[0].InstanceId), DuelError.StaleDecision);
        Reject(e, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.DecisionPending);
        var result = Ok(e, new ChooseDefinitionCard(PlayerId.One, choice.DecisionId, choice.Options[1].InstanceId));
        Assert.Equal(choice.Options[1], Assert.Single(result.Events.OfType<CardSearched>()).Card);
        Assert.True(result.Events.ToList().FindIndex(ev => ev is CardSearched) < result.Events.ToList().FindIndex(ev => ev is DeckShuffled));
        Assert.Contains(choice.Options[1], e.State.GetPlayer(PlayerId.One).Hand);
        Assert.Equal(deck.Where(c => c != choice.Options[1]).OrderBy(c => c.InstanceId),
            e.State.GetPlayer(PlayerId.One).Deck.OrderBy(c => c.InstanceId));
    }

    [Fact]
    public void FoolishBurialOffersOnlyRuledMonstersAndSendsTheChoiceToTheGraveyard()
    {
        var e = Engine(Deck(Burial, Poly, Poly, Poly, Poly, Poly, Stray));
        EnterMain(e);
        Activate(e, Burial);
        var choice = ReachChoice(e);
        Assert.DoesNotContain(choice.Options, c => c.GameCardId == Poly);
        var stray = Assert.Single(choice.Options, c => c.GameCardId == Stray);
        var result = Ok(e, new ChooseDefinitionCard(PlayerId.One, choice.DecisionId, stray.InstanceId));
        Assert.Equal(stray, Assert.Single(result.Events.OfType<CardSentFromDeckToGraveyard>()).Card);
        Assert.Single(result.Events.OfType<DeckShuffled>());
        Assert.Equal([Stray, Burial], e.State.GetPlayer(PlayerId.One).Graveyard.Select(c => c.GameCardId));
    }

    [Fact]
    public void ASearchWithNoMatchLeftAtResolutionIsSkippedWithoutAShuffle()
    {
        // Turn 3: Fusion Sage, answered by a set Needlebug Nest that mills the only Polymerization.
        var e = Engine(Deck(Sage, Needle, Filler, Filler, Filler, Filler, Poly));
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Needle).InstanceId, 4));
        Next(e); Next(e);
        Assert.Equal(Poly, e.State.GetPlayer(PlayerId.One).Deck[0].GameCardId);
        Activate(e, Sage);
        Ok(e, new PassChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId));
        Ok(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 4));
        var events = Resolve(e);
        Assert.Equal(5, events.OfType<CardSentFromDeckToGraveyard>().Count());
        Assert.Contains(events, ev => ev is ChainLinkSkipped { Reason: ChainSkipReason.NoMatchingCard });
        Assert.Empty(events.OfType<DeckShuffled>());
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == Poly);

        var none = Engine(Deck(Sage)); // no "Polymerization" anywhere in the Deck
        EnterMain(none);
        Reject(none, new ActivateSpell(PlayerId.One, Hand(none, PlayerId.One, Sage).InstanceId, 0), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void NeedlebugNestMillsTheTopFiveInDeckOrder()
    {
        var e = Engine(Deck(Needle));
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Needle).InstanceId, 0));
        Next(e); Next(e);
        var top = e.State.GetPlayer(PlayerId.One).Deck.Take(5).ToArray();
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        var events = Resolve(e);
        Assert.Equal(top, events.OfType<CardSentFromDeckToGraveyard>().Select(m => m.Card));
        Assert.Equal(top, e.State.GetPlayer(PlayerId.One).Graveyard.Take(5));
    }

    [Fact]
    public void MagicJammerNegatesPotOfGreedSoNothingIsDrawn()
    {
        var e = Engine(Deck(Pot), Deck(ChainCardFamily.MagicJammer));
        EnterMain(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, PlayerId.Two, ChainCardFamily.MagicJammer).InstanceId, 0));
        Next(e);
        int deck = e.State.GetPlayer(PlayerId.One).Deck.Count;
        Activate(e, Pot);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        var cost = e.State.PendingDecision!;
        Ok(e, new ChooseDecisionCard(PlayerId.Two, cost.DecisionId, cost.CardOptions[0].InstanceId));
        var events = Resolve(e);
        Assert.Empty(events.OfType<CardDrawn>());
        Assert.Equal(deck, e.State.GetPlayer(PlayerId.One).Deck.Count);
    }

    [Fact]
    public void StepsFiltersAndSeedRequirementsValidate()
    {
        Assert.Throws<ArgumentException>(() => new CardIdFilter([]));
        Assert.Throws<ArgumentException>(() => new CardIdFilter([1, 1]));
        Assert.Equal(new CardIdFilter([1, 2]), new CardIdFilter([1, 2]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new DrawCardsStep(0)]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new MillTopOfDeckStep(61)]));
        var rules = new Dictionary<uint, DuelCardRules> { [Filler] = new(CardCategory.Monster, true), [Sage] = new(CardCategory.Spell, AllowsSpellTrapSet: true) };
        Assert.Throws<ArgumentException>(() => new DuelEngine(Catalog(), rules, Deck(), Deck(),
            effectDefinitions: DeckOperationFamily.Definitions.Where(d => d.CardId == Sage))); // a search shuffles: seed required
        Assert.Equal(10, DeckOperationFamily.Cards.Count);
        Assert.Equal(114, DeckOperationFamily.FieldSpellIds.Distinct().Count());
        Assert.All(DeckOperationFamily.HighLevelNormalMonsterIds, id => Assert.Contains(id, TfspNormalMonsters.Ids));
    }
}
