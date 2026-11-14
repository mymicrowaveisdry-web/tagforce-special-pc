using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Synthetic destruction dependencies, including replacement payments and interrupted resolution choices.</summary>
public sealed class DestructionFollowupTests
{
    private const uint Lightning = 6512, Probe = 950, Monster = 951, Shield = 952, Response = 953, FillerCard = 954;

    private static DuelEngine Make(bool authored = false, bool guarded = true, bool protection = false,
        bool replacement = false, bool optional = false, bool redirect = false, bool thin = false,
        EffectStep? response = null, bool negate = false, int targets = 1)
    {
        uint[] ids = [Lightning, Probe, Monster, Shield, Response, FillerCard];
        var catalog = new CardCatalog(new(null, "synthetic-destruction-followups"), ids.Select(id =>
            new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, 4))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(
            id is Monster or FillerCard ? CardCategory.Monster : id == Response ? CardCategory.Trap : CardCategory.Spell,
            AllowsNormalSummon: id is Monster or FillerCard, AllowsBasicBattle: id is Monster or FillerCard,
            AllowsSpellTrapSet: id is not (Monster or FillerCard)));
        var source = authored ? Lightning : Probe;
        return new(catalog, rules,
            thin ? [source, .. Enumerable.Repeat(Shield, 39)] : [source, .. Enumerable.Repeat(FillerCard, 39)],
            [Monster, Shield, Response, Monster, FillerCard, .. Enumerable.Repeat(FillerCard, 35)],
            effectDefinitions:
            [ShortTextCardFamily.Family.Definitions.Single(d => d.CardId == Lightning),
                new("followup", Probe, EffectKind.NormalSpell,
                    [new DestroyTargetsStep(guarded), new GainLifePointsStep(123), new DiscardCardsStep(1)],
                    fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Opponent, count: targets)]),
                new("shield", Shield, thin ? EffectKind.NormalSpell : EffectKind.ContinuousSpell,
                    thin ? [new GainLifePointsStep(1)] : [], isMarker: !thin),
                negate ? ResponseProbe.Negate("response", Response, ResponseNegation.Effect)
                    : new("response", Response, EffectKind.NormalTrap, [response ?? new DestroyTargetsStep()],
                        fieldTargets: [new FieldTarget(FieldCardKind.Monster)])],
            ruleLocks: protection ? [new("protection", Shield,
                new RuleLock(LockedAction.BeDestroyedByEffect, TargetSide.Own))] : [],
            destructionReplacements: replacement ? [new("replacement", Shield, ReplacementSubject.Monsters,
                new ReplacementCost(ReplacementAction.DestroySource), Optional: optional)] : [],
            graveyardRedirects: redirect ? [new("redirect", Shield, new GraveyardRedirect())] : [], shuffleSeed: 17);
    }

    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(e.State.ActivePlayer).Hand.First(c => c.GameCardId == id).InstanceId;

    private static void Ready(DuelEngine e, bool authored = false, bool shield = false, bool response = false, int monsters = 1)
    {
        EnterMain(e); NextMain(e);
        Summon(e, Monster, set: authored);
        if (shield) { Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, Shield), 0)); Drain(e); }
        if (response) Set(e, Response, 1);
        NextMain(e);
        if (monsters == 2)
        {
            NextMain(e); Summon(e, Monster, 1); NextMain(e);
        }
    }

    private static void ChooseTarget(DuelEngine e, int zone = 0)
    {
        var p = e.State.PendingDecision!;
        var target = p.FieldTargetOptions!.Single(t => t.Player == PlayerId.Two && t.Location == FieldLocation.MonsterZone && t.Zone == zone);
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, target.Player, target.Location, target.Zone, target.Card.InstanceId));
    }

    private static void Activate(DuelEngine e, bool authored = false, int targets = 1)
    {
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, authored ? Lightning : Probe), 0));
        for (int zone = 0; zone < targets; zone++) ChooseTarget(e, zone);
    }

    private static List<DuelEvent> ToPause(DuelEngine e)
    {
        List<DuelEvent> events = [];
        for (int i = 0; i < 20 && e.State.PendingChain is { } chain; i++)
        {
            if (chain.DefinitionCardChoice is not null || chain.ReplacementChoice is not null) return events;
            events.AddRange(Ok(e, chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId)
                : new ResolveNextChainLink(e.State.ActivePlayer, chain.DecisionId)).Events);
        }
        return events;
    }

    [Fact]
    public void AuthoredSetTargetIsDestroyedBeforePrivateDiscardChoiceAndNeverPaidAsCost()
    {
        var e = Make(authored: true); Ready(e, authored: true);
        var target = e.State.GetPlayer(PlayerId.Two).MonsterZones[0]!.Card;
        var hand = e.State.GetPlayer(PlayerId.One).Hand.Count;
        Activate(e, authored: true);
        var events = ToPause(e);
        Assert.Single(events.OfType<MonsterDestroyed>());
        Assert.Contains(target, e.State.GetPlayer(PlayerId.Two).Graveyard);
        Assert.Empty(events.OfType<CardDiscarded>());
        Assert.Empty(events.OfType<ActivationCostPaid>());
        Assert.Equal(hand - 1, e.State.GetPlayer(PlayerId.One).Hand.Count);
        var choice = e.State.PendingChain!.DefinitionCardChoice!;
        Reject(e, new ChooseDefinitionCard(PlayerId.Two, choice.DecisionId, choice.Options[0].InstanceId), DuelError.NotActivePlayer);
        var picked = Ok(e, new ChooseDefinitionCard(choice.Player, choice.DecisionId, choice.Options[0].InstanceId));
        Assert.Single(picked.Events.OfType<CardDiscarded>());
        Assert.Empty(picked.Events.OfType<MonsterDestroyed>());
        Drain(e);
    }

    [Fact]
    public void DestroyingASetSpellTrapAlsoUnlocksTheDiscard()
    {
        var e = Make(authored: true); Ready(e, authored: true, response: true);
        var target = e.State.GetPlayer(PlayerId.Two).SpellTrapZones[1]!;
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Lightning), 0));
        var p = e.State.PendingDecision!;
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, PlayerId.Two, FieldLocation.SpellTrapZone, 1, target.InstanceId));
        var events = ToPause(e);
        Assert.Single(events.OfType<SpellTrapDestroyed>());
        Assert.Contains(target, e.State.GetPlayer(PlayerId.Two).Graveyard);
        Assert.NotNull(e.State.PendingChain!.DefinitionCardChoice);
        Drain(e);
    }

    [Fact]
    public void ProtectedTargetStopsFollowupWhileExistingUnguardedDestructionKeepsItsBehavior()
    {
        foreach (bool guarded in new[] { true, false })
        {
            var e = Make(guarded: guarded, protection: true); Ready(e, shield: true); Activate(e);
            var events = ToPause(e);
            Assert.Empty(events.OfType<MonsterDestroyed>());
            Assert.NotNull(e.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
            Assert.Equal(guarded ? 8000 : 8123, e.State.GetPlayer(PlayerId.One).LifePoints);
            Assert.Equal(!guarded, e.State.PendingChain?.DefinitionCardChoice is not null);
            Drain(e);
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void ReplacementPaymentDestructionDoesNotCountButDecliningReplacementAllowsFollowup(bool optional, bool use)
    {
        var e = Make(replacement: true, optional: optional); Ready(e, shield: true); Activate(e);
        var events = ToPause(e);
        if (optional)
        {
            var p = e.State.PendingChain!.ReplacementChoice!;
            events.AddRange(Ok(e, new ChooseDestructionReplacement(p.Player, p.DecisionId, use)).Events);
            events.AddRange(ToPause(e));
        }
        Assert.Equal(use ? 8000 : 8123, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(!use, e.State.PendingChain?.DefinitionCardChoice is not null);
        if (use)
        {
            Assert.Single(events.OfType<SpellTrapDestroyed>());
            Assert.Empty(events.OfType<MonsterDestroyed>());
            Assert.NotNull(e.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
        }
        Drain(e);
    }

    [Fact]
    public void DestroyedTargetStillCountsWhenRedirectedAwayFromGraveyard()
    {
        var e = Make(redirect: true); Ready(e, shield: true); Activate(e);
        var events = ToPause(e);
        Assert.Single(events.OfType<MonsterDestroyed>());
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Banished, c => c.GameCardId == Monster);
        Assert.NotNull(e.State.PendingChain!.DefinitionCardChoice);
        Assert.Equal(8123, e.State.GetPlayer(PlayerId.One).LifePoints);
        Drain(e);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATargetDestroyedByAnotherLinkOrFlippedFaceUpDoesNotUnlockDiscard(bool flip)
    {
        var e = Make(authored: true, response: flip ? new FlipTargetsFaceUpStep() : new DestroyTargetsStep());
        Ready(e, authored: true, response: true); Activate(e, authored: true);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 1)); ChooseTarget(e);
        var events = ToPause(e);
        Assert.Null(e.State.PendingChain);
        Assert.Empty(events.OfType<CardDiscarded>());
        Assert.Equal(flip ? 0 : 1, events.OfType<MonsterDestroyed>().Count());
    }

    [Fact]
    public void EffectNegationSkipsBothDestructionAndDiscard()
    {
        var e = Make(authored: true, negate: true); Ready(e, authored: true, response: true); Activate(e, authored: true);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 1));
        var events = ToPause(e);
        Assert.Null(e.State.PendingChain);
        Assert.Empty(events.OfType<MonsterDestroyed>());
        Assert.Empty(events.OfType<CardDiscarded>());
        Assert.NotNull(e.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
    }

    [Fact]
    public void AtLeastOneDestroyedTargetAllowsContinuationAcrossMultipleTargets()
    {
        var e = Make(targets: 2, replacement: true, optional: true); Ready(e, shield: true, monsters: 2); Activate(e, targets: 2);
        ToPause(e);
        var first = e.State.PendingChain!.ReplacementChoice!;
        Ok(e, new ChooseDestructionReplacement(first.Player, first.DecisionId, true));
        var second = e.State.PendingChain!.ReplacementChoice!;
        var result = Ok(e, new ChooseDestructionReplacement(second.Player, second.DecisionId, false));
        Assert.Single(result.Events.OfType<MonsterDestroyed>());
        Assert.NotNull(e.State.PendingChain!.DefinitionCardChoice);
        Assert.Equal(8123, e.State.GetPlayer(PlayerId.One).LifePoints);
        Drain(e);
    }

    [Fact]
    public void ActivationNeedsAnAdditionalHandCardEvenThoughDiscardIsNotACost()
    {
        var e = Make(authored: true, thin: true); Ready(e, authored: true);
        foreach (var card in e.State.GetPlayer(PlayerId.One).Hand.Where(c => c.GameCardId == Shield).ToArray())
        {
            Ok(e, new ActivateSpell(PlayerId.One, card.InstanceId, 0)); Drain(e);
        }
        Assert.Single(e.State.GetPlayer(PlayerId.One).Hand);
        var before = e.State;
        var result = e.Execute(new ActivateSpell(PlayerId.One, Hand(e, Lightning), 1));
        Assert.False(result.Succeeded);
        Assert.Same(before, e.State);
        Assert.Empty(result.Events);
    }
}
