using System.Text.Json;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Synthetic aura/lingering-stat interactions. No real card or native timing claims.</summary>
public sealed class ContinuousEffectTests
{
    [Fact]
    public void MultipleCopiesStackWithoutAccumulatingAcrossCommandsAndOldSnapshotsStayImmutable()
    {
        var profiles = new[] { new ContinuousEffectProfile("own-aura", A, EffectRelation.Controller, 200, 300) };
        var engine = Engine(continuous: profiles);
        profiles[0] = new("changed", A, EffectRelation.Any, 999, 999);
        EnterMain(engine); Summon(engine, A);
        var snapshot = engine.State;
        string saved = JsonSerializer.Serialize(snapshot);
        Assert.Equal(1200, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal(1300, engine.GetMonsterStatistics(PlayerId.One, 0)!.Defense);
        Assert.Null(engine.State.PendingChain);
        NextMain(engine); NextMain(engine); Summon(engine, A, 1);
        Assert.Equal(1400, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal(1600, engine.GetMonsterStatistics(PlayerId.One, 1)!.Defense);
        Assert.Equal(4, engine.State.EventEffects.AppliedModifiers.Count);
        Assert.Equal(saved, JsonSerializer.Serialize(snapshot));
        Assert.Equal(1200, Assert.Single(snapshot.EventEffects.Statistics).Attack);
        Assert.Throws<NotSupportedException>(() => ((IList<StatModifier>)snapshot.EventEffects.AppliedModifiers).Clear());
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Equal(1400, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal(4, engine.State.EventEffects.AppliedModifiers.Count);
    }

    [Theory]
    [InlineData(EffectRelation.Self, 1300, 2000)]
    [InlineData(EffectRelation.Controller, 1300, 2000)]
    [InlineData(EffectRelation.Opponent, 1000, 2300)]
    [InlineData(EffectRelation.Any, 1300, 2300)]
    public void ScopeUsesTheControllerOfTheSource(EffectRelation relation, int own, int opposing)
    {
        var engine = Engine(continuous: [new("scope", A, relation, 300, 0)]);
        EnterMain(engine); Summon(engine, A); NextMain(engine); Summon(engine, B);
        Assert.Equal(own, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal(opposing, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
    }

    [Fact]
    public void ASetSourceIsInactiveAndItsContinuousStatsStartOnFlipSummon()
    {
        var engine = Engine(continuous: [new("self", A, EffectRelation.Self, 500, 250)]);
        EnterMain(engine); Summon(engine, A, set: true);
        Assert.Equal(1000, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Empty(engine.State.EventEffects.AppliedModifiers);
        NextMain(engine); NextMain(engine);
        var flip = Ok(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack));
        Assert.Single(flip.Events.OfType<StatModifierApplied>());
        Assert.Equal(1500, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
    }

    [Fact]
    public void BattleUsesBoostedStatsAndRemovesTheDestroyedSourcesContributions()
    {
        var engine = Engine(continuous: [new("own-aura", A, EffectRelation.Controller, 500, 0)]);
        EnterMain(engine); Summon(engine, A); NextMain(engine); Summon(engine, B);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var result = Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Equal(7500, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Null(engine.GetMonsterStatistics(PlayerId.One, 0));
        Assert.Empty(engine.State.EventEffects.AppliedModifiers);
        var removed = Assert.Single(result.Events.OfType<StatModifierRemoved>());
        Assert.Equal(ModifierRemovalReason.ConditionNoLongerMet, removed.Reason);
        Assert.DoesNotContain(engine.State.EventEffects.Statistics, s => s.Monster.Player == PlayerId.One);
    }

    [Fact]
    public void RemovingOneOfTwoAuraSourcesRemovesOnlyItsContributionsToSurvivors()
    {
        var engine = Engine(continuous: [new("own", A, EffectRelation.Controller, 200, 0)]);
        EnterMain(engine); Summon(engine, A); NextMain(engine); Summon(engine, B);
        NextMain(engine); Summon(engine, A, 1); NextMain(engine);
        Assert.Equal(1400, engine.GetMonsterStatistics(PlayerId.One, 1)!.Attack);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var battle = Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Equal(3, battle.Events.OfType<StatModifierRemoved>().Count());
        Assert.Equal(1200, engine.GetMonsterStatistics(PlayerId.One, 1)!.Attack);
        Assert.Single(engine.State.EventEffects.AppliedModifiers);
        Assert.Equal(2000, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
    }

    [Fact]
    public void BanishmentRemovesAnAuraThroughTheSameLifecyclePath()
    {
        var engine = Engine(continuous: [new("own", A, EffectRelation.Controller, 200, 0)]);
        EnterMain(engine);
        Ok(engine, new ActivateSpell(PlayerId.One, engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == SummonSpell).InstanceId, 0));
        var p = engine.State.PendingDecision!;
        Ok(engine, new ChooseDecisionCard(p.Player, p.DecisionId, p.CardOptions[0].InstanceId));
        p = engine.State.PendingDecision!;
        Ok(engine, new ChooseDecisionPosition(p.Player, p.DecisionId, BattlePosition.FaceUpAttack));
        p = engine.State.PendingDecision!;
        Ok(engine, new ChooseDecisionZone(p.Player, p.DecisionId, 1));
        Assert.Single(engine.State.EventEffects.AppliedModifiers);
        var end = Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Single(end.Events.OfType<StatModifierRemoved>());
        Assert.Empty(engine.State.EventEffects.AppliedModifiers);
        Assert.Null(engine.GetMonsterStatistics(PlayerId.One, 1));
    }

    [Fact]
    public void AFaceDownDefendersAuraAppliesBeforeDamageCalculation()
    {
        var engine = Engine(continuous: [new("defense", A, EffectRelation.Self, 0, 1500)]);
        EnterMain(engine); Summon(engine, A, set: true); NextMain(engine); Summon(engine, B);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var result = Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Equal(7500, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.NotNull(engine.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Equal(BattlePosition.FaceUpDefense, engine.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Position);
        Assert.Equal(2500, engine.GetMonsterStatistics(PlayerId.One, 0)!.Defense);
        Assert.Single(result.Events.OfType<MonsterRevealed>());
        Assert.Empty(result.Events.OfType<MonsterDestroyed>());
        Assert.Single(result.Events.OfType<StatModifierApplied>());
    }

    [Fact]
    public void EndPhaseModifierStacksWithAurasAndExpiresWhileTheAuraRemains()
    {
        var trigger = new TriggerEffectProfile("temporary", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.ModifyEventMonsterUntilEndPhase, attackDelta: 400, defenseDelta: -200);
        var engine = Engine(triggers: [trigger], continuous: [new("aura", A, EffectRelation.Self, 100, 50)]);
        EnterMain(engine); Summon(engine, A); Drain(engine);
        Assert.Equal(1500, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal(850, engine.GetMonsterStatistics(PlayerId.One, 0)!.Defense);
        var old = engine.State;
        Assert.Single(old.EventEffects.TimedModifiers);
        var end = Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Empty(engine.State.EventEffects.TimedModifiers);
        Assert.Equal(1100, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal(1050, engine.GetMonsterStatistics(PlayerId.One, 0)!.Defense);
        Assert.Equal(ModifierRemovalReason.EndPhaseReached, Assert.Single(end.Events.OfType<StatModifierRemoved>()).Reason);
        Assert.Single(old.EventEffects.TimedModifiers);
        Assert.Equal(1500, Assert.Single(old.EventEffects.Statistics).Attack);
        Assert.Single(engine.State.EventEffects.AppliedModifiers);
    }

    [Fact]
    public void ResolvedModifierSurvivesItsSourceLeavingAndDoesNotAffectANewPlacement()
    {
        var trigger = new TriggerEffectProfile("opponent-weakness", A, MonsterEventKind.NormalSummoned, EffectRelation.Opponent,
            TriggerRequirement.Mandatory, TriggerOperation.ModifyEventMonsterUntilEndPhase, attackDelta: -300);
        var engine = Engine(triggers: [trigger]);
        EnterMain(engine); Summon(engine, A); NextMain(engine); Summon(engine, B); Drain(engine);
        Assert.Equal(1700, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Null(engine.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Equal(1700, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
        Assert.Single(engine.State.EventEffects.TimedModifiers);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Main2));
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.End));
        Assert.Empty(engine.State.EventEffects.TimedModifiers);
        Assert.Equal(2000, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
    }

    [Fact]
    public void ModifierIsRemovedWhenItsSubjectIsDestroyed()
    {
        var trigger = new TriggerEffectProfile("small-boost", A, MonsterEventKind.NormalSummoned, EffectRelation.Opponent,
            TriggerRequirement.Mandatory, TriggerOperation.ModifyEventMonsterUntilEndPhase, attackDelta: 100);
        var engine = Engine(triggers: [trigger], attackA: 2500);
        EnterMain(engine); Summon(engine, A); NextMain(engine); Summon(engine, B); Drain(engine);
        Assert.Equal(2100, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var result = Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Null(engine.GetMonsterStatistics(PlayerId.Two, 0));
        Assert.Empty(engine.State.EventEffects.TimedModifiers);
        Assert.Single(result.Events.OfType<StatModifierRemoved>());
    }

    [Theory]
    [InlineData(1000, -2000, 0)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    [InlineData(null, 500, null)]
    public void VariableAndExtremeStatsStayInTheManagedDomain(int? printed, int delta, int? expected)
    {
        var engine = Engine(continuous: [new("range", A, EffectRelation.Self, delta, 0)], attackA: printed);
        EnterMain(engine); Summon(engine, A);
        Assert.Equal(expected, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
    }

    [Fact]
    public void StackedDeltasAreSummedBeforeClampingAndRegistrationOrderDoesNotMatter()
    {
        var profiles = new[] { new ContinuousEffectProfile("minus", A, EffectRelation.Self, -2000, 0),
            new ContinuousEffectProfile("plus", A, EffectRelation.Self, 1500, 0) };
        var first = Engine(continuous: profiles); var second = Engine(continuous: profiles.Reverse());
        EnterMain(first); Summon(first, A); EnterMain(second); Summon(second, A);
        Assert.Equal(500, first.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal(first.GetMonsterStatistics(PlayerId.One, 0), second.GetMonsterStatistics(PlayerId.One, 0));
    }

    [Fact]
    public void InvalidContinuousProfilesAndStatQueriesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ContinuousEffectProfile("zero", A, EffectRelation.Any, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContinuousEffectProfile("invalid", A, (EffectRelation)100, 1, 0));
        Assert.Throws<ArgumentException>(() => Engine(continuous: [new("spell", Trap, EffectRelation.Any, 1, 0)]));
        var engine = Engine();
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.GetMonsterStatistics((PlayerId)100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.GetMonsterStatistics(PlayerId.One, 5));
    }
}
