using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Resolution choices stay private and immutable until every participant has chosen one simultaneous discard.</summary>
public sealed class ChosenDiscardStepTests
{
    private const uint ACard = 901, BCard = 902, Ordinary = 903, Synthetic = 910, Response = 911,
        Charity = 4812, BothSpell = 912, Fine = 7479;

    private static (DuelEngine Engine, CardCatalog Catalog) Make(uint source = Charity, bool watchers = true,
        bool shrink = false, bool bothResponse = false, EffectStep[]? steps = null, EffectCost? cost = null,
        bool thinHand = false, bool redirect = false, bool emptyHand = false, TriggerEventCause? cause = null)
    {
        uint[] ids = [ACard, BCard, Ordinary, Synthetic, Response, Charity, BothSpell, Fine];
        var catalog = new CardCatalog(new(null, "synthetic-chosen-discards"), ids.Select(id =>
            new CardDefinition(id, id == ACard ? "Secret A" : id == BCard ? "Secret B" : $"Probe {id}", "", new(1000, 1000, 4))));
        TriggerEffectProfile Watch(string id, uint card, int amount) => cause is { } selected
            ? new TriggerEffectProfile(new EffectDefinition(id, card, EffectKind.MonsterTrigger, [new GainLifePointsStep(amount)]),
                MonsterEventKind.SentToGraveyard, EffectRelation.Self, TriggerRequirement.Mandatory, TriggerSourceLocation.GraveyardSelf, cause: selected)
            : Gain(id, card, MonsterEventKind.SentToGraveyard, gain: amount, location: TriggerSourceLocation.GraveyardSelf);
        var family = ShortTextCardFamily.Family;
        var rules = family.Rules.Where(p => p.Key is Charity or Fine).ToDictionary();
        foreach (var id in new[] { ACard, BCard, Ordinary }) rules[id] = new(CardCategory.Monster, AllowsNormalSummon: true);
        rules[Synthetic] = rules[BothSpell] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[Response] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        EffectDefinition response = bothResponse
            ? new("response", Response, EffectKind.NormalTrap, [new DiscardCardsStep(2, EffectPlayers.Both)])
            : shrink ? new("response", Response, EffectKind.NormalTrap,
                [.. Enumerable.Repeat<EffectStep>(new DiscardRandomStep(TargetSide.Opponent), 10),
                    .. emptyHand ? Array.Empty<EffectStep>() : [new DrawCardsStep(1, EffectPlayers.Opponent)]])
            : ResponseProbe.Negate("response", Response, ResponseNegation.Effect, ChainSpellSpeed.Two);
        var e = new DuelEngine(catalog, rules,
            thinHand ? [Fine, ACard, Synthetic, Synthetic, Synthetic, Synthetic, .. Enumerable.Repeat(Ordinary, 34)]
                : [source, ACard, BCard, Ordinary, Ordinary, .. Enumerable.Repeat(Ordinary, 35)],
            [Response, Ordinary, Ordinary, Ordinary, Ordinary, .. Enumerable.Repeat(Ordinary, 35)],
            experimentalTriggerEffects: watchers
                ? [Watch("discard-a", ACard, 100),
                   Watch("discard-b", BCard, 200)] : [],
            effectDefinitions:
            [.. family.Definitions.Where(d => d.CardId is Charity or Fine),
                new("synthetic", Synthetic, EffectKind.NormalSpell, steps ?? [new DiscardCardsStep(2)], cost: cost),
                new("synthetic-both", BothSpell, EffectKind.NormalSpell, [new DrawCardsStep(1, EffectPlayers.Both), new DiscardCardsStep(1, EffectPlayers.Both)]), response],
            graveyardRedirects: redirect ? [new("banisher", Ordinary, new GraveyardRedirect())] : [], shuffleSeed: 8);
        return (e, catalog);
    }

