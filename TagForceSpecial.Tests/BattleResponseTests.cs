using TagForceSpecial.Core;
using static TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

public sealed class BattleResponseTests
{
    private static EffectDefinition Summoning(uint card = Summon, BattleTiming windows = BattleTiming.AttackDeclaration) =>
        ResponseProbe.SummonFromHand("hand-summon", card, windows, card == Stats ? A : B);

    [Fact]
    public void ANewDefenderCausesAReplayInsteadOfConvertingTheDeclaredDirectAttackAutomatically()
    {
        var engine = Engine(defender: false, profiles: [Summoning()]); Ready(engine); Declare(engine, null);
        var declared = engine.State;
        int card = engine.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == B).InstanceId;
        Respond(engine, 2, card, 1); DrainEffects(engine);
        Assert.True(engine.State.Battle!.Attack!.ReplayRequired);
        Assert.Null(engine.State.Battle.Attack.Defender);
        Assert.Empty(declared.Battle!.Attack!.DeclaredDefenders);
        Close(engine);
        Reject(engine, new AdvanceBattle(PlayerId.One, engine.State.Battle.Window.DecisionId), DuelError.AttackReplayPending);
        Reject(engine, new ChooseAttackReplay(PlayerId.One, engine.State.Battle.Window.DecisionId), DuelError.DirectAttackBlocked);
        var chosen = Ok(engine, new ChooseAttackReplay(PlayerId.One, engine.State.Battle.Window.DecisionId, 1));
        Assert.Empty(chosen.Events.OfType<AttackDeclared>());
        Assert.Single(chosen.Events.OfType<AttackReplayChosen>());
        Assert.False(engine.State.Battle!.Attack!.ReplayRequired);
        FinishAttack(engine);
        Assert.Equal(7800, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void DecliningReplaySpendsTheOriginalAttackButAllowsAnotherAttacker()
    {
        var engine = Engine(defender: false, additionalAttacker: true, profiles: [Summoning()]); Ready(engine); Declare(engine, null);
        int card = engine.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == B).InstanceId;
        Respond(engine, 2, card, 1); DrainEffects(engine); Close(engine);
        long id = engine.State.Battle!.Window.DecisionId;
        Reject(engine, new ChooseAttackReplay(PlayerId.One, id - 1, 1), DuelError.StaleBattleDecision);
        Reject(engine, new ChooseAttackReplay(PlayerId.Two, id, 1), DuelError.NotActivePlayer);
        Reject(engine, new ChooseAttackReplay(PlayerId.One, id, 0), DuelError.MonsterZoneEmpty);
        Reject(engine, new ChooseAttackReplay(PlayerId.One, id, 1, Cancel: true), DuelError.InvalidDecisionChoice);
        Ok(engine, new ChooseAttackReplay(PlayerId.One, id, Cancel: true));
        Assert.Null(engine.State.Battle!.Attack); Close(engine);
        Reject(engine, new DeclareAttack(PlayerId.One, 0, 1), DuelError.AlreadyAttacked);
        Ok(engine, new DeclareAttack(PlayerId.One, 1, 1));
        FinishAttack(engine);
        Assert.Equal(7800, engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void ATransientDefenderChangeStillRequiresReplayWhenTheOriginalRosterReturns()
    {
        var remove = new TriggerEffectProfile("remove-new", A, MonsterEventKind.SpecialSummoned, EffectRelation.Opponent,
            TriggerRequirement.Mandatory, TriggerOperation.DestroyTargetMonster, target: new(EffectRelation.Opponent));
        var engine = Engine(profiles: [Summoning()], triggers: [remove]); Ready(engine); Declare(engine);
        var original = engine.State.Battle!.Attack!.DeclaredDefenders.ToArray();
        int card = engine.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == B).InstanceId;
        Respond(engine, 2, card, 1);
        ResolveChain(engine);
        var batch = engine.State.EventEffects.PendingTriggers!;
        Ok(engine, new ChooseTriggerOrder(batch.Player, batch.DecisionId, batch.Options.Select(t => t.Id).ToArray()));
        var target = engine.State.EventEffects.PendingCommitment!;
        var summoned = target.TargetOptions.Single(t => t.Zone == 1);
        Ok(engine, new ChooseTriggerMonsterTarget(target.Player, target.DecisionId, summoned.Player, summoned.Zone, summoned.Card.InstanceId));
        DrainEffects(engine);
        Assert.Null(engine.State.GetPlayer(PlayerId.Two).MonsterZones[1]);
        Assert.Equal(original[0].Card, engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]!.Card);
        Assert.True(engine.State.Battle!.Attack!.ReplayRequired);
        Close(engine);
        Ok(engine, new ChooseAttackReplay(PlayerId.One, engine.State.Battle.Window.DecisionId, 0));
        FinishAttack(engine);
        Assert.Equal(7400, engine.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void AddingAnAttackingPlayersMonsterDoesNotReplayTheOpponentsUnchangedField()
    {
        var engine = Engine(profiles: [Summoning(Stats)]); Ready(engine); Declare(engine);
        var w = engine.State.Battle!.Window; Ok(engine, new PassBattleResponse(w.Responder, w.DecisionId));
        int card = engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == A).InstanceId;
        Respond(engine, 0, card, 1); DrainEffects(engine);
        Assert.False(engine.State.Battle!.Attack!.ReplayRequired);
        Assert.NotNull(engine.State.GetPlayer(PlayerId.One).MonsterZones[1]);
        FinishAttack(engine);
    }

    [Fact]
    public void AttackNegationCancelsDamageAndDoesNotRestoreTheAttackAllowance()
    {
        var engine = Engine(profiles: [ResponseProbe.NegateAttack("negate", Negate)]);
        Ready(engine); Declare(engine); Respond(engine, 0);
        var events = DrainEffects(engine);
        Assert.Equal(BattleAttackCancellation.Negated, engine.State.Battle!.Attack!.Cancellation);
        Assert.Single(events.OfType<AttackCancelled>());
        var finished = FinishAttack(engine);
        Assert.Empty(finished.OfType<BattleDamageDealt>());
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Close(engine); Reject(engine, new DeclareAttack(PlayerId.One, 0, 0), DuelError.AlreadyAttacked);
    }

    [Fact]
    public void DestroyingAnAttackerDuringTheDeclarationWindowCancelsThatAttack()
    {
        var engine = Engine(profiles: [ResponseProbe.DestroyAttacker("destroy", Destroy)]);
        Ready(engine); Declare(engine); Respond(engine, 1); DrainEffects(engine);
        Assert.Equal(BattleAttackCancellation.AttackerUnavailable, engine.State.Battle!.Attack!.Cancellation);
        Assert.Null(engine.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        FinishAttack(engine);
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Contains(engine.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == Destroy);
    }

    [Fact]
    public void NegatingAnAttackNegationUsesTheExistingChainAndLetsTheAttackContinue()
    {
        var profile = ResponseProbe.Negate("counter", Counter, ResponseNegation.Activation, ChainSpellSpeed.Three, respondsTo: CardCategory.Trap);
        var engine = Engine(chains: [profile], profiles: [ResponseProbe.NegateAttack("negate", Negate)]);
        Ready(engine); Declare(engine); Respond(engine, 0);
        var chain = engine.State.PendingChain!;
        Ok(engine, new ActivateChainResponse(chain.Responder, chain.DecisionId, 1));
        var events = DrainEffects(engine);
        Assert.Single(events.OfType<ChainLinkNegated>());
        Assert.Null(engine.State.Battle!.Attack!.Cancellation);
        Assert.False(engine.State.ResearchSliceComplete);
        FinishAttack(engine);
        Assert.Equal(7400, engine.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void BeforeCalculationStatModifiersAffectDamageThenExpireAfterTheDamageStep()
    {
        var boost = ResponseProbe.ModifyCombatant("boost", Stats, BattleTiming.BeforeCalculation, 1000);
        var engine = Engine(profiles: [boost]); Ready(engine); Declare(engine); ToDamage(engine, DamageStep.BeforeCalculation);
        Respond(engine, 0); DrainEffects(engine);
        Assert.Equal(2800, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        var modifier = Assert.Single(engine.State.EventEffects.TimedModifiers);
        Assert.Null(modifier.Source); Assert.Equal(Stats, modifier.SpellTrapSource!.Card.GameCardId);
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        var events = FinishAttack(engine);
        Assert.Equal(6400, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Equal(1800, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal(ModifierRemovalReason.DamageStepEnded, Assert.Single(events.OfType<StatModifierRemoved>()).Reason);
    }

    [Fact]
    public void DuringCalculationStatChangesRequireExplicitExceptionMetadata()
    {
        Assert.Throws<ArgumentException>(() => ResponseProbe.ModifyCombatant("ordinary", Stats, BattleTiming.Calculation, 500));
        var boost = ResponseProbe.ModifyCombatant("exception", Stats, BattleTiming.Calculation, 500, duringCalculation: true);
        var engine = Engine(profiles: [boost]); Ready(engine); Declare(engine); ToDamage(engine, DamageStep.Calculation);
        Respond(engine, 0); DrainEffects(engine); FinishAttack(engine);
        Assert.Equal(6900, engine.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void RemovingAnAttackerDuringDamageDoesNotReplayOrRevealAFaceDownDefender()
    {
        var engine = Engine(faceDown: true, profiles: [ResponseProbe.DestroyAttacker("damage-removal", Destroy, BattleTiming.DamageStart)]);
        Ready(engine); Declare(engine); ToDamage(engine, DamageStep.Start);
        var w = engine.State.Battle!.Window; Ok(engine, new PassBattleResponse(w.Responder, w.DecisionId));
        Respond(engine, 1); DrainEffects(engine);
        Assert.False(engine.State.Battle!.Attack!.ReplayRequired);
        var events = FinishAttack(engine);
        Assert.Single(events.OfType<BattleDamageCalculationSkipped>());
        Assert.Empty(events.OfType<MonsterRevealed>());
        Assert.Empty(events.OfType<BattleDamageDealt>());
        Assert.Equal(BattlePosition.FaceDownDefense, engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]!.Position);
    }

    [Fact]
    public void LosingTheDefenderDuringDamageSkipsCalculationWithoutReplayingOrAttackingDirectly()
    {
        var remove = new TriggerEffectProfile("remove-target", A, MonsterEventKind.SpecialSummoned, EffectRelation.Opponent,
            TriggerRequirement.Mandatory, TriggerOperation.DestroyTargetMonster, target: new(EffectRelation.Opponent));
        var engine = Engine(profiles: [Summoning(windows: BattleTiming.DamageStart)], triggers: [remove]);
        Ready(engine); Declare(engine); ToDamage(engine, DamageStep.Start);
        var w = engine.State.Battle!.Window; Ok(engine, new PassBattleResponse(w.Responder, w.DecisionId));
        int card = engine.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == B).InstanceId;
        Respond(engine, 2, card, 1);
        ResolveChain(engine);
        var batch = engine.State.EventEffects.PendingTriggers!;
        Ok(engine, new ChooseTriggerOrder(batch.Player, batch.DecisionId, batch.Options.Select(t => t.Id).ToArray()));
        var prompt = engine.State.EventEffects.PendingCommitment!; var target = prompt.TargetOptions.Single(t => t.Zone == 0);
        Ok(engine, new ChooseTriggerMonsterTarget(prompt.Player, prompt.DecisionId, target.Player, target.Zone, target.Card.InstanceId));
        DrainEffects(engine);
        Assert.False(engine.State.Battle!.Attack!.ReplayRequired);
        var events = FinishAttack(engine);
        Assert.Single(events.OfType<BattleDamageCalculationSkipped>());
        Assert.Empty(events.OfType<BattleDamageDealt>());
        Assert.NotNull(engine.State.GetPlayer(PlayerId.Two).MonsterZones[1]);
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void ARevealedOptionalFlipGetsItsDelayedWindowEvenWhenCalculationIsSkipped()
    {
        var flip = new TriggerEffectProfile("optional-flip", B, MonsterEventKind.FlippedFaceUp, EffectRelation.Self,
            TriggerRequirement.Optional, TriggerOperation.GainLifePoints, lifePointGain: 100, timing: TriggerTiming.When);
        var engine = Engine(faceDown: true, triggers: [flip], profiles: [ResponseProbe.DestroyAttacker("remove", Destroy, BattleTiming.BeforeCalculation)]);
        Ready(engine); Declare(engine); ToDamage(engine, DamageStep.BeforeCalculation);
        Assert.Null(engine.State.EventEffects.PendingTriggers);
        var w = engine.State.Battle!.Window; Ok(engine, new PassBattleResponse(w.Responder, w.DecisionId));
        Respond(engine, 1); DrainEffects(engine);
        ToDamage(engine, DamageStep.AfterCalculation);
        Assert.NotNull(engine.State.EventEffects.PendingTriggers);
        DrainEffects(engine);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        FinishAttack(engine);
    }

    [Fact]
    public void WrongWindowAndUnsupportedChoicesAreAtomic()
    {
        var engine = Engine(profiles: [Summoning()]);
        var w = engine.State.Battle!.Window;
        Reject(engine, new ActivateBattleResponse(w.Responder, w.DecisionId, 2), DuelError.UnsupportedBattleResponse);
        Ready(engine); Declare(engine); w = engine.State.Battle!.Window;
        Reject(engine, new ActivateBattleResponse(PlayerId.One, w.DecisionId, 2), DuelError.NotBattleResponder);
        Reject(engine, new ActivateBattleResponse(PlayerId.Two, w.DecisionId - 1, 2), DuelError.StaleBattleDecision);
        Reject(engine, new ActivateBattleResponse(PlayerId.Two, w.DecisionId, 5), DuelError.InvalidSpellTrapZone);
        Respond(engine, 2);
        while (engine.State.PendingChain is { DefinitionSummon: null } chain)
            Ok(engine, chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId)
                : new ResolveNextChainLink(PlayerId.One, chain.DecisionId));
        var summon = engine.State.PendingChain!.DefinitionSummon!;
        int card = engine.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == B).InstanceId;
        Reject(engine, new ChooseDefinitionSummon(PlayerId.Two, summon.DecisionId, card, 0, BattlePosition.FaceUpDefense), DuelError.InvalidMonsterZone);
        Reject(engine, new ChooseDefinitionSummon(PlayerId.Two, summon.DecisionId, int.MaxValue, 1, BattlePosition.FaceUpDefense), DuelError.InvalidDecisionChoice);
        Reject(engine, new ChooseDefinitionSummon(PlayerId.One, summon.DecisionId, card, 1, BattlePosition.FaceUpDefense), DuelError.NotActivePlayer);
    }

    [Fact]
    public void BattleDefinitionsValidateTheirTimingsAndTargets()
    {
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", Negate, EffectKind.NormalTrap, [new NegateAttackStep()],
            fieldTargets: [new AttackingMonsterTarget()]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", Stats, EffectKind.NormalTrap, [new ModifyCombatantStatsStep(0, 0)],
            [new BattleTimingCondition(BattleTiming.DamageStart)], fieldTargets: [new AttackingMonsterTarget()]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", Stats, EffectKind.NormalTrap, [new DealDamageStep(1)],
            [new BattleTimingCondition(BattleTiming.None)]));
        Assert.Throws<ArgumentException>(() => Engine(profiles: [ResponseProbe.NegateAttack("unknown", 17)]));
    }

    private static void ResolveChain(DuelEngine engine)
    {
        while (engine.State.PendingChain is { } chain)
        {
            if (chain.DefinitionSummon is not null) EventEffectScenario.ChooseSummon(engine);
            else if (chain.NextLink == 0) Ok(engine, new PassChainResponse(chain.Responder, chain.DecisionId));
            else Ok(engine, new ResolveNextChainLink(PlayerId.One, chain.DecisionId));
        }
    }
}