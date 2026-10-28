using System.Text.Json;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.ChainScenario;

namespace TagForceSpecial.Tests;

public sealed class DuelChainTests
{
    internal static DuelActionResult Commit(DuelEngine engine)
    {
        var p = engine.State.PendingDecision!;
        return p.Kind == DecisionKind.Card
            ? Ok(engine, new ChooseDecisionCard(p.Player, p.DecisionId, p.CardOptions[0].InstanceId))
            : Ok(engine, new ChooseDecisionTarget(p.Player, p.DecisionId, PlayerId.Two, 1,
                p.TargetOptions.Single(t => t.Player == PlayerId.Two && t.Zone == 1).Card.InstanceId));
    }

    internal static void PassBoth(DuelEngine engine)
    {
        for (int i = 0; i < 2; i++)
        {
            var c = engine.State.PendingChain!;
            Ok(engine, new PassChainResponse(c.Responder, c.DecisionId));
        }
    }

    internal static DuelActionResult Resolve(DuelEngine engine) =>
        Ok(engine, new ResolveNextChainLink(engine.State.ActivePlayer, engine.State.PendingChain!.DecisionId));

    private static void Reject(DuelEngine engine, DuelCommand command, DuelError error)
    {
        var before = engine.State;
        var json = JsonSerializer.Serialize(before);
        var result = engine.Execute(command);
        Assert.False(result.Succeeded);
        Assert.Equal(error, result.Error);
        Assert.Same(before, result.State);
        Assert.Same(before, engine.State);
        Assert.Equal(json, JsonSerializer.Serialize(engine.State));
        Assert.Empty(result.Events);
    }

