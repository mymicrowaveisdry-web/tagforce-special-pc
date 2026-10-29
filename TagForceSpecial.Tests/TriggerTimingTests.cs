using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Rules-informed action grouping. These are not PSP timing observations.</summary>
public sealed class TriggerTimingTests
{
    private static TriggerEffectProfile Grave(string id, TriggerTiming timing, TriggerRequirement requirement = TriggerRequirement.Optional,
        MonsterEventKind kind = MonsterEventKind.Destroyed) =>
        new(id, B, kind, EffectRelation.Self, requirement, TriggerOperation.GainLifePoints, lifePointGain: 100,
            sourceLocation: kind == MonsterEventKind.Destroyed ? TriggerSourceLocation.DestroyedSelf : TriggerSourceLocation.GraveyardSelf,
            timing: timing, turnLimit: TriggerTurnLimit.SharedPerPlayer);

    private static TriggerEffectProfile Destroy(bool sequential)
    {
        var destroy = new TriggerEffectAction(TriggerOperation.DestroyTargetMonster);
        var gain = new TriggerEffectAction(TriggerOperation.GainLifePoints, lifePointGain: 50);
        TriggerEffectStep[] steps = sequential ? [new([destroy]), new([gain])] : [new([destroy, gain])];
        return new("destroy-and-gain", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, steps, target: new(EffectRelation.Opponent));
    }

