using System.Text.Json;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>Synthetic staged battle contracts. Native declaration/response/replay timing is not verified.</summary>
public sealed class BattleFlowTests
{
    [Fact]
    public void ImageHierarchyIsExplicitAndDamageRemainsDeferredUntilCalculation()
    {
        var engine = Engine();
        Assert.Equal(BattleStep.Start, engine.State.Battle!.Step);
        Assert.Null(engine.State.Battle.DamageStep);
        Ready(engine); var declared = Declare(engine);
        Assert.Single(declared.Events.OfType<AttackDeclared>());
        Assert.Empty(declared.Events.OfType<BattleDamageDealt>());
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        ToDamage(engine, DamageStep.Calculation);
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.NotNull(engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
        Close(engine); var damage = Advance(engine);
        Assert.Single(damage.Events.OfType<BattleDamageDealt>());
        Assert.Equal(7400, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        var destroyed = engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]!;
        Assert.True(destroyed.BattleDestroyed);
        Assert.Empty(engine.State.GetPlayer(PlayerId.Two).Graveyard);
        Assert.Equal(DamageStep.AfterCalculation, engine.State.Battle!.DamageStep);
        Close(engine); var end = Advance(engine);
        Assert.Single(end.Events.OfType<MonsterDestroyed>());
        Assert.Null(engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
        Assert.Equal(B, Assert.Single(engine.State.GetPlayer(PlayerId.Two).Graveyard).GameCardId);
        Assert.Equal(DamageStep.End, engine.State.Battle!.DamageStep);
        Close(engine); Advance(engine);
        Assert.Equal(BattleStep.Battle, engine.State.Battle!.Step);
        Assert.Null(engine.State.Battle.Attack);
        Close(engine); Advance(engine);
        Assert.Equal(BattleStep.End, engine.State.Battle!.Step);
        Close(engine); Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2));
        Assert.Equal(DuelPhase.Main2, engine.State.Phase); Assert.Null(engine.State.Battle);
    }