    [Fact]
    public void ChainOptInPreservesCommitmentOnlyDefault()
    {
        var engine = Engine(5217, chains: false);
        Commit(engine);
        Assert.True(engine.State.ResearchSliceComplete);
        Assert.Null(engine.State.PendingChain);
        Assert.Single(engine.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.NotNull(engine.State.GetPlayer(PlayerId.Two).MonsterZones[2]);
    }

    [Fact]
    public void VortexRetainsCostThroughPassesThenDestroysAndCleansUpOnce()
    {
        var engine = Engine(5217);
        var before = engine.State;
        var result = Commit(engine);
        var paid = result.State;
        var cost = paid.CommittedActivation!.DiscardedCost!;
        Assert.False(paid.ResearchSliceComplete);
        Assert.IsType<ChainResponseRequested>(result.Events[^1]);
        Assert.Equal(PlayerId.Two, paid.PendingChain!.Responder);
        PassBoth(engine);
        Assert.Single(engine.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.NotNull(engine.State.GetPlayer(PlayerId.Two).MonsterZones[2]);
        result = Resolve(engine);
        Assert.Collection(result.Events, e => Assert.IsType<MonsterDestroyed>(e),
            e => Assert.IsType<ChainLinkResolved>(e), e => Assert.IsType<SpellSentToGraveyard>(e),
            e => Assert.IsType<ChainCompleted>(e));
        Assert.Equal(new uint[] { cost.GameCardId, 5217 }, result.State.GetPlayer(PlayerId.One).Graveyard.Select(c => c.GameCardId));
        Assert.Null(result.State.GetPlayer(PlayerId.Two).MonsterZones[2]);
        Assert.Single(result.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == 4028);
        Assert.True(result.State.ResearchSliceComplete);
        Assert.Null(result.State.PendingChain);
        Assert.Null(result.State.Outcome);
        Assert.NotNull(before.PendingDecision);
        Assert.Null(before.PendingChain);
        Assert.Contains(cost, before.GetPlayer(PlayerId.One).Hand);
        Assert.Single(paid.PendingChain.Links);
        Assert.NotNull(paid.GetPlayer(PlayerId.Two).MonsterZones[2]);
        Reject(engine, new EndTurn(PlayerId.One), DuelError.ResearchSliceComplete);
    }

    [Fact]
    public void TargetedScapegoatResolvesBeforeTyphoonAndTokensSurviveDestruction()
    {
        var engine = Engine(4909);
        Commit(engine);
        var committed = engine.State;
        var binding = committed.CommittedActivation!.Target!;
        var chain = committed.PendingChain!;
        var result = Ok(engine, new ActivateChainResponse(PlayerId.Two, chain.DecisionId, 1));
        Assert.Equal(PlayerId.One, result.State.ActivePlayer);
        Assert.Equal(2, result.State.PendingChain!.Links.Count);
        var field = result.State.GetPlayer(PlayerId.Two).SpellTrapFields[1];
        Assert.True(field!.IsFaceUp);
        Assert.True(binding.IsStillPresent(field));
        Assert.False(committed.GetPlayer(PlayerId.Two).SpellTrapFields[1]!.IsFaceUp);
        PassBoth(engine);
        var tokens = Resolve(engine);
        Assert.Equal(2, Assert.Single(tokens.Events.OfType<ChainLinkResolved>()).Number);
        Assert.Equal(4, tokens.Events.OfType<TokenSpecialSummoned>().Count());
        Assert.NotNull(tokens.State.GetPlayer(PlayerId.Two).SpellTrapFields[1]);
        Assert.NotNull(tokens.State.GetPlayer(PlayerId.One).SpellTrapFields[2]);
        Assert.Empty(tokens.State.GetPlayer(PlayerId.Two).Graveyard);
        var final = Resolve(engine);
        Assert.Equal(1, Assert.Single(final.Events.OfType<ChainLinkResolved>()).Number);
        Assert.Equal(5, final.State.GetPlayer(PlayerId.Two).MonsterZones.OfType<FieldMonster>().Count());
        Assert.All(final.State.GetPlayer(PlayerId.Two).MonsterZones.OfType<FieldMonster>().Where(m => m.Card.GameCardId != 4028),
            m => Assert.Equal(BattlePosition.FaceUpDefense, m.Position));
        Assert.Equal(4818u, Assert.Single(final.State.GetPlayer(PlayerId.Two).Graveyard).GameCardId);
        Assert.Equal(4909u, Assert.Single(final.State.GetPlayer(PlayerId.One).Graveyard).GameCardId);
        Assert.Single(final.Events.OfType<SpellTrapDestroyed>());
        Assert.Single(final.Events.OfType<SpellSentToGraveyard>());
        Assert.Throws<NotSupportedException>(() => ((IList<ChainLink>)chain.Links).Clear());
        Assert.Single(chain.Links);
    }

    [Fact]
    public void ResponseAnswersAndResolutionRejectAtomically()
    {
        var engine = Engine(4909);
        Commit(engine);
        var c = engine.State.PendingChain!;
        Reject(engine, new EndTurn(PlayerId.One), DuelError.ChainPending);
        Reject(engine, new PassChainResponse((PlayerId)99, c.DecisionId), DuelError.InvalidPlayer);
        Reject(engine, new PassChainResponse(PlayerId.One, c.DecisionId), DuelError.NotChainResponder);
        Reject(engine, new PassChainResponse(PlayerId.Two, c.DecisionId - 1), DuelError.StaleChainDecision);
        Reject(engine, new ResolveNextChainLink(PlayerId.One, c.DecisionId), DuelError.ChainPending);
        Reject(engine, new ActivateChainResponse(PlayerId.Two, c.DecisionId, -1), DuelError.InvalidSpellTrapZone);
        Reject(engine, new ActivateChainResponse(PlayerId.Two, c.DecisionId, 2), DuelError.UnsupportedChainResponse);
        Ok(engine, new ActivateChainResponse(PlayerId.Two, c.DecisionId, 1));
        c = engine.State.PendingChain!;
        Reject(engine, new ActivateChainResponse(PlayerId.One, c.DecisionId, 2), DuelError.UnsupportedChainResponse);
        PassBoth(engine);
        c = engine.State.PendingChain!;
        Reject(engine, new PassChainResponse(PlayerId.Two, c.DecisionId), DuelError.ChainResolving);
        Reject(engine, new ResolveNextChainLink(PlayerId.Two, c.DecisionId), DuelError.NotActivePlayer);
        Resolve(engine);
        Reject(engine, new ResolveNextChainLink(PlayerId.One, c.DecisionId), DuelError.StaleChainDecision);
        Assert.Equal(5, engine.State.GetPlayer(PlayerId.Two).MonsterZones.OfType<FieldMonster>().Count());
    }

    [Fact]
    public void TwoPassesCloseTheWindowAndRejectedResponsePreservesFirstPass()
    {
        var engine = Engine(4909);
        Commit(engine);
        var c = engine.State.PendingChain!;
        // The opponent passes, then the source player passes: resolution starts.
        Ok(engine, new PassChainResponse(PlayerId.Two, c.DecisionId));
        c = engine.State.PendingChain!;
        Assert.Equal(1, c.ConsecutivePasses);
        Assert.Equal(PlayerId.One, c.Responder);
        // Unsupported own response cannot consume that pass or change priority.
        Reject(engine, new ActivateChainResponse(PlayerId.One, c.DecisionId, 2), DuelError.UnsupportedChainResponse);
        Ok(engine, new PassChainResponse(PlayerId.One, c.DecisionId));
        Assert.Equal(1, engine.State.PendingChain!.NextLink);
    }

    [Fact]
    public void UnsupportedTokenCapacityRejectsWithoutFlippingOrAddingLink()
    {
        var engine = Engine(4909, twoMonsters: true);
        Commit(engine);
        var c = engine.State.PendingChain!;
        Reject(engine, new ActivateChainResponse(PlayerId.Two, c.DecisionId, 1), DuelError.UnsupportedChainResponse);
        Assert.False(engine.State.GetPlayer(PlayerId.Two).SpellTrapFields[1]!.IsFaceUp);
    }

    [Fact]
    public void NoResponseTyphoonDestroysTargetWithoutCreatingTokens()
    {
        var engine = Engine(4909);
        Commit(engine);
        PassBoth(engine);
        Resolve(engine);
        Assert.Single(engine.State.GetPlayer(PlayerId.Two).MonsterZones.OfType<FieldMonster>());
        Assert.Equal(4818u, Assert.Single(engine.State.GetPlayer(PlayerId.Two).Graveyard).GameCardId);
    }

    [Fact]
    public void ChainCommandsCannotStartAnUncommittedActivation()
    {
        var engine = Engine(4909);
        Reject(engine, new PassChainResponse(PlayerId.Two, 1), DuelError.NoPendingChain);
    }

    [Fact]
    public void ProfilesCopyAndValidateTokenDefinitions()
    {
        uint[] ids = [3905, 3903, 3902, 3904];
        var profile = new ChainEffectProfile("test", 4818, ChainOperation.SummonFourTokens, ids);
        ids[0] = 1;
        Assert.Equal(3905u, profile.TokenCardIds[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<uint>)profile.TokenCardIds).Clear());
        Assert.Throws<ArgumentException>(() => new ChainEffectProfile("test", 4818, ChainOperation.SummonFourTokens, [1, 1, 2, 3]));
        Assert.Throws<ArgumentException>(() => new ChainEffectProfile("test", 4818, ChainOperation.DestroyBoundSpellTrap, [1]));
    }

    [Fact]
    public void RegistrationRejectsUnboundEffectsWrongCommitmentKindsAndNonMonsterTokens()
    {
        Assert.Throws<ArgumentException>(() => Engine(4909, profiles:
            [new("wrong-id", 4909, ChainOperation.DestroyBoundSpellTrap)]));
        Assert.Throws<ArgumentException>(() => Engine(4909, profiles:
            [new("lightning-vortex-discard-commit-v1", 5217, ChainOperation.DestroyBoundSpellTrap)]));
        Assert.Throws<ArgumentException>(() => Engine(4909, profiles:
            [new("response", 4818, ChainOperation.SummonFourTokens, [4818, 3903, 3902, 3904])]));
        Assert.Throws<ArgumentException>(() => Engine(4909, profiles:
            [new("response", 4909, ChainOperation.SummonFourTokens, [3905, 3903, 3902, 3904])]));
    }

    [Fact]
    public void ReplayIncludesIdenticalSnapshotsEventsAndTokenInstances()
    {
        string Run()
        {
            var engine = Engine(4909);
            Commit(engine);
            var c = engine.State.PendingChain!;
            Ok(engine, new ActivateChainResponse(c.Responder, c.DecisionId, 1));
            PassBoth(engine);
            var a = Resolve(engine);
            var b = Resolve(engine);
            return JsonSerializer.Serialize(new { engine.State,
                Events = a.Events.Concat(b.Events).Select(e => JsonSerializer.Serialize(e, e.GetType())) });
        }
        Assert.Equal(Run(), Run());
    }
}
