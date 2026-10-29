using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tests;

/// <summary>
/// Synthetic legal-command scenarios for the interpreted native contract.
/// These are not original negation/interruption gameplay replays.
/// Card 2 and card 3 are invented probes, not real-card support declarations.
/// </summary>
public sealed class ChainInteractionTests
{
    private static PlayerId Other(PlayerId player) => player == PlayerId.One ? PlayerId.Two : PlayerId.One;

    private static DuelActionResult Ok(DuelEngine engine, DuelCommand command)
    {
        long revision = engine.State.Revision;
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
        Assert.Equal(revision + 1, result.State.Revision);
        return result;
    }

    private static void Reject(DuelEngine engine, DuelCommand command, DuelError error)
    {
        var before = engine.State;
        string json = JsonSerializer.Serialize(before);
        var result = engine.Execute(command);
        Assert.False(result.Succeeded);
        Assert.Equal(error, result.Error);
        Assert.Same(before, result.State);
        Assert.Same(before, engine.State);
        Assert.Equal(json, JsonSerializer.Serialize(engine.State));
        Assert.Empty(result.Events);
    }

    private static ChainResponseProfile EffectProbe(bool destroy = false) =>
        new("synthetic-effect-negation", 2, ChainOperation.NegateEffect, ChainSpellSpeed.Two,
            respondsToCategory: CardCategory.Spell, destroyNegatedSource: destroy);

    private static ChainResponseProfile CounterProbe() =>
        new("synthetic-counter-negation", 3, ChainOperation.NegateActivation, ChainSpellSpeed.Three,
            respondsToCategory: CardCategory.Trap, destroyNegatedSource: true);

