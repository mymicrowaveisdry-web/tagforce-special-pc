using System.Text.Json;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;
using static TagForceSpecial.Tests.TriggerCommitmentTests;

namespace TagForceSpecial.Tests;

/// <summary>Generic synthetic field/lingering negation; no complete real-card support claims.</summary>
public sealed class ContinuousNegationTests
{
    [Fact]
    public void AFieldNegatorRemovesAuraContributionsAndItsDestructionRestoresThem()
    {
        var engine = Engine(attackA: 2500, continuous: [new("aura", A, EffectRelation.Self, 500, 0)],
            negations: [new("field-negation", B, EffectRelation.Opponent, [A])]);
        EnterMain(engine); Summon(engine, A);
        Assert.Equal(3000, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        var saved = engine.State; string before = JsonSerializer.Serialize(saved);
        NextMain(engine); var summon = Summon(engine, B);
        Assert.Equal(2500, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Single(summon.Events.OfType<StatModifierRemoved>());
        Assert.Equal(A, Assert.Single(engine.State.EventEffects.NegatedMonsters).Card.GameCardId);
        Assert.Equal(before, JsonSerializer.Serialize(saved));
        NextMain(engine); Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        var battle = Ok(engine, new DeclareAttack(PlayerId.One, 0, 0));
        Assert.Null(engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
        Assert.Equal(3000, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Empty(engine.State.EventEffects.NegatedMonsters);
        Assert.Single(battle.Events.OfType<StatModifierApplied>());
    }

    [Fact]
    public void NegatingANegationSourceRestoresItsTargetsWithoutAnIterationShortcut()
    {
        var engine = Engine(continuous: [new("filler-aura", Filler, EffectRelation.Self, 300, 0)],
            negations: [new("a-negates-b", A, EffectRelation.Opponent, [B]), new("b-negates-filler", B, EffectRelation.Controller, [Filler])]);
        EnterMain(engine); Summon(engine, A); NextMain(engine); Summon(engine, B); NextMain(engine); NextMain(engine);
        Summon(engine, Filler, 1);
        Assert.Equal(1300, engine.GetMonsterStatistics(PlayerId.Two, 1)!.Attack);
        Assert.Equal(B, Assert.Single(engine.State.EventEffects.NegatedMonsters).Card.GameCardId);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Equal(1000, engine.GetMonsterStatistics(PlayerId.Two, 1)!.Attack);
        Assert.Equal(Filler, Assert.Single(engine.State.EventEffects.NegatedMonsters).Card.GameCardId);
    }

    [Fact]
    public void ASetNegatorIsInactiveAndStartsOnFlipSummon()
    {
        var engine = Engine(continuous: [new("aura", B, EffectRelation.Self, 300, 0)],
            negations: [new("negator", A, EffectRelation.Opponent, [B])]);
        EnterMain(engine); Summon(engine, A, set: true); NextMain(engine); Summon(engine, B);
        Assert.Equal(2300, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
        NextMain(engine); Ok(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack));
        Assert.Equal(2000, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
        Assert.Single(engine.State.EventEffects.NegatedMonsters);
    }

    [Fact]
    public void ATargetedNegationExpiresAndRestoresAurasAtEndPhase()
    {
        var profile = new TriggerEffectProfile("target-negation", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.NegateTargetMonsterUntilEndPhase, target: new(EffectRelation.Opponent));
        var engine = Engine(triggers: [profile], continuous: [new("b-aura", B, EffectRelation.Self, 500, 0)]);
        EnterMain(engine); NextMain(engine); Summon(engine, B); NextMain(engine); Summon(engine, A);
        Assert.Equal(2500, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
        Drain(engine);
        Assert.Equal(2000, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
        Assert.Single(engine.State.EventEffects.TimedNegations);
        var old = engine.State;
        var end = Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Equal(2500, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
        Assert.Empty(engine.State.EventEffects.TimedNegations);
        Assert.Single(end.Events.OfType<MonsterEffectNegationEnded>());
        Assert.Single(old.EventEffects.TimedNegations);
    }

    [Fact]
    public void AContinuouslyNegatedTriggerStillPaysCostsAndConsumesUseBeforeItsEffectIsSkipped()
    {
        var source = new TriggerEffectProfile("costly", B, MonsterEventKind.NormalSummoned, EffectRelation.Opponent,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 500,
            lifePointCost: 200, turnLimit: TriggerTurnLimit.SharedPerPlayer);
        var engine = Engine(triggers: [source], negations: [new("negation", A, EffectRelation.Opponent, [B])]);
        EnterMain(engine); NextMain(engine); Summon(engine, B); NextMain(engine); Summon(engine, A);
        var activated = Order(engine);
        Assert.Single(activated.Events.OfType<TriggerCostPaid>());
        Assert.Equal(7800, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Single(engine.State.EventEffects.UsedTriggers);
        var events = Drain(engine);
        Assert.Equal(ChainSkipReason.SourceEffectsNegated, Assert.Single(events.OfType<ChainLinkSkipped>()).Reason);
        Assert.Equal(7800, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Single(engine.State.EventEffects.UsedTriggers);
    }

    [Fact]
    public void TargetedNegationDoesNotEraseAStatModifierThatAlreadyResolved()
    {
        var profile = new TriggerEffectProfile("boost-then-negate", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, [new TriggerEffectStep([new(TriggerOperation.ModifyEventMonsterUntilEndPhase, attackDelta: 400)]),
                new TriggerEffectStep([new(TriggerOperation.NegateTargetMonsterUntilEndPhase)])], target: new(EffectRelation.Self));
        var engine = Engine(triggers: [profile], continuous: [new("aura", A, EffectRelation.Self, 200, 0)]);
        EnterMain(engine); Summon(engine, A); Drain(engine);
        Assert.Equal(1400, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Single(engine.State.EventEffects.TimedModifiers);
        Assert.Single(engine.State.EventEffects.TimedNegations);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Equal(1200, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
    }

    [Fact]
    public void NegationEndsWhenTheTargetLeavesAndDoesNotAffectItsGraveyardTrigger()
    {
        var grave = new TriggerEffectProfile("grave", B, MonsterEventKind.Destroyed, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100, sourceLocation: TriggerSourceLocation.DestroyedSelf);
        var engine = Engine(attackA: 2500, triggers: [grave], negations: [new("negation", A, EffectRelation.Opponent, [B])]);
        EnterMain(engine); Summon(engine, A); NextMain(engine); Summon(engine, B);
        Assert.Single(engine.State.EventEffects.NegatedMonsters);
        NextMain(engine); Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Ok(engine, new DeclareAttack(PlayerId.One, 0, 0));
        Assert.Empty(engine.State.EventEffects.NegatedMonsters);
        var events = Drain(engine);
        Assert.Single(events.OfType<LifePointsGained>());
        Assert.Equal(7600, engine.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void AResolvedNegationSurvivesItsSourceUntilEndPhase()
    {
        var profile = new TriggerEffectProfile("negate", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.NegateTargetMonsterUntilEndPhase, target: new(EffectRelation.Opponent));
        var engine = Engine(triggers: [profile], continuous: [new("aura", B, EffectRelation.Self, 500, 0)]);
        EnterMain(engine); NextMain(engine); Summon(engine, B); NextMain(engine); Summon(engine, A); Drain(engine);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Ok(engine, new DeclareAttack(PlayerId.One, 0, 0));
        Assert.Null(engine.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Single(engine.State.EventEffects.TimedNegations);
        Assert.Equal(2000, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2));
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Equal(2500, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
    }

    [Fact]
    public void NegatingANegationTriggerPreventsItsPersistentSuppressionFromApplying()
    {
        var profile = new TriggerEffectProfile("negate", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.NegateTargetMonsterUntilEndPhase, target: new(EffectRelation.Opponent));
        var engine = Engine(triggers: [profile], continuous: [new("aura", B, EffectRelation.Self, 500, 0)],
            responses: [new("response", Trap, ChainOperation.NegateEffect, ChainSpellSpeed.Two)]);
        EnterMain(engine); NextMain(engine); Set(engine, Trap); Summon(engine, B); NextMain(engine); Summon(engine, A); Order(engine);
        var p = engine.State.EventEffects.PendingCommitment!; var target = p.TargetOptions[0];
        Ok(engine, new ChooseTriggerMonsterTarget(p.Player, p.DecisionId, target.Player, target.Zone, target.Card.InstanceId));
        var c = engine.State.PendingChain!;
        Ok(engine, new ActivateChainResponse(PlayerId.Two, c.DecisionId, 0));
        Drain(engine);
        Assert.Empty(engine.State.EventEffects.TimedNegations);
        Assert.Empty(engine.State.EventEffects.NegatedMonsters);
        Assert.Equal(2500, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
    }

    [Fact]
    public void RepeatedIdenticalActionsRetainSeparateModifierAndRemovalIdentities()
    {
        var actions = new[] { new TriggerEffectAction(TriggerOperation.ModifyEventMonsterUntilEndPhase, attackDelta: 100),
            new TriggerEffectAction(TriggerOperation.ModifyEventMonsterUntilEndPhase, attackDelta: 100) };
        var profile = new TriggerEffectProfile("double", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, [new TriggerEffectStep(actions)]);
        var engine = Engine(triggers: [profile]);
        EnterMain(engine); Summon(engine, A);
        var events = Drain(engine);
        Assert.Equal(1200, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal(2, events.OfType<StatModifierApplied>().Count());
        Assert.Equal(2, engine.State.EventEffects.TimedModifiers.Distinct().Count());
        var end = Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Equal(2, end.Events.OfType<StatModifierRemoved>().Count());
        Assert.Equal(1000, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
    }

    [Fact]
    public void CyclicOrUnknownNegationDependenciesFailClosedAtRegistration()
    {
        Assert.Throws<ArgumentException>(() => Engine(negations: [new("self-cycle", A, EffectRelation.Controller, [A])]));
        Assert.Throws<ArgumentException>(() => Engine(negations: [new("a", A, EffectRelation.Any, [B]), new("b", B, EffectRelation.Any, [A])]));
        Assert.Throws<ArgumentException>(() => Engine(negations: [new("unknown", A, EffectRelation.Any, [17])]));
        Assert.Throws<ArgumentException>(() => Engine(negations: [new("spell", Trap, EffectRelation.Any, [A])]));
        Assert.Throws<ArgumentException>(() => new ContinuousNegationProfile("empty", A, EffectRelation.Any, []));
    }

    [Fact]
    public void NegationDependencyResultsAreIndependentOfRegistrationOrder()
    {
        var profiles = new[] { new ContinuousNegationProfile("a", A, EffectRelation.Any, [B]),
            new ContinuousNegationProfile("b", B, EffectRelation.Any, [Filler]) };
        var first = Engine(negations: profiles); var second = Engine(negations: profiles.Reverse());
        foreach (var engine in new[] { first, second })
        {
            EnterMain(engine); Summon(engine, A); NextMain(engine); Summon(engine, B);
        }
        Assert.Equal(JsonSerializer.Serialize(first.State), JsonSerializer.Serialize(second.State));
    }
}
