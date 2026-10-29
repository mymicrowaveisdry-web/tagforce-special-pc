using System.Text.Json;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Synthetic command scenarios, not PSP trigger gameplay comparisons.</summary>
public sealed class TriggerEffectTests
{
    [Theory]
    [InlineData(PlayerId.One)]
    [InlineData(PlayerId.Two)]
    public void MandatorySummonTriggerUsesChainsAndResumesNormalPlay(PlayerId starting)
    {
        var engine = Engine(triggers: [Gain("summon", starting == PlayerId.One ? A : B)], startingPlayer: starting);
        EnterMain(engine);
        var before = engine.State;
        var result = Summon(engine, starting == PlayerId.One ? A : B);
        Assert.Collection(result.Events, e => Assert.IsType<MonsterNormalSummoned>(e), e => Assert.IsType<TriggerOrderRequested>(e));
        Assert.Null(before.EventEffects.PendingTriggers);
        Assert.Null(before.GetPlayer(starting).MonsterZones[0]);
        Assert.Equal(8000, engine.State.GetPlayer(starting).LifePoints);
        var batch = engine.State.EventEffects.PendingTriggers!;
        Assert.True(batch.Mandatory);
        Assert.Equal(engine.State.Revision, batch.DecisionId);
        Order(engine);
        var chain = engine.State.PendingChain!;
        Assert.Equal(ChainOperation.TriggerEffect, Assert.Single(chain.Links).Operation);
        Assert.Equal(starting == PlayerId.One ? PlayerId.Two : PlayerId.One, chain.Responder);
        Assert.False(chain.FreezeOnCompletion);
        Pass(engine); Pass(engine);
        var resolved = Resolve(engine);
        Assert.IsType<LifePointsGained>(resolved.Events[0]);
        Assert.Equal(8100, engine.State.GetPlayer(starting).LifePoints);
        Assert.NotNull(engine.State.GetPlayer(starting).MonsterZones[0]);
        Assert.Empty(engine.State.GetPlayer(starting).Graveyard);
        Assert.False(engine.State.ResearchSliceComplete);
        NextMain(engine);
    }

    [Fact]
    public void FourGroupsUseTurnMandatoryOpponentMandatoryTurnOptionalOpponentOptional()
    {
        var profiles = new[] {
            Gain("a-m1", A, relation: EffectRelation.Any), Gain("a-m2", A, relation: EffectRelation.Any),
            Gain("a-o", A, relation: EffectRelation.Any, requirement: TriggerRequirement.Optional),
            Gain("b-m", B, relation: EffectRelation.Any),
            Gain("b-o1", B, relation: EffectRelation.Any, requirement: TriggerRequirement.Optional),
            Gain("b-o2", B, relation: EffectRelation.Any, requirement: TriggerRequirement.Optional) };
        var engine = Engine(triggers: profiles);
        EnterMain(engine); Summon(engine, A); Drain(engine); NextMain(engine);
        Summon(engine, B);
        var pending = engine.State.EventEffects.PendingTriggers!;
        Assert.Equal(PlayerId.Two, pending.Player); Assert.True(pending.Mandatory);
        Order(engine);
        Assert.Equal(PlayerId.One, engine.State.EventEffects.PendingTriggers!.Player);
        Assert.True(engine.State.EventEffects.PendingTriggers.Mandatory);
        Order(engine, reverse: true);
        Assert.Equal(PlayerId.Two, engine.State.EventEffects.PendingTriggers!.Player);
        Assert.False(engine.State.EventEffects.PendingTriggers.Mandatory);
        // Accept only the second optional trigger, preserving the player's selected order.
        var optional = engine.State.EventEffects.PendingTriggers;
        Ok(engine, new ChooseTriggerOrder(optional.Player, optional.DecisionId, [optional.Options[1].Id]));
        Assert.Equal(PlayerId.One, engine.State.EventEffects.PendingTriggers!.Player);
        Order(engine, decline: true);
        Assert.Equal(new[] { "b-m", "a-m2", "a-m1", "b-o2" }, engine.State.PendingChain!.Links.Select(l => l.Activation.EffectId));
        Assert.Equal(PlayerId.One, engine.State.PendingChain.Responder);
        var resolved = Drain(engine).OfType<LifePointsGained>().Select(e => e.EffectId);
        Assert.Equal(new[] { "b-o2", "a-m1", "a-m2", "b-m" }, resolved);
    }