    [Fact]
    public void WindowCommandsValidateActorRevisionAndStepWithoutMutation()
    {
        var engine = Engine(); var w = engine.State.Battle!.Window;
        Reject(engine, new DeclareAttack(PlayerId.One, 0, 0), DuelError.BattleWindowPending);
        Reject(engine, new PassBattleResponse(PlayerId.Two, w.DecisionId), DuelError.NotBattleResponder);
        Reject(engine, new PassBattleResponse(PlayerId.One, w.DecisionId - 1), DuelError.StaleBattleDecision);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2), DuelError.BattleWindowPending);
        Ok(engine, new PassBattleResponse(PlayerId.One, w.DecisionId));
        Reject(engine, new PassBattleResponse(PlayerId.Two, w.DecisionId), DuelError.StaleBattleDecision);
        Close(engine); w = engine.State.Battle.Window;
        Reject(engine, new PassBattleResponse(w.Responder, w.DecisionId), DuelError.BattleWindowClosed);
        Reject(engine, new AdvanceBattle(PlayerId.Two, w.DecisionId), DuelError.NotActivePlayer);
        Reject(engine, new DeclareAttack(PlayerId.One, 0, 0), DuelError.WrongBattleStep);
        Advance(engine); Close(engine); Declare(engine);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2), DuelError.BattleWindowPending);
        Close(engine); Reject(engine, new DeclareAttack(PlayerId.One, 0, 0), DuelError.WrongBattleStep);
    }

    [Fact]
    public void FaceDownRevealIsBeforeCalculationAndFlipTriggersWaitUntilAfterwards()
    {
        var flip = new TriggerEffectProfile("flip", B, MonsterEventKind.FlippedFaceUp, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100);
        var engine = Engine(faceDown: true, triggers: [flip]); Ready(engine); Declare(engine);
        ToDamage(engine, DamageStep.Start);
        Assert.Equal(BattlePosition.FaceDownDefense, engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]!.Position);
        Close(engine); var reveal = Advance(engine);
        Assert.Single(reveal.Events.OfType<MonsterRevealed>());
        Assert.Equal(BattlePosition.FaceUpDefense, engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]!.Position);
        Assert.Null(engine.State.EventEffects.PendingTriggers);
        ToDamage(engine, DamageStep.AfterCalculation);
        Assert.NotNull(engine.State.EventEffects.PendingTriggers);
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        DrainEffects(engine);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        FinishAttack(engine);
    }

    [Fact]
    public void AFlipTriggerOfABattleDestroyedMonsterResolvesBeforeItEntersTheGraveyard()
    {
        var flip = new TriggerEffectProfile("flip", B, MonsterEventKind.FlippedFaceUp, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100);
        var engine = Engine(attack: 2500, faceDown: true, triggers: [flip]); Ready(engine); Declare(engine);
        ToDamage(engine, DamageStep.AfterCalculation);
        Assert.True(engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]!.BattleDestroyed);
        Assert.NotNull(engine.State.EventEffects.PendingTriggers);
        Assert.Empty(engine.State.GetPlayer(PlayerId.Two).Graveyard);
        DrainEffects(engine);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        FinishAttack(engine);
        Assert.Equal(B, Assert.Single(engine.State.GetPlayer(PlayerId.Two).Graveyard).GameCardId);
    }

    [Fact]
    public void BattleDestroyedSourcesStopTheirAurasBeforeGraveyardCleanup()
    {
        var engine = Engine(attack: 2500, continuous: [new("self-aura", B, EffectRelation.Self, 100, 0)]);
        Ready(engine); Declare(engine);
        Assert.Equal(1300, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
        ToDamage(engine, DamageStep.AfterCalculation);
        Assert.True(engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]!.BattleDestroyed);
        Assert.Equal(1200, engine.GetMonsterStatistics(PlayerId.Two, 0)!.Attack);
        Assert.Equal(6800, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Empty(engine.State.GetPlayer(PlayerId.Two).Graveyard);
        FinishAttack(engine);
    }

    [Fact]
    public void ADestroyedFlipSourceCanResolveButCannotSelectBattleDestroyedTargets()
    {
        var flip = new TriggerEffectProfile("flip-target", B, MonsterEventKind.FlippedFaceUp, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.DestroyTargetMonster, target: new(EffectRelation.Any));
        var engine = Engine(attack: 2500, faceDown: true, triggers: [flip]); Ready(engine); Declare(engine);
        ToDamage(engine, DamageStep.AfterCalculation);
        var batch = engine.State.EventEffects.PendingTriggers!;
        Ok(engine, new ChooseTriggerOrder(batch.Player, batch.DecisionId, batch.Options.Select(t => t.Id).ToArray()));
        var commitment = engine.State.EventEffects.PendingCommitment!;
        Assert.Equal(A, Assert.Single(commitment.TargetOptions).Card.GameCardId);
        DrainEffects(engine);
        Assert.Null(engine.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        FinishAttack(engine);
        Assert.Equal(B, Assert.Single(engine.State.GetPlayer(PlayerId.Two).Graveyard).GameCardId);
    }

    [Fact]
    public void AFieldNegatorStillSuppressesTheFlipEffectOfABattleDestroyedTarget()
    {
        var flip = new TriggerEffectProfile("flip", B, MonsterEventKind.FlippedFaceUp, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 500, lifePointCost: 100);
        var engine = Engine(attack: 2500, faceDown: true, triggers: [flip],
            negations: [new("field-negator", A, EffectRelation.Opponent, [B])]);
        Ready(engine); Declare(engine); ToDamage(engine, DamageStep.AfterCalculation);
        var events = DrainEffects(engine);
        Assert.Equal(ChainSkipReason.SourceEffectsNegated, Assert.Single(events.OfType<ChainLinkSkipped>()).Reason);
        Assert.Equal(7900, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        FinishAttack(engine);
    }

    [Fact]
    public void LethalDamageEndsTheDuelBeforeMarkingOrSendingBattleDestroyedMonsters()
    {
        var engine = Engine(attack: 10_000); Ready(engine); Declare(engine); ToDamage(engine, DamageStep.Calculation);
        Close(engine); var result = Advance(engine);
        Assert.Equal(PlayerId.One, engine.State.Outcome!.Winner);
        Assert.Equal(0, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.NotNull(engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
        Assert.False(engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]!.BattleDestroyed);
        Assert.Empty(engine.State.GetPlayer(PlayerId.Two).Graveyard);
        Assert.Empty(result.Events.OfType<MonsterDestroyed>());
        Assert.IsType<DuelEnded>(result.Events[^1]);
        Reject(engine, new AdvanceBattle(PlayerId.One, engine.State.Battle!.Window.DecisionId), DuelError.DuelFinished);
    }

    [Theory]
    [InlineData(1800, 1200, false, 2000, 600, false, true)]
    [InlineData(1000, 2000, false, 2000, 1000, true, false)]
    [InlineData(1000, 1000, false, 1000, 0, true, true)]
    [InlineData(0, 0, false, 0, 0, false, false)]
    [InlineData(1500, 1000, true, 2000, 500, false, false)]
    [InlineData(2500, 1000, true, 2000, 0, false, true)]
    public void StagedAndAtomicCombatAgreeOnSupportedFinalOutcomes(int attack, int opposing, bool faceDown,
        int defense, int damage, bool attackerDestroyed, bool defenderDestroyed)
    {
        var staged = Engine(attack: attack, opposingAttack: opposing, opposingDefense: defense, faceDown: faceDown);
        var atomic = Engine(attack: attack, opposingAttack: opposing, opposingDefense: defense, faceDown: faceDown, staged: false);
        Ready(staged); Declare(staged); FinishAttack(staged);
        var old = Ok(atomic, new DeclareAttack(PlayerId.One, 0, 0));
        Assert.Equal(atomic.State.GetPlayer(PlayerId.One).LifePoints, staged.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(atomic.State.GetPlayer(PlayerId.Two).LifePoints, staged.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Equal(attackerDestroyed, staged.State.GetPlayer(PlayerId.One).MonsterZones[0] is null);
        Assert.Equal(defenderDestroyed, staged.State.GetPlayer(PlayerId.Two).MonsterZones[0] is null);
        Assert.Equal(damage, old.Events.OfType<BattleDamageDealt>().Sum(e => e.Damage));
        Assert.Equal(atomic.State.GetPlayer(PlayerId.One).Graveyard, staged.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Equal(atomic.State.GetPlayer(PlayerId.Two).Graveyard, staged.State.GetPlayer(PlayerId.Two).Graveyard);
    }

    [Fact]
    public void SnapshotsAndReplayCommandsAreDeterministic()
    {
        var a = Engine(); var b = Engine();
        var saved = a.State; string before = JsonSerializer.Serialize(saved);
        void Both(DuelCommand c)
        {
            var first = Ok(a, c); var second = Ok(b, c);
            Assert.Equal(JsonSerializer.Serialize(first.State), JsonSerializer.Serialize(second.State));
            Assert.Equal(first.Events.Select(e => (e.GetType().Name, JsonSerializer.Serialize(e, e.GetType()))),
                second.Events.Select(e => (e.GetType().Name, JsonSerializer.Serialize(e, e.GetType()))));
        }
        void CloseBoth()
        {
            while (!a.State.Battle!.Window.Closed)
            { var w = a.State.Battle.Window; Both(new PassBattleResponse(w.Responder, w.DecisionId)); }
        }
        CloseBoth(); Both(new AdvanceBattle(PlayerId.One, a.State.Battle!.Window.DecisionId)); CloseBoth();
        Both(new DeclareAttack(PlayerId.One, 0, 0));
        var attackSnapshot = a.State; string attackJson = JsonSerializer.Serialize(attackSnapshot);
        Assert.Throws<NotSupportedException>(() => ((IList<MonsterBinding>)attackSnapshot.Battle!.Attack!.DeclaredDefenders).Clear());
        while (a.State.Battle!.Attack is not null)
        { CloseBoth(); Both(new AdvanceBattle(PlayerId.One, a.State.Battle.Window.DecisionId)); }
        Assert.Equal(before, JsonSerializer.Serialize(saved));
        Assert.Equal(attackJson, JsonSerializer.Serialize(attackSnapshot));
    }
}
