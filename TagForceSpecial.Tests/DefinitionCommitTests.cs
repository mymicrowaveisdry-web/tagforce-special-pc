using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tests;

/// <summary>
/// Definition commit boundaries: discard cost and Spell/Trap target are separate decisions, checked before the source
/// moves and never refunded.
/// </summary>
public sealed class DefinitionCommitTests
{
    private static DuelEngine Engine(PlayerId actor = PlayerId.One, bool enabled = true, bool combined = false, bool onlyTraps = false)
    {
        uint[] ids = [1, 2, 4909, 5217];
        var catalog = new CardCatalog(new(new string('0', 64), "invented-activation-inputs"),
            ids.Select(id => new CardDefinition(id, $"Synthetic {id}", "", new(1000, 1000, 4),
                new(0, 0, 0, 0, 0, 0, 0), new((int)id, id, 4u << 11, 0))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(id == 1 ? CardCategory.Monster :
            id == 2 ? CardCategory.Trap : CardCategory.Spell, AllowsNormalSummon: id == 1,
            AllowsSpellTrapSet: id != 1));
        uint candidate = onlyTraps ? 2u : 1u;
        uint[] deck = [5217, candidate, candidate, 4909, 2, .. Enumerable.Repeat(candidate, 35)];
        EffectDefinition[] definitions = combined
            ? [new("synthetic-cost-and-target", 4909, EffectKind.QuickPlaySpell, [new DestroyTargetStep()],
                cost: new DiscardCost(), target: new SpellTrapOnFieldTarget())]
            : EventEffectScenario.ChainCards(5217, 4909);
        var engine = new DuelEngine(catalog, rules, deck, deck, actor, effectDefinitions: enabled ? definitions : []);
        Ok(engine, new ResolveDrawStep(actor));
        Ok(engine, new ChangePhase(actor, DuelPhase.Main1));
        return engine;
    }

    private static DuelActionResult Ok(DuelEngine engine, DuelCommand command)
    {
        var revision = engine.State.Revision;
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
        Assert.Equal(revision + 1, result.State.Revision);
        return result;
    }

    private static void Reject(DuelEngine engine, DuelCommand command, DuelError error)
    {
        var before = engine.State;
        var json = JsonSerializer.Serialize(before);
        var result = engine.Execute(command);
        Assert.Equal(error, result.Error);
        Assert.False(result.Succeeded);
        Assert.Same(before, result.State);
        Assert.Same(before, engine.State);
        Assert.Equal(json, JsonSerializer.Serialize(engine.State));
        Assert.Empty(result.Events);
    }

    private static CardInstance Hand(DuelEngine e, uint id) => e.State.GetPlayer(e.State.ActivePlayer).Hand.First(c => c.GameCardId == id);
    private static void Activate(DuelEngine e, uint id) => Ok(e, new ActivateSpell(e.State.ActivePlayer, Hand(e, id).InstanceId, 2));

    [Fact]
    public void CardsWithoutADefinitionCannotActivate()
    {
        var engine = Engine(enabled: false);
        Reject(engine, new ActivateSpell(PlayerId.One, Hand(engine, 5217).InstanceId, 2), DuelError.UnsupportedSpellEffect);
    }

    [Theory]
    [InlineData(PlayerId.One)]
    [InlineData(PlayerId.Two)]
    public void DiscardCommitsBeforeTheLinkAndIsNotRefundedByRejectedCommands(PlayerId actor)
    {
        var engine = Engine(actor);
        var initial = engine.State;
        var spell = Hand(engine, 5217);
        Activate(engine, 5217);
        var before = engine.State;
        Assert.Empty(before.GetPlayer(actor).Graveyard);
        var decision = before.PendingDecision!;
        Assert.Equal(4, decision.CardOptions.Count); // any other hand card
        Assert.DoesNotContain(spell, decision.CardOptions);
        var cost = decision.CardOptions[0];
        var other = decision.CardOptions[1];
        var result = Ok(engine, new ChooseDecisionCard(actor, decision.DecisionId, cost.InstanceId));
        Assert.Collection(result.Events, e => Assert.IsType<DecisionAnswered>(e),
            e => Assert.Equal(cost, Assert.IsType<ActivationCostPaid>(e).Card),
            e => Assert.IsType<DecisionSequenceCompleted>(e), e => Assert.IsType<ActivationCommitted>(e),
            e => Assert.IsType<ChainLinkAdded>(e), e => Assert.IsType<ChainResponseRequested>(e));
        var committed = result.State.CommittedActivation!;
        Assert.Equal(cost, committed.DiscardedCost);
        Assert.Equal(spell, committed.Spell);
        Assert.Null(committed.Target);
        Assert.Equal(cost, Assert.Single(result.State.GetPlayer(actor).Graveyard));
        Assert.Contains(other, result.State.GetPlayer(actor).Hand);
        Assert.Equal(spell, result.State.GetPlayer(actor).SpellTrapFields[2]!.Card);
        Assert.All(result.State.GetPlayer(actor).MonsterZones, m => Assert.Null(m));
        Assert.False(result.State.NormalSummonUsed);
        Assert.False(result.State.ResearchSliceComplete);
        Assert.Null(result.State.Outcome);
        Assert.Null(result.State.PendingSpellActivation);
        Assert.Null(result.State.PendingDecision);
        Assert.Equal(committed, Assert.Single(result.State.PendingChain!.Links).Activation);
        Assert.Contains(cost, before.GetPlayer(actor).Hand);
        Assert.Empty(before.GetPlayer(actor).Graveyard);
        Assert.Null(initial.CommittedActivation);
        Reject(engine, new ChooseDecisionCard(actor, decision.DecisionId, cost.InstanceId), DuelError.NoPendingDecision);
        Reject(engine, new EndTurn(actor), DuelError.ChainPending);
        Assert.Equal(cost, Assert.Single(engine.State.GetPlayer(actor).Graveyard));
    }

    [Fact]
    public void CostAnswersValidateIdentityOwnerKindAndEligibilityAtomically()
    {
        var engine = Engine();
        var spell = Hand(engine, 5217);
        Activate(engine, 5217);
        var p = engine.State.PendingDecision!;
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.DecisionPending);
        Reject(engine, new ChooseDecisionCard(PlayerId.Two, p.DecisionId, p.CardOptions[0].InstanceId), DuelError.NotActivePlayer);
        Reject(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId - 1, p.CardOptions[0].InstanceId), DuelError.StaleDecision);
        Reject(engine, new ChooseDecisionPosition(PlayerId.One, p.DecisionId, BattlePosition.FaceUpAttack), DuelError.WrongDecisionKind);
        Reject(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId, spell.InstanceId), DuelError.InvalidDecisionChoice);
        Reject(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId,
            engine.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId), DuelError.InvalidDecisionChoice);
        Assert.Empty(engine.State.GetPlayer(PlayerId.One).Graveyard);
    }

    [Fact]
    public void TargetMustExistBeforeSourceIsPlacedAndSourceCannotTargetItself()
    {
        var engine = Engine();
        Reject(engine, new ActivateSpell(PlayerId.One, Hand(engine, 4909).InstanceId, 2), DuelError.DecisionUnavailable);
        var trap = Hand(engine, 2);
        Ok(engine, new SetSpellTrap(PlayerId.One, trap.InstanceId, 1));
        var mst = Hand(engine, 4909);
        Activate(engine, 4909);
        var p = engine.State.PendingDecision!;
        Assert.Equal(DecisionKind.SpellTrapTarget, p.Kind);
        var target = Assert.Single(p.TargetOptions);
        Assert.Equal(trap, target.Card);
        Reject(engine, new ChooseDecisionTarget(PlayerId.One, p.DecisionId, PlayerId.One, 2, mst.InstanceId), DuelError.InvalidDecisionChoice);
        Reject(engine, new ChooseDecisionTarget(PlayerId.One, p.DecisionId, PlayerId.Two, 1, trap.InstanceId), DuelError.InvalidDecisionChoice);
        Reject(engine, new ChooseDecisionTarget(PlayerId.One, p.DecisionId, (PlayerId)99, 99, trap.InstanceId), DuelError.InvalidDecisionChoice);
        var result = Ok(engine, new ChooseDecisionTarget(PlayerId.One, p.DecisionId, target.Player, target.Zone, target.Card.InstanceId));
        Assert.Equal(target, result.State.CommittedActivation!.Target);
        Assert.Null(result.State.CommittedActivation.DiscardedCost);
        Assert.Equal(trap, result.State.GetPlayer(PlayerId.One).SpellTrapFields[1]!.Card);
        Assert.Empty(result.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Throws<NotSupportedException>(() => ((IList<SpellTrapTarget>)p.TargetOptions).Clear());
    }

    [Fact]
    public void TargetBindingAcceptsFaceUpChangeAndRejectsMissingReplacementAndReentry()
    {
        var card = new CardInstance(1, 2, PlayerId.Two);
        var target = new SpellTrapTarget(PlayerId.Two, 1, card, 10);
        Assert.True(target.IsStillPresent(new(card, false, 10)));
        Assert.True(target.IsStillPresent(new(card, true, 10)));
        Assert.False(target.IsStillPresent(null));
        Assert.False(target.IsStillPresent(new(new(2, 2, PlayerId.Two), false, 10)));
        Assert.False(target.IsStillPresent(new(card, false, 11)));
    }

    [Fact]
    public void CostPaymentAndTargetSelectionAreSeparateBoundaries()
    {
        var engine = Engine(combined: true);
        var trap = Hand(engine, 2);
        Ok(engine, new SetSpellTrap(PlayerId.One, trap.InstanceId, 1));
        Activate(engine, 4909);
        var p = engine.State.PendingDecision!;
        var cost = p.CardOptions[0];
        var costDecisionId = p.DecisionId;
        Ok(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId, cost.InstanceId));
        var paid = engine.State;
        Assert.Equal(cost, paid.PendingSpellActivation!.DiscardedCost);
        Assert.Null(paid.CommittedActivation);
        Assert.Null(paid.PendingChain);
        p = paid.PendingDecision!;
        Reject(engine, new ChooseDecisionCard(PlayerId.One, costDecisionId, cost.InstanceId), DuelError.StaleDecision);
        Reject(engine, new ChooseDecisionTarget(PlayerId.One, p.DecisionId, PlayerId.One, 4, trap.InstanceId), DuelError.InvalidDecisionChoice);
        Assert.Equal(cost, Assert.Single(engine.State.GetPlayer(PlayerId.One).Graveyard));
        var target = p.TargetOptions[0];
        Ok(engine, new ChooseDecisionTarget(PlayerId.One, p.DecisionId, target.Player, target.Zone, target.Card.InstanceId));
        Assert.Equal(cost, engine.State.CommittedActivation!.DiscardedCost);
        Assert.Equal(target, engine.State.CommittedActivation.Target);
        Assert.Equal(cost, Assert.Single(engine.State.GetPlayer(PlayerId.One).Graveyard));
    }

    [Fact]
    public void MissingCostOrTargetRejectsBeforeSourceCommit()
    {
        // Only the source left in hand: nothing to discard.
        var engine = Engine(onlyTraps: true);
        var others = engine.State.GetPlayer(PlayerId.One).Hand.Where(c => c.GameCardId != 5217).ToArray();
        int[] zones = [0, 1, 3, 4];
        for (int i = 0; i < others.Length; i++) Ok(engine, new SetSpellTrap(PlayerId.One, others[i].InstanceId, zones[i]));
        Assert.Equal(5217u, Assert.Single(engine.State.GetPlayer(PlayerId.One).Hand).GameCardId);
        Reject(engine, new ActivateSpell(PlayerId.One, Hand(engine, 5217).InstanceId, 2), DuelError.DecisionUnavailable);
        var combined = Engine(combined: true);
        Reject(combined, new ActivateSpell(PlayerId.One, Hand(combined, 4909).InstanceId, 2), DuelError.DecisionUnavailable);
        Assert.Empty(combined.State.GetPlayer(PlayerId.One).Graveyard);
    }

    [Fact]
    public void DefinitionsValidateTheirPartsAndCannotStartThroughGenericSequenceCommands()
    {
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.QuickPlaySpell, [new DestroyTargetStep()]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.QuickPlaySpell, [new DealDamageStep(1)],
            target: new SpellTrapOnFieldTarget()));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.CounterTrap, [new NegateActivationStep(true)]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new DealDamageStep(1)],
            conditions: [new RespondsToSpellActivationCondition()]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new DealDamageStep(1)],
            conditions: [new NoSummonThisTurnCondition(), new NoSummonThisTurnCondition()]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FreeMonsterZonesCondition(6));
        Assert.Throws<ArgumentException>(() => new SpecialSummonStep(SpecialSummonSources.Deck, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpecialSummonStep(SpecialSummonSources.Graveyard, [1]));
        var engine = Engine();
        Reject(engine, new BeginDecisionSequence(PlayerId.One, "activation:lightning-vortex-v1"), DuelError.UnsupportedDecisionSequence);
        var catalog = new CardCatalog(new(null, "x"), new uint[] { 1, 5217 }.Select(id => new CardDefinition(id, $"{id}", "", new(0, 0, 1))));
        var rules = new Dictionary<uint, DuelCardRules> { [1] = new(CardCategory.Monster), [5217] = new(CardCategory.Spell, AllowsSpellTrapSet: true) };
        uint[] deck = [.. Enumerable.Repeat(1u, 40)];
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, rules, deck, deck,
            decisionSequences: [new("activation:lightning-vortex-v1", [DecisionStep.AnyCard(DecisionCardSource.Hand)])],
            effectDefinitions: EventEffectScenario.ChainCards(5217)));
    }

    [Fact]
    public void ReplayIsDeterministic()
    {
        string Run()
        {
            var engine = Engine();
            Activate(engine, 5217);
            var p = engine.State.PendingDecision!;
            var result = Ok(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId, p.CardOptions[0].InstanceId));
            return JsonSerializer.Serialize(new { result.State, Events = result.Events.Select(e => JsonSerializer.Serialize(e, e.GetType())) });
        }
        Assert.Equal(Run(), Run());
    }
}