    [Fact]
    public void InvalidOrderingAndCommandsPreserveThePublishedSnapshot()
    {
        var engine = Engine(triggers: [Gain("first", A), Gain("second", A)]);
        EnterMain(engine); Summon(engine, A);
        var batch = engine.State.EventEffects.PendingTriggers!;
        int id = batch.Options[0].Id;
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.TriggerOrderPending);
        Reject(engine, new ChooseTriggerOrder(PlayerId.One, batch.DecisionId - 1, [id]), DuelError.StaleTriggerDecision);
        Reject(engine, new ChooseTriggerOrder(PlayerId.Two, batch.DecisionId, [id]), DuelError.NotTriggerPlayer);
        Reject(engine, new ChooseTriggerOrder(PlayerId.One, batch.DecisionId, [id, id]), DuelError.InvalidTriggerOrder);
        Reject(engine, new ChooseTriggerOrder(PlayerId.One, batch.DecisionId, []), DuelError.InvalidTriggerOrder);
        Reject(engine, new ChooseTriggerOrder(PlayerId.One, batch.DecisionId, [int.MaxValue]), DuelError.InvalidTriggerOrder);
        Reject(engine, new ChooseTriggerOrder(PlayerId.One, batch.DecisionId, null!), DuelError.InvalidTriggerOrder);
        string saved = JsonSerializer.Serialize(batch);
        var result = Order(engine);
        Assert.Equal(saved, JsonSerializer.Serialize(batch));
        var chosen = result.Events.OfType<TriggerOrderChosen>().Single();
        Assert.Throws<NotSupportedException>(() => ((IList<int>)chosen.TriggerIds)[0] = 99);
        Assert.Throws<NotSupportedException>(() => ((IList<TriggerActivation>)batch.Options).Clear());
    }

    [Fact]
    public void OptionalTriggersCanAllBeDeclinedWithoutCreatingAnEmptyChain()
    {
        var engine = Engine(triggers: [Gain("optional", A, requirement: TriggerRequirement.Optional)]);
        EnterMain(engine); Summon(engine, A); Order(engine, decline: true);
        Assert.Null(engine.State.PendingChain);
        Assert.Null(engine.State.EventEffects.PendingTriggers);
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Reject(engine, new ChooseTriggerOrder(PlayerId.One, engine.State.Revision, []), DuelError.NoPendingTriggers);
        NextMain(engine);
    }

    [Fact]
    public void FaceDownSetDoesNotTriggerButLaterFlipSummonDoes()
    {
        var engine = Engine(triggers: [Gain("normal", A), Gain("flip", A, MonsterEventKind.FlipSummoned)]);
        EnterMain(engine); Summon(engine, A, set: true);
        Assert.Null(engine.State.EventEffects.PendingTriggers);
        NextMain(engine); NextMain(engine);
        Ok(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack));
        Assert.Equal("flip", Assert.Single(engine.State.EventEffects.PendingTriggers!.Options).Profile.EffectId);
        Drain(engine);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Theory]
    [InlineData(ChainOperation.NegateEffect, false)]
    [InlineData(ChainOperation.NegateActivation, true)]
    public void NegatingAMonsterTriggerDefersItsDestructionTriggerUntilTheChainCompletes(ChainOperation operation, bool destroy)
    {
        var engine = Engine(triggers: [Gain("summon", A), Gain("destroyed", A, MonsterEventKind.Destroyed,
            gain: 400, location: TriggerSourceLocation.DestroyedSelf)], responses:
            [new("monster-negation", Trap, operation, ChainSpellSpeed.Two, respondsToCategory: CardCategory.Monster, destroyNegatedSource: destroy)]);
        EnterMain(engine); NextMain(engine); Set(engine, Trap); NextMain(engine);
        Summon(engine, A); Order(engine);
        var chain = engine.State.PendingChain!;
        Ok(engine, new ActivateChainResponse(chain.Responder, chain.DecisionId, 0));
        Pass(engine); Pass(engine);
        var response = Resolve(engine);
        Assert.Single(response.Events.OfType<ChainLinkNegated>());
        Assert.Null(engine.State.EventEffects.PendingTriggers);
        Assert.Equal(destroy ? 1 : 0, engine.State.EventEffects.DeferredTriggers.Count);
        Assert.Equal(1, engine.State.PendingChain!.NextLink);
        var finish = Resolve(engine);
        Assert.Single(finish.Events.OfType<ChainLinkSkipped>());
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Contains(engine.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == Trap);
        if (destroy)
        {
            Assert.NotNull(engine.State.EventEffects.PendingTriggers);
            Assert.IsType<ChainCompleted>(finish.Events[^2]);
            Drain(engine);
            Assert.Equal(8400, engine.State.GetPlayer(PlayerId.One).LifePoints);
            Assert.Null(engine.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        }
        else Assert.Null(engine.State.EventEffects.PendingTriggers);
    }

    [Fact]
    public void DestroyingASharedSourceByNegatingOneTriggerDoesNotNegateItsOtherTrigger()
    {
        var engine = Engine(triggers: [Gain("a-first", A, gain: 100), Gain("b-second", A, gain: 250)], responses:
            [new("destroy-and-negate", Trap, ChainOperation.NegateEffect, ChainSpellSpeed.Two, destroyNegatedSource: true)]);
        EnterMain(engine); NextMain(engine); Set(engine, Trap); NextMain(engine);
        Summon(engine, A); Order(engine);
        var chain = engine.State.PendingChain!;
        Ok(engine, new ActivateChainResponse(chain.Responder, chain.DecisionId, 0));
        Pass(engine); Pass(engine);
        Resolve(engine);
        Assert.Null(engine.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.False(engine.State.PendingChain!.Links[0].EffectNegated);
        Assert.True(engine.State.PendingChain.Links[1].EffectNegated);
        var events = Drain(engine);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal("a-first", Assert.Single(events.OfType<LifePointsGained>()).EffectId);
        Assert.Single(engine.State.GetPlayer(PlayerId.One).Graveyard);
    }

    [Fact]
    public void SpecialSummonTriggerWaitsForDecisionCompletionAndPreservesLifecycleGate()
    {
        var engine = Engine(triggers: [Gain("special", A, MonsterEventKind.SpecialSummoned)]);
        EnterMain(engine);
        var player = engine.State.GetPlayer(PlayerId.One);
        Ok(engine, new ActivateSpell(PlayerId.One, player.Hand.First(c => c.GameCardId == SummonSpell).InstanceId, 0));
        var pending = engine.State.PendingDecision!;
        Ok(engine, new ChooseDecisionCard(PlayerId.One, pending.DecisionId, pending.CardOptions[0].InstanceId));
        pending = engine.State.PendingDecision!;
        Ok(engine, new ChooseDecisionPosition(PlayerId.One, pending.DecisionId, BattlePosition.FaceUpAttack));
        pending = engine.State.PendingDecision!;
        var result = Ok(engine, new ChooseDecisionZone(PlayerId.One, pending.DecisionId, 1));
        Assert.Null(engine.State.PendingDecision);
        Assert.Single(result.Events.OfType<TriggerOrderRequested>());
        Assert.Single(engine.State.PendingEndPhaseBanishments);
        Drain(engine);
        Assert.Single(engine.State.PendingEndPhaseBanishments);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.True(engine.State.ResearchSliceComplete);
        Assert.Single(engine.State.GetPlayer(PlayerId.One).Banished);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void SimultaneousBattleDestructionCreatesOneBatchAndTerminalDamageCreatesNone()
    {
        var profiles = new[] { Gain("a-destroyed", A, MonsterEventKind.Destroyed, location: TriggerSourceLocation.DestroyedSelf),
            Gain("b-destroyed", B, MonsterEventKind.Destroyed, location: TriggerSourceLocation.DestroyedSelf) };
        var engine = Engine(triggers: profiles, attackB: 1000);
        EnterMain(engine); Summon(engine, A); NextMain(engine); Summon(engine, B);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var battle = Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Equal(2, battle.Events.OfType<MonsterDestroyed>().Count());
        Assert.Single(battle.Events.OfType<TriggerOrderRequested>());
        Assert.Equal(2, engine.State.EventEffects.PendingTriggers!.Remaining.Count);
        Assert.Equal(PlayerId.Two, engine.State.EventEffects.PendingTriggers.Player);
        Drain(engine);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        var lethal = Engine(triggers: profiles, attackB: 10_000);
        EnterMain(lethal); Summon(lethal, A); NextMain(lethal); Summon(lethal, B);
        Ok(lethal, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var end = Ok(lethal, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.NotNull(lethal.State.Outcome);
        Assert.Empty(end.Events.OfType<MonsterDestroyed>());
        Assert.Null(lethal.State.EventEffects.PendingTriggers);
        Assert.Empty(lethal.State.EventEffects.DeferredTriggers);
    }

    [Fact]
    public void FourSimultaneousTokenEventsWaitUntilLowerLinksAndCleanupHaveCompleted()
    {
        uint[] first = [4909, A, A, Trap, Filler, .. Enumerable.Repeat(Filler, 35)];
        var engine = Engine(triggers: [Gain("token-watch", A, MonsterEventKind.SpecialSummoned, EffectRelation.Any)], playerOneDeck: first);
        EnterMain(engine); Summon(engine, A); NextMain(engine); Set(engine, 4818); NextMain(engine);
        var source = engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == 4909);
        Ok(engine, new ActivateSpell(PlayerId.One, source.InstanceId, 0));
        var choice = engine.State.PendingDecision!;
        var target = choice.TargetOptions.Single(t => t.Player == PlayerId.Two);
        Ok(engine, new ChooseDecisionTarget(PlayerId.One, choice.DecisionId, target.Player, target.Zone, target.Card.InstanceId));
        var chain = engine.State.PendingChain!;
        Ok(engine, new ActivateChainResponse(PlayerId.Two, chain.DecisionId, 0));
        Pass(engine); Pass(engine);
        var tokens = Resolve(engine);
        Assert.Equal(4, tokens.Events.OfType<TokenSpecialSummoned>().Count());
        Assert.Equal(4, engine.State.EventEffects.DeferredTriggers.Count);
        Assert.Null(engine.State.EventEffects.PendingTriggers);
        var saved = engine.State;
        var finish = Resolve(engine);
        Assert.Single(finish.Events.OfType<ChainCompleted>());
        Assert.NotNull(engine.State.EventEffects.PendingTriggers);
        Assert.Equal(4, engine.State.EventEffects.PendingTriggers.Options.Count);
        Assert.Equal(4, engine.State.EventEffects.PendingTriggers.Options.Select(t => t.Occurrence.Subject.Card.InstanceId).Distinct().Count());
        Assert.Equal(4, saved.EventEffects.DeferredTriggers.Count);
        Assert.Empty(engine.State.EventEffects.DeferredTriggers);
        Order(engine, reverse: true);
        Assert.True(engine.State.PendingChain!.FreezeOnCompletion);
        Drain(engine);
        Assert.Equal(8400, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.True(engine.State.ResearchSliceComplete);
        Assert.NotNull(engine.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.ResearchSliceComplete);
    }

    [Fact]
    public void TriggerLocationsAreRecheckedAtTheActivationBoundary()
    {
        var engine = Engine(attackB: 1000, triggers: [
            Gain("a-field-watch", A, MonsterEventKind.Destroyed, EffectRelation.Any),
            Gain("b-field-watch", B, MonsterEventKind.Destroyed, EffectRelation.Any),
            Gain("a-grave", A, MonsterEventKind.Destroyed, location: TriggerSourceLocation.DestroyedSelf),
            Gain("b-grave", B, MonsterEventKind.Destroyed, location: TriggerSourceLocation.DestroyedSelf)]);
        EnterMain(engine); Summon(engine, A); NextMain(engine); Summon(engine, B);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var battle = Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Equal(4, battle.Events.OfType<TriggerUnavailable>().Count());
        Assert.All(engine.State.EventEffects.PendingTriggers!.Remaining,
            t => Assert.Equal(TriggerSourceLocation.DestroyedSelf, t.Profile.SourceLocation));
        Drain(engine);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void CommandsAndEventsReplayDeterministicallyWithTriggersAndModifiersEnabled()
    {
        var profiles = new[] { Gain("first", A), Gain("second", A, gain: 200) };
        var first = Engine(triggers: profiles, continuous: [new("aura", A, EffectRelation.Controller, 150, 50)]);
        var second = Engine(triggers: profiles.Reverse(), continuous: [new("aura", A, EffectRelation.Controller, 150, 50)]);
        void Both(DuelCommand command)
        {
            var a = Ok(first, command); var b = Ok(second, command);
            Assert.Equal(JsonSerializer.Serialize(a.State), JsonSerializer.Serialize(b.State));
            Assert.Equal(a.Events.Select(e => (e.GetType().Name, JsonSerializer.Serialize(e, e.GetType()))),
                b.Events.Select(e => (e.GetType().Name, JsonSerializer.Serialize(e, e.GetType()))));
        }
        Both(new ResolveDrawStep(PlayerId.One)); Both(new ChangePhase(PlayerId.One, DuelPhase.Main1));
        Both(new NormalSummon(PlayerId.One, first.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0));
        var pending = first.State.EventEffects.PendingTriggers!;
        Both(new ChooseTriggerOrder(pending.Player, pending.DecisionId, pending.Options.Select(t => t.Id).Reverse().ToArray()));
        for (int i = 0; i < 2; i++)
        {
            var chain = first.State.PendingChain!; Both(new PassChainResponse(chain.Responder, chain.DecisionId));
        }
        while (first.State.PendingChain is { } chain)
            Both(new ResolveNextChainLink(PlayerId.One, chain.DecisionId));
        Both(new ChangePhase(PlayerId.One, DuelPhase.End));
    }

    [Fact]
    public void RegistrationIsCopiedAndUnregisteredCardsHaveNoAutomaticEffects()
    {
        var profiles = new[] { Gain("registered", A) };
        var engine = Engine(triggers: profiles);
        profiles[0] = Gain("changed", A, gain: 999);
        EnterMain(engine); Summon(engine, A); Drain(engine);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.One).LifePoints);
        var plain = Engine(); EnterMain(plain); Summon(plain, A);
        Assert.Null(plain.State.EventEffects.PendingTriggers);
        Assert.Equal(8000, plain.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void InvalidAndConflictingProfilesAreRejectedAtRegistration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Gain("zero", 0));
        Assert.Throws<ArgumentException>(() => Engine(triggers: [Gain("unknown", 17)]));
        Assert.Throws<ArgumentException>(() => Engine(triggers: [Gain("spell-source", Trap)]));
        Assert.Throws<ArgumentException>(() => Engine(triggers: [Gain("duplicate", A), Gain("duplicate", B)]));
        Assert.Throws<ArgumentException>(() => Engine(triggers: [Gain("shared", A)],
            continuous: [new("shared", B, EffectRelation.Self, 100, 0)]));
        Assert.Throws<ArgumentException>(() => Gain("bad-location", A, location: TriggerSourceLocation.DestroyedSelf));
        Assert.Throws<ArgumentException>(() => new TriggerEffectProfile("bad-payload", A, MonsterEventKind.NormalSummoned,
            EffectRelation.Self, TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100, attackDelta: 1));
    }
}