    [Theory]
    [InlineData(false, 3, 0)]
    [InlineData(true, 2, 1)]
    public void SimultaneousActionsPreserveOptionalWhenButSequentialFollowUpsMissIt(bool sequential, int remaining, int missed)
    {
        var engine = Engine(triggers: [Destroy(sequential), Grave("optional-when", TriggerTiming.When),
            Grave("optional-if", TriggerTiming.If), Grave("mandatory-when", TriggerTiming.When, TriggerRequirement.Mandatory)]);
        EnterMain(engine); NextMain(engine); Summon(engine, B); NextMain(engine); Summon(engine, A);
        Order(engine);
        var p = engine.State.EventEffects.PendingCommitment!; var target = p.TargetOptions[0];
        Ok(engine, new ChooseTriggerMonsterTarget(p.Player, p.DecisionId, target.Player, target.Zone, target.Card.InstanceId));
        Pass(engine); Pass(engine);
        var result = Resolve(engine);
        Assert.Equal(missed, result.Events.OfType<TriggerMissedTiming>().Count());
        Assert.Equal(remaining, engine.State.EventEffects.PendingTriggers!.Remaining.Count);
        Assert.Contains(engine.State.EventEffects.PendingTriggers.Remaining, t => t.Profile.EffectId == "mandatory-when");
        Assert.DoesNotContain(engine.State.EventEffects.UsedTriggers, u => u.Key == "optional-when");
        Drain(engine);
        Assert.Equal(8000 + 100 * remaining, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Equal(8050, engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void OptionalWhenUsesTheNextWindowAndIgnoresAuraAndChainBookkeeping()
    {
        var profile = new TriggerEffectProfile("when-summoned", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Optional, TriggerOperation.GainLifePoints, lifePointGain: 100, timing: TriggerTiming.When);
        var engine = Engine(triggers: [profile], continuous: [new("aura", A, EffectRelation.Self, 100, 0)]);
        EnterMain(engine); var summon = Summon(engine, A);
        Assert.Single(summon.Events.OfType<StatModifierApplied>());
        Assert.Single(engine.State.EventEffects.PendingTriggers!.Options);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.TriggerOrderPending);
        Drain(engine);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void EventsInAHigherLinkWaitAndOptionalWhenMissesAfterALowerLinkResolves()
    {
        var lower = Gain("a-lower", A, gain: 50);
        var higher = new TriggerEffectProfile("b-higher", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.DestroyTargetMonster, target: new(EffectRelation.Opponent));
        var engine = Engine(triggers: [lower, higher, Grave("when", TriggerTiming.When), Grave("if", TriggerTiming.If),
            Grave("mandatory", TriggerTiming.When, TriggerRequirement.Mandatory)]);
        EnterMain(engine); NextMain(engine); Summon(engine, B); NextMain(engine); Summon(engine, A); Order(engine);
        var p = engine.State.EventEffects.PendingCommitment!; var target = p.TargetOptions[0];
        Ok(engine, new ChooseTriggerMonsterTarget(p.Player, p.DecisionId, target.Player, target.Zone, target.Card.InstanceId));
        Pass(engine); Pass(engine);
        Resolve(engine);
        Assert.Equal(3, engine.State.EventEffects.DeferredTriggers.Count);
        Assert.Null(engine.State.EventEffects.PendingTriggers);
        var last = Resolve(engine);
        Assert.Single(last.Events.OfType<TriggerMissedTiming>());
        Assert.Equal(2, engine.State.EventEffects.PendingTriggers!.Remaining.Count);
        Drain(engine);
        Assert.Equal(8200, engine.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void ATriggerCostCannotOpenAnOptionalWhenWindowButIfAndMandatoryWhenAreRetained()
    {
        var parent = new TriggerEffectProfile("parent", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100, discardCardIds: [B]);
        uint[] deck = [A, B, Trap, SummonSpell, Filler, .. Enumerable.Repeat(A, 35)];
        var engine = Engine(playerOneDeck: deck, triggers: [parent,
            Grave("cost-when", TriggerTiming.When, kind: MonsterEventKind.SentToGraveyard),
            Grave("cost-if", TriggerTiming.If, kind: MonsterEventKind.SentToGraveyard),
            Grave("cost-mandatory", TriggerTiming.When, TriggerRequirement.Mandatory, MonsterEventKind.SentToGraveyard)]);
        EnterMain(engine); Summon(engine, A); Order(engine);
        var p = engine.State.EventEffects.PendingCommitment!;
        var cost = p.CardOptions[0];
        Ok(engine, new ChooseTriggerCostCard(p.Player, p.DecisionId, cost.InstanceId));
        Assert.Equal(3, engine.State.EventEffects.DeferredTriggers.Count);
        Assert.Null(engine.State.EventEffects.PendingTriggers);
        Assert.Single(engine.State.PendingChain!.Links);
        Pass(engine); Pass(engine);
        var result = Resolve(engine);
        Assert.Single(result.Events.OfType<TriggerMissedTiming>());
        Assert.Equal(2, engine.State.EventEffects.PendingTriggers!.Remaining.Count);
        Drain(engine);
        Assert.Equal(8300, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Contains(cost, engine.State.GetPlayer(PlayerId.One).Graveyard);
    }

    [Fact]
    public void ExistingSpellCostEventsFeedTheSameGraveyardTimingCollector()
    {
        TriggerEffectProfile Probe(string id, TriggerTiming timing) => new(id, A, MonsterEventKind.SentToGraveyard,
            EffectRelation.Self, TriggerRequirement.Optional, TriggerOperation.GainLifePoints, lifePointGain: 100,
            sourceLocation: TriggerSourceLocation.GraveyardSelf, timing: timing);
        var engine = Engine(triggers: [Probe("when", TriggerTiming.When), Probe("if", TriggerTiming.If)]);
        EnterMain(engine);
        var player = engine.State.GetPlayer(PlayerId.One);
        Ok(engine, new ActivateSpell(PlayerId.One, player.Hand.First(c => c.GameCardId == 5217).InstanceId, 0));
        var p = engine.State.PendingDecision!;
        Ok(engine, new ChooseDecisionCard(p.Player, p.DecisionId, p.CardOptions.First(c => c.GameCardId == A).InstanceId));
        Assert.Equal(2, engine.State.EventEffects.DeferredTriggers.Count);
        Pass(engine); Pass(engine);
        var result = Resolve(engine);
        Assert.Single(result.Events.OfType<TriggerMissedTiming>());
        Assert.Equal("if", Assert.Single(engine.State.EventEffects.PendingTriggers!.Options).Profile.EffectId);
        Drain(engine);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.True(engine.State.ResearchSliceComplete);
    }

    [Fact]
    public void BothMonstersInSimultaneousBattleDestructionRetainOptionalWhenTiming()
    {
        TriggerEffectProfile Profile(string id, uint card) => new(id, card, MonsterEventKind.Destroyed, EffectRelation.Self,
            TriggerRequirement.Optional, TriggerOperation.GainLifePoints, lifePointGain: 100,
            sourceLocation: TriggerSourceLocation.DestroyedSelf, timing: TriggerTiming.When);
        var engine = Engine(attackB: 1000, triggers: [Profile("a", A), Profile("b", B)]);
        EnterMain(engine); Summon(engine, A); NextMain(engine); Summon(engine, B);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var result = Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Empty(result.Events.OfType<TriggerMissedTiming>());
        Assert.Equal(2, engine.State.EventEffects.PendingTriggers!.Remaining.Count);
        Drain(engine);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void ResolutionStepsCopyCallerCollectionsAndRejectInvalidPayloads()
    {
        var actions = new[] { new TriggerEffectAction(TriggerOperation.GainLifePoints, lifePointGain: 100) };
        var steps = new[] { new TriggerEffectStep(actions) };
        var profile = new TriggerEffectProfile("copied", A, MonsterEventKind.NormalSummoned, EffectRelation.Self, TriggerRequirement.Mandatory, steps);
        actions[0] = new(TriggerOperation.GainLifePoints, lifePointGain: 999);
        steps[0] = new([actions[0]]);
        Assert.Equal(100, profile.Steps[0].Actions[0].LifePointGain);
        Assert.Throws<ArgumentException>(() => new TriggerEffectStep([]));
        Assert.Throws<ArgumentException>(() => new TriggerEffectAction(TriggerOperation.DestroyTargetMonster, lifePointGain: 1));
        Assert.Throws<ArgumentException>(() => new TriggerEffectProfile("targetless", A, MonsterEventKind.NormalSummoned,
            EffectRelation.Self, TriggerRequirement.Optional, TriggerOperation.DestroyTargetMonster));
    }
}
