using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

public sealed class ContinuousSweepDeclarationTests
{
    private const uint Dispersion = 6216, Purification = 6225, ContinuousSpell = 960,
        ContinuousTrap = 961, Response = 962, FillerCard = 4469;

    private static DuelEngine Make(uint source, bool protection = false, bool replacement = false, bool returnResponse = false)
    {
        var family = ShortTextCardFamily.Family;
        uint[] ids = [Dispersion, Purification, ContinuousSpell, ContinuousTrap, Response, FillerCard];
        var catalog = new CardCatalog(new(null, "synthetic-continuous-sweep-scenarios"), ids.Select(id =>
            new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, 4),
                new(0, 0, 0, 0, id is ContinuousSpell or ContinuousTrap ? 4u : 0u, 0, 0))));
        var rules = family.Rules.Where(p => p.Key is Dispersion or Purification or FillerCard).ToDictionary();
        rules[ContinuousSpell] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[ContinuousTrap] = rules[Response] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        uint affected = source == Dispersion ? ContinuousTrap : ContinuousSpell;
        return new(catalog, rules,
            [source, ContinuousSpell, ContinuousTrap, FillerCard, FillerCard, .. Enumerable.Repeat(FillerCard, 35)],
            [ContinuousSpell, ContinuousTrap, Response, FillerCard, FillerCard, .. Enumerable.Repeat(FillerCard, 35)],
            effectDefinitions: [.. family.Definitions.Where(d => d.CardId is Dispersion or Purification),
                new("continuous-spell", ContinuousSpell, EffectKind.ContinuousSpell, [], isMarker: true),
                new("continuous-trap", ContinuousTrap, EffectKind.ContinuousTrap, [], isMarker: true),
                returnResponse ? new("return", Response, EffectKind.NormalTrap,
                    [new ReturnAllToHandStep(FieldCardKind.SpellTrap)])
                    : ResponseProbe.Negate("negate", Response, ResponseNegation.Effect)],
            experimentalTriggerEffects: family.Triggers.Where(t => t.CardId == FillerCard),
            ruleLocks: protection ? [new("protection", affected,
                new RuleLock(LockedAction.BeDestroyedByEffect, TargetSide.Own, new CardIdFilter([affected])))] : [],
            destructionReplacements: replacement ? [new("replacement", affected, ReplacementSubject.Self,
                new ReplacementCost(ReplacementAction.SendFromDeckToGraveyard))] : [], shuffleSeed: 6);
    }

    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(e.State.ActivePlayer).Hand.First(c => c.GameCardId == id).InstanceId;

    private static void Ready(DuelEngine e)
    {
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, ContinuousSpell), 0)); Drain(e); Set(e, ContinuousTrap, 1);
        NextMain(e);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, ContinuousSpell), 0)); Drain(e); Set(e, ContinuousTrap, 1); Set(e, Response, 2);
        NextMain(e); Ok(e, new ActivateSetSpell(PlayerId.One, 1)); Drain(e);
        NextMain(e); Ok(e, new ActivateSetSpell(PlayerId.Two, 1)); Drain(e); NextMain(e);
    }

    private static IReadOnlyList<DuelEvent> ActivateAndPay(DuelEngine e, uint source)
    {
        if (source == Purification)
        {
            Set(e, source, 2); NextMain(e); NextMain(e);
            Ok(e, new ActivateSetSpell(PlayerId.One, 2));
        }
        else Ok(e, new ActivateSpell(PlayerId.One, Hand(e, source), 2));
        var pending = e.State.PendingDecision!;
        Assert.Equal(DecisionKind.Card, pending.Kind);
        Assert.Null(pending.FieldTargetOptions);
        var cost = pending.CardOptions.First(c => c.GameCardId == FillerCard);
        return Ok(e, new ChooseDecisionCard(pending.Player, pending.DecisionId, cost.InstanceId)).Events;
    }

    [Theory]
    [InlineData(Dispersion)]
    [InlineData(Purification)]
    public void ActualDeclarationsPayCostAndSweepBothSidesWithoutTargeting(uint source)
    {
        var e = Make(source); Ready(e);
        var paid = ActivateAndPay(e, source);
        Assert.Single(paid.OfType<ActivationCostPaid>());
        Assert.Empty(paid.OfType<CardDiscarded>());
        Assert.Null(e.State.PendingDecision);
        Assert.All(e.State.PendingChain!.Links, link => Assert.Empty(link.Activation.FieldTargets ?? []));
        var events = Drain(e);
        Assert.Equal(2, events.OfType<SpellTrapDestroyed>().Count());
        foreach (var player in e.State.Players)
        {
            Assert.Contains(player.Graveyard, c => c.GameCardId == (source == Dispersion ? ContinuousTrap : ContinuousSpell));
            Assert.Contains(player.SpellTrapZones, c => c?.GameCardId == (source == Dispersion ? ContinuousSpell : ContinuousTrap));
        }
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Empty(events.OfType<CardDiscarded>());
    }

    [Theory]
    [InlineData(Dispersion)]
    [InlineData(Purification)]
    public void ProtectedEligibleCardsSurviveWithoutRefundingCost(uint source)
    {
        var e = Make(source, protection: true); Ready(e); ActivateAndPay(e, source);
        var events = Drain(e);
        Assert.Empty(events.OfType<SpellTrapDestroyed>());
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == FillerCard);
    }

    [Theory]
    [InlineData(Dispersion)]
    [InlineData(Purification)]
    public void OptionalReplacementCanSaveOneSideWhileOtherSideIsDestroyed(uint source)
    {
        var e = Make(source, replacement: true); Ready(e); ActivateAndPay(e, source);
        while (e.State.PendingChain is { ReplacementChoice: null } chain)
            Ok(e, chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(PlayerId.One, chain.DecisionId));
        var first = e.State.PendingChain!.ReplacementChoice!;
        Ok(e, new ChooseDestructionReplacement(first.Player, first.DecisionId, true, first.Replacement.Current.CardOptions[0].InstanceId));
        var second = e.State.PendingChain!.ReplacementChoice!;
        var result = Ok(e, new ChooseDestructionReplacement(second.Player, second.DecisionId, false));
        Assert.Single(result.Events.OfType<SpellTrapDestroyed>());
        Assert.Single(result.Events.OfType<SpellTrapDestructionReplaced>());
        Drain(e);
    }

    [Theory]
    [InlineData(Dispersion, false)]
    [InlineData(Purification, false)]
    [InlineData(Dispersion, true)]
    [InlineData(Purification, true)]
    public void NegationOrRemovingEligibleCardsInResponseDoesNotRefundCost(uint source, bool remove)
    {
        var e = Make(source, returnResponse: remove); Ready(e); ActivateAndPay(e, source);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 2));
        var events = Drain(e);
        Assert.Empty(events.OfType<SpellTrapDestroyed>());
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == FillerCard);
        Assert.Empty(events.OfType<CardDiscarded>());
    }

    [Fact]
    public void ZeroEligibleCardsAndIncorrectActivationCategoryRejectWithoutMutation()
    {
        var e = Make(Dispersion); EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Dispersion), 0), DuelError.DecisionUnavailable);
        var trap = Make(Purification); Ready(trap);
        var before = trap.State;
        Assert.False(trap.Execute(new ActivateSpell(PlayerId.One, Hand(trap, Purification), 2)).Succeeded);
        Assert.Same(before, trap.State);
        Set(trap, Purification, 2); before = trap.State;
        Assert.False(trap.Execute(new ActivateSetSpell(PlayerId.One, 2)).Succeeded);
        Assert.Same(before, trap.State);
    }
}