    private static int Hand(DuelEngine e, uint id, PlayerId player = PlayerId.One) =>
        e.State.GetPlayer(player).Hand.First(c => c.GameCardId == id).InstanceId;

    private static List<DuelEvent> ToChoice(DuelEngine e)
    {
        List<DuelEvent> events = [];
        while (e.State.PendingChain is { DefinitionCardChoice: null } chain)
            events.AddRange(chain.NextLink == 0 ? Ok(e, new PassChainResponse(chain.Responder, chain.DecisionId)).Events : Resolve(e).Events);
        return events;
    }

    private static DuelActionResult Pick(DuelEngine e, uint? id = null)
    {
        var p = e.State.PendingChain!.DefinitionCardChoice!;
        var card = id is { } wanted ? p.Options.First(c => c.GameCardId == wanted) : p.Options[0];
        return Ok(e, new ChooseDefinitionCard(p.Player, p.DecisionId, card.InstanceId));
    }

    private static void ReadyResponse(DuelEngine e, uint source)
    {
        EnterMain(e);
        if (source == Fine) Set(e, Fine);
        NextMain(e); Set(e, Response); NextMain(e);
        if (source == Fine) Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        else Ok(e, new ActivateSpell(PlayerId.One, Hand(e, source), 0));
    }

    [Fact]
    public void DrawsOnceThenCollectsTwoChoicesWithoutMovingEitherUntilTheLastAnswer()
    {
        var (e, _) = Make(); EnterMain(e);
        var deck = e.State.GetPlayer(PlayerId.One).Deck.Count;
        var source = Hand(e, Charity);
        Ok(e, new ActivateSpell(PlayerId.One, source, 0));
        var drawEvents = ToChoice(e);
        Assert.Equal(deck - 3, e.State.GetPlayer(PlayerId.One).Deck.Count);
        var old = e.State; var saved = JsonSerializer.Serialize(old);
        var first = e.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == ACard);
        var p = e.State.PendingChain!.DefinitionCardChoice!;
        Reject(e, new ChooseDefinitionCard(p.Player, p.DecisionId - 1, first.InstanceId), DuelError.StaleDecision);
        Reject(e, new ChooseDefinitionCard(PlayerId.Two, p.DecisionId, first.InstanceId), DuelError.NotActivePlayer);
        Reject(e, new ChooseDefinitionCard(p.Player, p.DecisionId, source), DuelError.InvalidDecisionChoice);
        var partial = Pick(e, ACard);
        Assert.Empty(partial.Events.OfType<CardDiscarded>());
        Assert.Empty(e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Contains(first, e.State.GetPlayer(PlayerId.One).Hand);
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Equal(saved, JsonSerializer.Serialize(old));
        var next = e.State.PendingChain!.DefinitionCardChoice!;
        Assert.Single(next.Picks); Assert.Empty(p.Picks);
        Reject(e, new ChooseDefinitionCard(next.Player, next.DecisionId, first.InstanceId), DuelError.InvalidDecisionChoice);
        var final = Pick(e, BCard);
        Assert.Equal(2, final.Events.OfType<CardDiscarded>().Count());
        Assert.Empty(final.Events.OfType<ActivationCostPaid>());
        Assert.Equal(deck - 3, e.State.GetPlayer(PlayerId.One).Deck.Count);
        var triggers = e.State.EventEffects.PendingTriggers!.Options;
        Assert.Equal(2, triggers.Count);
        Assert.All(triggers, t => Assert.False(t.Occurrence.IsActivationCost));
        Assert.Single(triggers.Select(t => t.Occurrence.TimingPoint).Distinct());
        Drain(e); Assert.Equal(8300, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.NotEmpty(drawEvents);
    }

    [Fact]
    public void BothPlayersChooseTheirOwnCardsWithoutExposingTheFirstSelection()
    {
        var (e, catalog) = Make(BothSpell, watchers: false); EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, BothSpell), 0)); ToChoice(e);
        var views = new DuelActionSource(e, catalog);
        Assert.Null(views.Observe(PlayerId.Two).Decision);
        var secret = e.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == ACard);
        var partial = Pick(e, ACard);
        Assert.Empty(partial.Events.OfType<CardDiscarded>());
        Assert.Contains(secret, e.State.GetPlayer(PlayerId.One).Hand);
        Assert.Equal(PlayerId.Two, e.State.PendingChain!.DefinitionCardChoice!.Player);
        var second = views.Observe(PlayerId.Two);
        Assert.Equal(PlayerId.Two, second.Actor);
        Assert.NotNull(second.Decision);
        Assert.DoesNotContain("Secret A", JsonSerializer.Serialize(second));
        Assert.Null(views.Observe(PlayerId.One).Decision);
        var result = Pick(e);
        Assert.Equal(new[] { PlayerId.One, PlayerId.Two }, result.Events.OfType<CardDiscarded>().Select(x => x.Player));
        Assert.Null(e.State.PendingChain);
    }

    [Fact]
    public void BothPlayerDiscardUsesTurnPlayerOrderWhenTheControllerIsTheOpponent()
    {
        var (e, _) = Make(watchers: false, bothResponse: true); ReadyResponse(e, Charity);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0)); ToChoice(e);
        Assert.Equal("response", e.State.PendingChain!.DefinitionCardChoice!.EffectId);
        Assert.Equal(PlayerId.One, e.State.PendingChain.DefinitionCardChoice.Player);
        var first = Pick(e); Assert.Empty(first.Events.OfType<CardDiscarded>());
        var second = Pick(e); Assert.Empty(second.Events.OfType<CardDiscarded>());
        Assert.Equal(PlayerId.Two, e.State.PendingChain.DefinitionCardChoice!.Player);
        Pick(e); var final = Pick(e);
        Assert.Equal(4, final.Events.OfType<CardDiscarded>().Count());
        Drain(e);
    }

    [Theory]
    [InlineData(Charity)]
    [InlineData(BothSpell)]
    public void NegationNeverDrawsOrRequestsDiscardChoices(uint source)
    {
        var (e, _) = Make(source, watchers: false); ReadyResponse(e, source);
        var hands = e.State.Players.Select(p => p.Hand.Count).ToArray();
        var decks = e.State.Players.Select(p => p.Deck.Count).ToArray();
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        var events = Drain(e);
        Assert.Empty(events.OfType<DefinitionCardChoiceRequested>());
        Assert.Empty(events.OfType<CardDiscarded>());
        Assert.Equal(hands, e.State.Players.Select(p => p.Hand.Count));
        Assert.Equal(decks, e.State.Players.Select(p => p.Deck.Count));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandShrinkingInTheChainDiscardsOnlyTheCardsLeftAtResolution(bool empty)
    {
        var (e, _) = Make(Fine, watchers: false, shrink: true, emptyHand: empty); ReadyResponse(e, Fine);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        var events = ToChoice(e);
        if (empty)
        {
            Assert.Empty(events.OfType<DefinitionCardChoiceRequested>());
            Assert.Null(e.State.PendingChain);
            return;
        }
        Assert.Single(e.State.PendingChain!.DefinitionCardChoice!.Options);
        var result = Pick(e);
        Assert.Single(result.Events.OfType<CardDiscarded>());
        Assert.Empty(e.State.GetPlayer(PlayerId.One).Hand);
        Assert.Null(e.State.PendingChain);
    }

    [Fact]
    public void HandCostsAreCountedBeforeAllowingAnEffectDiscard()
    {
        var (e, _) = Make(Synthetic, cost: new DiscardCost(Count: 4)); EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Synthetic), 0), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void ASetDiscardTrapCannotActivateWithOnlyOneCardLeftInHand()
    {
        var (e, _) = Make(thinHand: true); EnterMain(e);
        Set(e, Fine, 0); Set(e, Synthetic, 1); Set(e, Synthetic, 2); Set(e, Synthetic, 3);
        NextMain(e); NextMain(e); Set(e, Synthetic, 4);
        Assert.Single(e.State.GetPlayer(PlayerId.One).Hand);
        Reject(e, new ActivateSetSpell(PlayerId.One, 0), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void OpponentOnlyDiscardIsChosenByThatOpponent()
    {
        var (e, _) = Make(Synthetic, watchers: false, steps: [new DiscardCardsStep(1, EffectPlayers.Opponent)]);
        EnterMain(e); Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Synthetic), 0)); ToChoice(e);
        var p = e.State.PendingChain!.DefinitionCardChoice!;
        Assert.Equal(PlayerId.Two, p.Player);
        Assert.All(p.Options, c => Assert.Equal(PlayerId.Two, c.Owner));
        var result = Pick(e);
        Assert.Equal(PlayerId.Two, Assert.Single(result.Events.OfType<CardDiscarded>()).Player);
    }

    [Fact]
    public void DiscardedCardsUseExistingRedirectsAndDoNotTriggerGraveyardOnlyEffects()
    {
        var (e, _) = Make(Synthetic, redirect: true); EnterMain(e); Summon(e, Ordinary);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Synthetic), 0)); ToChoice(e);
        Pick(e, ACard); var result = Pick(e, BCard);
        Assert.All(result.Events.OfType<CardDiscarded>(), x => Assert.Equal(FieldCardDestination.Banished, x.FieldDestination));
        Assert.Empty(e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Null(e.State.EventEffects.PendingTriggers);
    }

    [Fact]
    public void ConsecutiveDrawAndDiscardStepsKeepTheirOwnSelectionsAndResumeOnce()
    {
        var (e, _) = Make(Synthetic, watchers: false,
            steps: [new DrawCardsStep(1), new DiscardCardsStep(1), new DrawCardsStep(1), new DiscardCardsStep(1)]);
        EnterMain(e); var deck = e.State.GetPlayer(PlayerId.One).Deck.Count;
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Synthetic), 0)); ToChoice(e);
        Pick(e, ACard);
        Assert.Empty(e.State.PendingChain!.DefinitionCardChoice!.Picks);
        Assert.Equal(deck - 2, e.State.GetPlayer(PlayerId.One).Deck.Count);
        Pick(e, BCard);
        Assert.Null(e.State.PendingChain);
        Assert.Equal(2, e.State.GetPlayer(PlayerId.One).Graveyard.Count(c => c.GameCardId is ACard or BCard));
    }

    [Fact]
    public void DiscardCountsAndPlayerSelectorsAreValidated()
    {
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", Synthetic, EffectKind.NormalSpell, [new DiscardCardsStep(0)]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", Synthetic, EffectKind.NormalSpell, [new DiscardCardsStep(6)]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", Synthetic, EffectKind.NormalSpell, [new DiscardCardsStep(1, (EffectPlayers)99)]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FilteredUpToDiscardsFinishOnlyAfterMinimumAndPublishTogether(int count)
    {
        var (e, catalog) = Make(Synthetic, watchers: false,
            steps: [new DiscardCardsStep(3, Filter: new AnyMonsterFilter(), Minimum: 1)]);
        EnterMain(e); Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Synthetic), 0)); ToChoice(e);
        var first = e.State.PendingChain!.DefinitionCardChoice!;
        Assert.False(first.CanFinish);
        Reject(e, new FinishDefinitionCardChoice(first.Player, first.DecisionId), DuelError.InvalidDecisionChoice);
        for (int i = 0; i < count; i++)
        {
            var result = Pick(e);
            if (i < 2) Assert.Empty(result.Events.OfType<CardDiscarded>());
        }
        if (count < 3)
        {
            var p = e.State.PendingChain!.DefinitionCardChoice!;
            Assert.True(p.CanFinish);
            Assert.Contains(new DuelActionSource(e, catalog).Observe(PlayerId.One).Actions, a => a.Label == "Finish choosing cards");
            Reject(e, new FinishDefinitionCardChoice(PlayerId.Two, p.DecisionId), DuelError.NotActivePlayer);
            Reject(e, new FinishDefinitionCardChoice(p.Player, p.DecisionId-1), DuelError.StaleDecision);
            var finished = Ok(e, new FinishDefinitionCardChoice(p.Player, p.DecisionId));
            Assert.Equal(count, finished.Events.OfType<CardDiscarded>().Count());
            Assert.All(finished.Events.OfType<CardDiscarded>(), d => Assert.Equal(PlayerId.One, d.EffectController));
        }
        Assert.Equal(count, e.State.GetPlayer(PlayerId.One).Graveyard.Count(c => c.GameCardId != Synthetic));
        Drain(e);
    }

    [Theory]
    [InlineData(TriggerEventCause.ByDiscardEffect, 8300)]
    [InlineData(TriggerEventCause.ByOpponentDiscardEffect, 8000)]
    public void SelfEffectDiscardsHaveControllerProvenanceAndNarrowTriggers(TriggerEventCause cause, int life)
    {
        var (e, _) = Make(Synthetic, cause: cause); EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Synthetic), 0)); ToChoice(e);
        Pick(e, ACard); Pick(e, BCard); Drain(e);
        Assert.Equal(life, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void OpponentRandomEffectDiscardsRetainControllerAndTriggerAfterResolution()
    {
        var (e, _) = Make(Fine, shrink: true, emptyHand: true, cause: TriggerEventCause.ByOpponentDiscardEffect);
        ReadyResponse(e, Fine);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        var events = Drain(e);
        Assert.All(events.OfType<CardDiscarded>(), d => Assert.Equal(PlayerId.Two, d.EffectController));
        Assert.Equal(8300, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void FilterAndMinimumRejectUnavailableOrInvalidDeclarations()
    {
        var (e, _) = Make(Synthetic, steps: [new DiscardCardsStep(1, Filter: new CardCategoryFilter(CardCategory.Trap))]);
        EnterMain(e); Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Synthetic), 0), DuelError.DecisionUnavailable);
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", Synthetic, EffectKind.NormalSpell, [new DiscardCardsStep(2, Minimum: 3)]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", Synthetic, EffectKind.NormalSpell,
            [new DrawCardsStep(1), new DiscardCardsStep(1, Filter: new AnyMonsterFilter())]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", Synthetic, EffectKind.NormalSpell,
            [new DiscardCardsStep(1, Filter: new AnyMonsterFilter())], cost: new DiscardCost()));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", Synthetic, EffectKind.NormalSpell, [new DiscardCardsStep(2, EffectPlayers.Both, Minimum: 1)]));
    }

    [Fact]
    public void HandLimitDiscardNeverMasqueradesAsAnEffectDiscard()
    {
        var (e, _) = Make(Synthetic, cause: TriggerEventCause.ByDiscardEffect, steps: [new DrawCardsStep(3)]);
        EnterMain(e); Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Synthetic), 0)); Drain(e);
        Phase(e, DuelPhase.End);
        var result = Ok(e, new DiscardForHandLimit(PlayerId.One, Hand(e, ACard)));
        Assert.Null(Assert.Single(result.Events.OfType<CardDiscarded>()).EffectController);
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void RedirectedEffectDiscardsDoNotCreateGraveyardDiscardTriggers()
    {
        var (e, _) = Make(Synthetic, redirect: true, cause: TriggerEventCause.ByDiscardEffect);
        EnterMain(e); Summon(e, Ordinary);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Synthetic), 0)); ToChoice(e);
        Pick(e, ACard); var result = Pick(e, BCard);
        Assert.All(result.Events.OfType<CardDiscarded>(), d => Assert.Equal(PlayerId.One, d.EffectController));
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
    }
}