    private static DuelEngine Engine(PlayerId actor = PlayerId.One, uint initialSpell = 5217,
        bool enabled = true, bool missingResponseCost = false, bool destroyEffectSource = false,
        IEnumerable<ChainResponseProfile>? profiles = null, bool setOwnTyphoonOnActivationTurn = false,
        IEnumerable<DecisionSequence>? decisionSequences = null)
    {
        uint[] ids = [1, 2, 3, 4, 4909, 5217, 4862];
        var catalog = new CardCatalog(new(new string('0', 64), "invented-chain-interaction-inputs"),
            ids.Select(id => new CardDefinition(id, $"Synthetic {id}", "", new(1000, 1000, 2),
                new(0, 0, 0, 0, 0, 0, 0), new((int)id, id, 2u << 11, 0))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(id == 1 ? CardCategory.Monster :
            id is 2 or 3 or 4862 ? CardCategory.Trap : CardCategory.Spell,
            AllowsNormalSummon: id == 1, AllowsSpellTrapSet: id != 1));
        uint[] initiatingDeck = [initialSpell, 4909, 4862, 2, 3, .. Enumerable.Repeat(1u, 35)];
        uint[] respondingDeck = [4909, 4862, 2, 3, 1, missingResponseCost ? 4u : 1u, .. Enumerable.Repeat(1u, 34)];
        var engine = new DuelEngine(catalog, rules,
            actor == PlayerId.One ? initiatingDeck : respondingDeck,
            actor == PlayerId.Two ? initiatingDeck : respondingDeck, startingPlayer: actor, decisionSequences: decisionSequences,
            experimentalActivationContracts: [ActivationResearch.LightningVortex([1]), ActivationResearch.MysticalSpaceTyphoon()],
            experimentalChainEffects: [ChainResearch.LightningVortex(), ChainResearch.MysticalSpaceTyphoon()],
            experimentalChainResponses: enabled ? profiles ??
                [ChainResearch.MysticalSpaceTyphoonResponse(), ChainResearch.MagicJammer([1]), EffectProbe(destroyEffectSource), CounterProbe()] : []);
        for (int turn = 1; turn <= 3; turn++)
        {
            var player = engine.State.ActivePlayer;
            Ok(engine, new ResolveDrawStep(player));
            Ok(engine, new ChangePhase(player, DuelPhase.Main1));
            if (turn == 3) break;
            uint[] sets = [4909, 4862, 2, 3];
            for (int zone = 0; zone < sets.Length; zone++)
            {
                if (turn == 1 && zone == 0 && setOwnTyphoonOnActivationTurn) continue;
                uint id = sets[zone];
                var card = engine.State.GetPlayer(player).Hand.Last(c => c.GameCardId == id);
                Ok(engine, new SetSpellTrap(player, card.InstanceId, zone));
            }
            if (turn == 2)
                Ok(engine, new NormalSummon(player, engine.State.GetPlayer(player).Hand.First(c => c.GameCardId == 1).InstanceId, 2));
            Ok(engine, new ChangePhase(player, DuelPhase.End));
            Ok(engine, new EndTurn(player));
        }
        if (setOwnTyphoonOnActivationTurn)
            Ok(engine, new SetSpellTrap(actor, engine.State.GetPlayer(actor).Hand.Last(c => c.GameCardId == 4909).InstanceId, 0));
        var source = engine.State.GetPlayer(actor).Hand.First(c => c.GameCardId == initialSpell);
        Ok(engine, new ActivateSpell(actor, source.InstanceId, 4));
        if (initialSpell == 5217) Pay(engine);
        else Target(engine, Other(actor), 2);
        return engine;
    }

    private static DuelActionResult Pay(DuelEngine engine)
    {
        var p = engine.State.PendingDecision!;
        return Ok(engine, new ChooseDecisionCard(p.Player, p.DecisionId, p.CardOptions[0].InstanceId));
    }

    private static DuelActionResult Target(DuelEngine engine, PlayerId player, int zone)
    {
        var p = engine.State.PendingDecision!;
        var target = p.TargetOptions.Single(t => t.Player == player && t.Zone == zone);
        return Ok(engine, new ChooseDecisionTarget(p.Player, p.DecisionId, player, zone, target.Card.InstanceId));
    }

    private static DuelActionResult Respond(DuelEngine engine, int zone)
    {
        var c = engine.State.PendingChain!;
        return Ok(engine, new ActivateChainResponse(c.Responder, c.DecisionId, zone));
    }

    private static void Pass(DuelEngine engine)
    {
        var c = engine.State.PendingChain!;
        Ok(engine, new PassChainResponse(c.Responder, c.DecisionId));
    }

    private static void CloseWindow(DuelEngine engine) { Pass(engine); Pass(engine); }
    private static DuelActionResult Resolve(DuelEngine engine) =>
        Ok(engine, new ResolveNextChainLink(engine.State.ActivePlayer, engine.State.PendingChain!.DecisionId));

    [Theory]
    [InlineData(PlayerId.One)]
    [InlineData(PlayerId.Two)]
    public void MagicJammerPaysTheResponderCostAndNegatesActivationWithoutRefund(PlayerId actor)
    {
        var engine = Engine(actor);
        var opponent = Other(actor);
        var paid = engine.State;
        var originalCost = paid.CommittedActivation!.DiscardedCost!;
        Assert.Equal(opponent, paid.PendingChain!.Responder);
        Respond(engine, 1);
        var prompt = engine.State.PendingDecision!;
        Assert.Equal(opponent, prompt.Player);
        Assert.Equal(actor, engine.State.ActivePlayer);
        Assert.All(prompt.CardOptions, c => Assert.Equal(opponent, c.Owner));
        var responseCost = prompt.CardOptions[0];
        Pay(engine);
        var committed = engine.State;
        var response = committed.PendingChain!.Links[1];
        Assert.Equal(responseCost, response.Activation.DiscardedCost);
        Assert.Equal(1, response.TargetLinkNumber);
        Assert.Equal(ChainSpellSpeed.Three, response.SpellSpeed);
        Assert.Equal(actor, committed.PendingChain.Responder);
        CloseWindow(engine);
        var result = Resolve(engine);
        var negated = result.State.PendingChain!.Links[0];
        Assert.True(negated.ActivationNegated);
        Assert.True(negated.EffectNegated);
        Assert.Collection(result.Events, e => Assert.Equal(ChainNegation.Activation, Assert.IsType<ChainLinkNegated>(e).Negation),
            e => Assert.Equal(5217u, Assert.IsType<SpellTrapDestroyed>(e).Card.GameCardId), e => Assert.IsType<ChainLinkResolved>(e));
        Assert.NotNull(result.State.GetPlayer(opponent).MonsterZones[2]);
        Assert.Null(result.State.GetPlayer(actor).SpellTrapFields[4]);
        Assert.False(committed.PendingChain.Links[0].EffectNegated);
        result = Resolve(engine);
        Assert.Equal(ChainSkipReason.ActivationNegated, Assert.Single(result.Events.OfType<ChainLinkSkipped>()).Reason);
        Assert.Empty(result.Events.OfType<MonsterDestroyed>());
        Assert.Single(result.Events.OfType<ChainTrapSentToGraveyard>());
        Assert.Equal(new uint[] { 1, 5217 }, result.State.GetPlayer(actor).Graveyard.Select(c => c.GameCardId));
        Assert.Equal(new uint[] { 1, 4862 }, result.State.GetPlayer(opponent).Graveyard.Select(c => c.GameCardId));
        Assert.Contains(originalCost, result.State.GetPlayer(actor).Graveyard);
        Assert.Contains(responseCost, result.State.GetPlayer(opponent).Graveyard);
        Assert.True(result.State.ResearchSliceComplete);
        Assert.Null(result.State.Outcome);
        Assert.NotNull(paid.GetPlayer(actor).SpellTrapFields[4]);
        Reject(engine, new EndTurn(actor), DuelError.ResearchSliceComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EffectOnlyNegationKeepsActivationDistinctAndDestructionIsExplicitPolicy(bool destroy)
    {
        var engine = Engine(destroyEffectSource: destroy);
        Respond(engine, 2);
        CloseWindow(engine);
        var result = Resolve(engine);
        var link = result.State.PendingChain!.Links[0];
        Assert.False(link.ActivationNegated);
        Assert.True(link.EffectNegated);
        Assert.Equal(ChainNegation.Effect, Assert.Single(result.Events.OfType<ChainLinkNegated>()).Negation);
        Assert.Equal(destroy, result.State.GetPlayer(PlayerId.One).SpellTrapFields[4] is null);
        Assert.Equal(destroy ? 1 : 0, result.Events.OfType<SpellTrapDestroyed>().Count());
        result = Resolve(engine);
        Assert.Equal(ChainSkipReason.EffectNegated, Assert.Single(result.Events.OfType<ChainLinkSkipped>()).Reason);
        Assert.NotNull(result.State.GetPlayer(PlayerId.Two).MonsterZones[2]);
        Assert.Equal(new uint[] { 1, 5217 }, result.State.GetPlayer(PlayerId.One).Graveyard.Select(c => c.GameCardId));
    }

    [Fact]
    public void NegatingTheNegationKeepsBothPaidCostsAndAllowsTheOriginalEffect()
    {
        var engine = Engine();
        Respond(engine, 1);
        Pay(engine);
        var responseCost = engine.State.PendingChain!.Links[1].Activation.DiscardedCost!;
        Respond(engine, 3); // invented Counter Trap probe responds to Magic Jammer
        Assert.Equal(PlayerId.Two, engine.State.PendingChain!.Responder);
        var pending = engine.State.PendingChain;
        Reject(engine, new ActivateChainResponse(PlayerId.Two, pending.DecisionId, 1), DuelError.UnsupportedChainResponse);
        CloseWindow(engine);
        Resolve(engine);
        Assert.True(engine.State.PendingChain!.Links[1].ActivationNegated);
        Assert.False(engine.State.PendingChain.Links[0].EffectNegated);
        var skipped = Resolve(engine);
        Assert.Empty(skipped.Events.OfType<ChainLinkNegated>());
        Assert.Equal(ChainSkipReason.ActivationNegated, Assert.Single(skipped.Events.OfType<ChainLinkSkipped>()).Reason);
        var final = Resolve(engine);
        Assert.Single(final.Events.OfType<MonsterDestroyed>());
        Assert.Null(final.State.GetPlayer(PlayerId.Two).MonsterZones[2]);
        Assert.Contains(responseCost, final.State.GetPlayer(PlayerId.Two).Graveyard);
        Assert.Single(final.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == 4862);
        Assert.Single(final.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == 5217);
    }

    [Theory]
    [InlineData(PlayerId.One)]
    [InlineData(PlayerId.Two)]
    public void TyphoonFinishesWithoutRetargetingAfterAnotherLinkDestroysItsTarget(PlayerId actor)
    {
        var engine = Engine(actor, initialSpell: 4909);
        var opponent = Other(actor);
        var binding = engine.State.CommittedActivation!.Target!;
        Respond(engine, 0);
        Target(engine, opponent, 2); // destroy the same placement that link 1 selected
        CloseWindow(engine);
        Resolve(engine);
        Assert.False(binding.IsStillPresent(engine.State.GetPlayer(opponent).SpellTrapFields[2]));
        var final = Resolve(engine);
        Assert.Equal(ChainSkipReason.TargetUnavailable, Assert.Single(final.Events.OfType<ChainLinkSkipped>()).Reason);
        Assert.Empty(final.Events.OfType<SpellTrapDestroyed>());
        Assert.NotNull(final.State.GetPlayer(opponent).SpellTrapFields[1]);
        Assert.Equal(new uint[] { 4909 }, final.State.GetPlayer(actor).Graveyard.Select(c => c.GameCardId));
        Assert.Equal(new uint[] { 2, 4909 }, final.State.GetPlayer(opponent).Graveyard.Select(c => c.GameCardId));
        Assert.Equal(2, final.Events.OfType<SpellSentToGraveyard>().Count());
        Assert.True(final.State.ResearchSliceComplete);
        Assert.All(final.State.Players, p => Assert.Equal(8000, p.LifePoints));
    }

    [Fact]
    public void DestroyingAnOrdinarySpellSourceDoesNotNegateItsAlreadyCommittedEffect()
    {
        var engine = Engine();
        Respond(engine, 0);
        Target(engine, PlayerId.One, 4);
        CloseWindow(engine);
        Resolve(engine);
        Assert.Null(engine.State.GetPlayer(PlayerId.One).SpellTrapFields[4]);
        var result = Resolve(engine);
        Assert.Single(result.Events.OfType<MonsterDestroyed>());
        Assert.Empty(result.Events.OfType<ChainLinkSkipped>());
        Assert.Single(result.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == 5217);
    }

    [Fact]
    public void ResponseAfterOnePassRestartsOtherPlayerFirstAndRequiresTwoFreshPasses()
    {
        var engine = Engine();
        Pass(engine);
        Assert.Equal(PlayerId.One, engine.State.PendingChain!.Responder);
        Assert.Equal(1, engine.State.PendingChain.ConsecutivePasses);
        Respond(engine, 0);
        Target(engine, PlayerId.Two, 2);
        var c = engine.State.PendingChain!;
        Assert.Equal(PlayerId.Two, c.Responder);
        Assert.Equal(0, c.ConsecutivePasses);
        Reject(engine, new ResolveNextChainLink(PlayerId.One, c.DecisionId), DuelError.ChainPending);
        Pass(engine);
        Assert.Equal(0, engine.State.PendingChain!.NextLink);
        Pass(engine);
        Assert.Equal(2, engine.State.PendingChain!.NextLink);
    }

    [Fact]
    public void LowerSpeedResponsesCannotFollowACounterTrapOrConsumeAnotherCost()
    {
        var engine = Engine();
        Respond(engine, 1);
        Pay(engine);
        var c = engine.State.PendingChain!;
        Reject(engine, new ActivateChainResponse(c.Responder, c.DecisionId, 0), DuelError.ChainResponseSpeedNotAllowed);
        Reject(engine, new ActivateChainResponse(c.Responder, c.DecisionId, 2), DuelError.UnsupportedChainResponse);
        Assert.False(engine.State.GetPlayer(PlayerId.One).SpellTrapFields[0]!.IsFaceUp);
        Assert.False(engine.State.GetPlayer(PlayerId.One).SpellTrapFields[2]!.IsFaceUp);
        Assert.Single(engine.State.GetPlayer(PlayerId.One).Graveyard);
    }

    [Fact]
    public void ResponseDecisionRejectsWrongOwnerStaleAnswerAndOtherCommandsAtomically()
    {
        var engine = Engine();
        Respond(engine, 1);
        var p = engine.State.PendingDecision!;
        int id = p.CardOptions[0].InstanceId;
        Reject(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId, id), DuelError.NotActivePlayer);
        Reject(engine, new ChooseDecisionCard(PlayerId.Two, p.DecisionId - 1, id), DuelError.StaleDecision);
        Reject(engine, new ChooseDecisionCard(PlayerId.Two, p.DecisionId, 1), DuelError.InvalidDecisionChoice);
        Reject(engine, new ChooseDecisionPosition(PlayerId.Two, p.DecisionId, BattlePosition.FaceUpAttack), DuelError.WrongDecisionKind);
        Reject(engine, new PassChainResponse(PlayerId.Two, engine.State.PendingChain!.DecisionId), DuelError.DecisionPending);
        Assert.Empty(engine.State.GetPlayer(PlayerId.Two).Graveyard);
        Pay(engine);
        Reject(engine, new ChooseDecisionCard(PlayerId.Two, p.DecisionId, id), DuelError.NoPendingDecision);
        Assert.Single(engine.State.GetPlayer(PlayerId.Two).Graveyard);
    }

    [Fact]
    public void MissingDiscardRejectsBeforeSourceFlipAndUnregisteredResponsesStayDisabled()
    {
        var engine = Engine(missingResponseCost: true);
        var c = engine.State.PendingChain!;
        Reject(engine, new ActivateChainResponse(c.Responder, c.DecisionId, 1), DuelError.DecisionUnavailable);
        Assert.False(engine.State.GetPlayer(PlayerId.Two).SpellTrapFields[1]!.IsFaceUp);
        var disabled = Engine(enabled: false);
        c = disabled.State.PendingChain!;
        Reject(disabled, new ActivateChainResponse(c.Responder, c.DecisionId, 1), DuelError.UnsupportedChainResponse);
    }

    [Fact]
    public void MagicJammerDoesNotNegateATrapResponse()
    {
        var engine = Engine();
        Respond(engine, 2);
        var c = engine.State.PendingChain!;
        Reject(engine, new ActivateChainResponse(c.Responder, c.DecisionId, 1), DuelError.UnsupportedChainResponse);
    }

    [Fact]
    public void ResponseProfilesAreImmutableAndRejectUntrustedRegistration()
    {
        uint[] costs = [1];
        var profile = ChainResearch.MagicJammer(costs);
        costs[0] = 4;
        Assert.Equal(1u, Assert.Single(profile.DiscardCardIds));
        Assert.Throws<NotSupportedException>(() => ((IList<uint>)profile.DiscardCardIds).Clear());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChainResponseProfile("bad", 2, ChainOperation.NegateEffect, ChainSpellSpeed.One));
        Assert.Throws<ArgumentException>(() => ChainResearch.MagicJammer([]));
        Assert.Throws<ArgumentNullException>(() => ChainResearch.MagicJammer(null!));
        Assert.Throws<ArgumentException>(() => Engine(profiles: [ChainResearch.MagicJammer([999])]));
        Assert.Throws<ArgumentException>(() => Engine(profiles:
            [new("wrong-id", 4909, ChainOperation.DestroyBoundSpellTrap, ChainSpellSpeed.Two)]));
        Assert.Throws<ArgumentException>(() => Engine(profiles:
            [new("bad-cost", 1, ChainOperation.NegateEffect, ChainSpellSpeed.Two)]));
        Assert.Throws<ArgumentException>(() => Engine(decisionSequences:
            [new("chain-response:magic-jammer-static-response-v1", [DecisionStep.Card(DecisionCardSource.Hand, [1])])]));
        var sequence = new BeginDecisionSequence(PlayerId.One, "chain-response:magic-jammer-static-response-v1");
        var engine = Engine();
        Reject(engine, sequence, DuelError.ChainPending);
    }

    [Fact]
    public void NegationReplayIncludesIdenticalCostsFlagsAndEvents()
    {
        string Run()
        {
            var engine = Engine();
            Respond(engine, 1);
            Pay(engine);
            CloseWindow(engine);
            var a = Resolve(engine);
            var b = Resolve(engine);
            return JsonSerializer.Serialize(new { a.State, Final = b.State,
                Events = a.Events.Concat(b.Events).Select(e => JsonSerializer.Serialize(e, e.GetType())) });
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void ResponseCostRemainsPaidWhileItsSeparateTargetDecisionWaits()
    {
        var profile = new ChainResponseProfile("mystical-space-typhoon-target-commit-v1", 4909,
            ChainOperation.DestroyBoundSpellTrap, ChainSpellSpeed.Two, [1]); // synthetic costed-target combination
        var engine = Engine(profiles: [profile]);
        Respond(engine, 0);
        var cost = engine.State.PendingDecision!.CardOptions[0];
        Pay(engine);
        var paid = engine.State;
        var p = paid.PendingDecision!;
        Assert.Equal(PlayerId.Two, p.Player);
        Assert.Equal(DecisionKind.SpellTrapTarget, p.Kind);
        Assert.Equal(cost, paid.PendingSpellActivation!.DiscardedCost);
        var source = paid.GetPlayer(PlayerId.Two).SpellTrapFields[0]!.Card;
        Reject(engine, new ChooseDecisionTarget(PlayerId.Two, p.DecisionId, PlayerId.Two, 0, source.InstanceId), DuelError.InvalidDecisionChoice);
        Assert.Equal(cost, Assert.Single(engine.State.GetPlayer(PlayerId.Two).Graveyard));
        Target(engine, PlayerId.Two, 2);
        Assert.Equal(cost, engine.State.PendingChain!.Links[1].Activation.DiscardedCost);
        CloseWindow(engine);
        Resolve(engine);
        Resolve(engine);
        Assert.Single(engine.State.GetPlayer(PlayerId.Two).Graveyard, c => c == cost);
        Assert.Single(paid.PendingChain!.Links);
    }

    [Fact]
    public void AQuickPlaySetThisTurnCannotRespondEvenWhenItsOwnerGetsPriority()
    {
        var engine = Engine(setOwnTyphoonOnActivationTurn: true);
        Pass(engine);
        var c = engine.State.PendingChain!;
        Assert.Equal(PlayerId.One, c.Responder);
        Reject(engine, new ActivateChainResponse(PlayerId.One, c.DecisionId, 0), DuelError.UnsupportedChainResponse);
        Assert.False(engine.State.GetPlayer(PlayerId.One).SpellTrapFields[0]!.IsFaceUp);
    }
}
