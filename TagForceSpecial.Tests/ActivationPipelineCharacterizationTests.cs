using TagForceSpecial.Core;
using Xunit.Abstractions;
using static TagForceSpecial.Tests.CountQueryScenario;

namespace TagForceSpecial.Tests;

/// <summary>Activation rejection precedence, payment, decisions and committed links through public commands.</summary>
public sealed class ActivationPipelineCharacterizationTests(ITestOutputHelper output)
{
    private CountQueryScenario Scenario(EffectDefinition definition) => new(definition, output: new ActivationOutput(output));

    [Theory]
    [InlineData(false, 500, 7600)]
    [InlineData(true, 4000, 4101)]
    public void LifePointCostsArePaidExactlyOnceBeforeCommitment(bool half, int paid, int remaining)
    {
        EffectCost cost = half ? new PayHalfLifePointsCost() : new PayLifePointsCost(500);
        var s = Scenario(new("activation", Probe, EffectKind.NormalSpell, [new GainLifePointsStep(100)], cost: cost));
        s.EnterMain();
        if (half) { s.Cast(Preparation); s.Resolve(); }
        s.Events.Clear(); s.Cast();
        Assert.Equal(new[] { typeof(SpellActivated), typeof(LifePointsPaid), typeof(ActivationCommitted), typeof(ChainLinkAdded), typeof(ChainResponseRequested) },
            s.Events.Select(e => e.GetType()));
        s.Resolve();
        Assert.Equal(paid, Assert.Single(s.Events.OfType<LifePointsPaid>()).Amount);
        Assert.Single(s.Events.OfType<ActivationCommitted>());
        Assert.Equal(remaining, s.Engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void DiscardsStayPaidWhileTargetsWaitAndStaleAnswersDoNothing()
    {
        var definition = new EffectDefinition("activation", Probe, EffectKind.NormalSpell, [new DestroyTargetsStep()],
            cost: new DiscardCost(new AnyMonsterFilter(), 2),
            fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp)]);
        var s = Scenario(definition); s.Populate(); s.Cast();
        var first = s.Engine.State.PendingDecision!;
        var card = first.CardOptions[0];
        s.Execute(new ChooseDecisionCard(first.Player, first.DecisionId, card.InstanceId));
        Assert.Contains(card, s.Engine.State.GetPlayer(first.Player).Graveyard);
        Assert.Empty(s.Events.OfType<ActivationCommitted>());
        s.Execute(new ChooseDecisionCard(first.Player, first.DecisionId, card.InstanceId), DuelError.StaleDecision);
        var second = s.Engine.State.PendingDecision!;
        s.Execute(new ChooseDecisionCard(second.Player, second.DecisionId, second.CardOptions[0].InstanceId));
        var target = s.Engine.State.PendingDecision!;
        Assert.Equal(2, s.Events.OfType<ActivationCostPaid>().Count());
        Assert.Empty(s.Events.OfType<ActivationCommitted>());
        s.Execute(new FinishDecision(target.Player, target.DecisionId), DuelError.InvalidDecisionChoice);
        s.Resolve();
        var receipt = Assert.Single(s.Events.OfType<ActivationCommitted>()).Activation;
        Assert.Equal(card, receipt.DiscardedCost);
        Assert.Single(receipt.FieldTargets!);
        Assert.Single(s.Events.OfType<ChainLinkAdded>());
        Assert.Contains(card, s.Engine.State.GetPlayer(first.Player).Graveyard);
    }

    [Fact]
    public void SetTurnTimingRejectsBeforeAnUnpayableCost()
    {
        var s = Scenario(new("activation", Probe, EffectKind.QuickPlaySpell,
            [new GainLifePointsStep(100)], cost: new PayLifePointsCost(9000)));
        s.EnterMain();
        int card = s.Engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Probe).InstanceId;
        s.Execute(new SetSpellTrap(PlayerId.One, card, 0));
        s.Execute(new ActivateSetSpell(PlayerId.One, 0), DuelError.SetThisTurn);
        Assert.Empty(s.Events.OfType<LifePointsPaid>());
    }

    [Fact]
    public void UnpayableCostRejectsBeforeUnavailableTargetChoices()
    {
        var s = Scenario(new("activation", Probe, EffectKind.NormalSpell, [new DestroyTargetsStep()],
            cost: new PayLifePointsCost(9000), fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own)]));
        s.EnterMain();
        int card = s.Engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Probe).InstanceId;
        s.Execute(new ActivateSpell(PlayerId.One, card, 0), DuelError.InsufficientLifePoints);
    }

    [Fact]
    public void OptionalTargetCompletionKeepsPaidCostsAndCommitsOneLink()
    {
        var definition = new EffectDefinition("activation", Probe, EffectKind.NormalSpell, [new AddTargetsToHandStep()],
            cost: new PayLifePointsCost(500), fieldTargets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new AnyMonsterFilter(), 1, 2)]);
        var s = new CountQueryScenario(definition, preparation: [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Own)], output: new ActivationOutput(output));
        s.Populate(); s.Cast(Preparation); s.Resolve(); s.Cast();
        var required = s.Engine.State.PendingDecision!;
        var target = required.FieldTargetOptions![0];
        s.Execute(new ChooseDecisionFieldTarget(required.Player, required.DecisionId, target.Player, target.Location, target.Zone, target.Card.InstanceId));
        var pending = s.Engine.State.PendingDecision!;
        Assert.True(pending.CanFinish);
        s.Execute(new FinishDecision(pending.Player, pending.DecisionId));
        s.Resolve();
        Assert.Single(s.Events.OfType<LifePointsPaid>());
        Assert.Equal(7500, s.Engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(2, s.Events.OfType<ActivationCommitted>().Count());
        Assert.Null(s.Engine.State.PendingDecision);
        Assert.Null(s.Engine.State.PendingChain);
    }

    [Fact]
    public void AppendedResponsesPayTheirOwnCostAndResolveInReverseOrder()
    {
        var s = Scenario(new("activation", Probe, EffectKind.QuickPlaySpell,
            [new GainLifePointsStep(100)], cost: new PayLifePointsCost(500)));
        s.EnterMain();
        var first = s.Engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Probe);
        s.Execute(new SetSpellTrap(PlayerId.One, first.InstanceId, 0));
        s.Execute(new ChangePhase(PlayerId.One, DuelPhase.End));
        s.Execute(new EndTurn(PlayerId.One)); s.EnterMain();
        if (s.Engine.State.OpenWindow is { } window)
            s.Execute(new PassChainResponse(window.Responder, window.DecisionId));
        s.Cast();
        var chain = s.Engine.State.PendingChain!;
        s.Execute(new ActivateChainResponse(PlayerId.One, chain.DecisionId, 0));
        Assert.Equal(new[] { 1, 2 }, s.Engine.State.PendingChain!.Links.Select(l => l.Number));
        s.Resolve();
        Assert.Equal(new[] { 2, 1 }, s.Events.OfType<ChainLinkResolved>().Select(e => e.Number));
        Assert.Equal(2, s.Events.OfType<LifePointsPaid>().Count());
        Assert.All(s.Engine.State.Players, p => Assert.Equal(7600, p.LifePoints));
    }

    private sealed class ActivationOutput(ITestOutputHelper output) : ITestOutputHelper
    {
        public void WriteLine(string message) => output.WriteLine(message.Replace("COUNT_TRACE ", "ACTIVATION_TRACE ", StringComparison.Ordinal));
        public void WriteLine(string format, params object[] args) => WriteLine(string.Format(format, args));
    }
}
