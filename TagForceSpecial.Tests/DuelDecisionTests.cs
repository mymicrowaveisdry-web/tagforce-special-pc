using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tests;

/// <summary>Managed workflow contracts only; no PSP effect/activation equivalence is claimed.</summary>
public sealed class DuelDecisionTests
{
    private static DecisionSequence Flow(bool stop = false, DecisionCardSource source = DecisionCardSource.Deck) =>
        new("select", [DecisionStep.Card(source, [1]), DecisionStep.Position([BattlePosition.FaceUpAttack]),
            DecisionStep.EmptyMonsterZone()], stop);

    private static DuelEngine Engine(IEnumerable<DecisionSequence>? sequences = null, PlayerId starter = PlayerId.One)
    {
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-decisions"),
            Enumerable.Range(1, 4).Select(i => new CardDefinition((uint)i, $"Synthetic {i}", "",
                new(1000, 1000, 4), new(0, 0, 0, 0, 0, 0, 0), new(i, (uint)i, 4u << 11, 0))));
        var rules = catalog.Cards.ToDictionary(c => c.GameCardId, _ => new DuelCardRules(CardCategory.Monster, true));
        uint[] deck = [1, 2, 3, 4, 1, .. Enumerable.Repeat(1u, 35)];
        return new(catalog, rules, deck, deck, starter, sequences);
    }

    private static DuelActionResult Ok(DuelEngine engine, DuelCommand command)
    {
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
        Assert.Same(engine.State, result.State);
        return result;
    }

    private static void Reject(DuelEngine engine, DuelCommand command, DuelError error)
    {
        var before = engine.State;
        string serialized = JsonSerializer.Serialize(before);
        var result = engine.Execute(command);
        Assert.False(result.Succeeded);
        Assert.Equal(error, result.Error);
        Assert.Same(before, result.State);
        Assert.Same(before, engine.State);
        Assert.Equal(serialized, JsonSerializer.Serialize(engine.State));
        Assert.Empty(result.Events);
    }

    private static void EnterMain(DuelEngine engine)
    {
        var player = engine.State.ActivePlayer;
        Ok(engine, new ResolveDrawStep(player));
        Ok(engine, new ChangePhase(player, DuelPhase.Main1));
    }

    private static PendingDecision Begin(DuelEngine engine)
    {
        var result = Ok(engine, new BeginDecisionSequence(engine.State.ActivePlayer, "select"));
        var request = Assert.IsType<DecisionRequested>(Assert.Single(result.Events));
        Assert.Same(engine.State.PendingDecision, request.Decision);
        Assert.Equal(engine.State.Revision, request.Decision.DecisionId);
        return request.Decision;
    }

    private static DuelActionResult Complete(DuelEngine engine, int zone = 0)
    {
        var p = engine.State.PendingDecision!;
        Ok(engine, new ChooseDecisionCard(p.Player, p.DecisionId, p.CardOptions[0].InstanceId));
        p = engine.State.PendingDecision!;
        Ok(engine, new ChooseDecisionPosition(p.Player, p.DecisionId, BattlePosition.FaceUpAttack));
        p = engine.State.PendingDecision!;
        return Ok(engine, new ChooseDecisionZone(p.Player, p.DecisionId, zone));
    }

    [Theory]
    [InlineData(PlayerId.One)]
    [InlineData(PlayerId.Two)]
    public void CardPositionAndZonePauseAdvanceAndThenResumeWithoutMovingCards(PlayerId starter)
    {
        var engine = Engine([Flow()], starter);
        EnterMain(engine);
        var before = engine.State;
        var first = Begin(engine);
        Assert.Equal(DecisionKind.Card, first.Kind);
        Assert.Equal(DecisionCardSource.Deck, first.CardSource);
        Assert.All(first.CardOptions, c => Assert.Equal(starter, c.Owner));
        var result = Complete(engine, 2);
        Assert.Null(engine.State.PendingDecision);
        Assert.False(engine.State.ResearchSliceComplete);
        var completed = engine.State.DecisionResult!;
        Assert.Equal([DecisionKind.Card, DecisionKind.Position, DecisionKind.MonsterZone], completed.Answers.Select(a => a.Kind));
        Assert.Equal(first.CardOptions[0].InstanceId, completed.Answers[0].CardInstanceId);
        Assert.Equal(BattlePosition.FaceUpAttack, completed.Answers[1].Position);
        Assert.Equal(2, completed.Answers[2].MonsterZone);
        Assert.Collection(result.Events, e => Assert.IsType<DecisionAnswered>(e),
            e => Assert.Same(completed, Assert.IsType<DecisionSequenceCompleted>(e).Result));
        Assert.All(before.Players, p => Assert.Same(p, engine.State.GetPlayer(p.Id)));
        Assert.Equal(before.NormalSummonUsed, engine.State.NormalSummonUsed);
        Assert.Empty(first.Answers); // Earlier request/event snapshot has not changed.
        Ok(engine, new ChangePhase(starter, DuelPhase.End));
    }

    [Theory]
    [InlineData(DecisionCardSource.Hand)]
    [InlineData(DecisionCardSource.Deck)]
    public void SelectionUsesInstancesFromOnlyTheConfiguredSource(DecisionCardSource source)
    {
        var engine = Engine([Flow(source: source)]);
        EnterMain(engine);
        var p = Begin(engine);
        var player = engine.State.GetPlayer(PlayerId.One);
        var expected = source == DecisionCardSource.Deck ? player.Deck : player.Hand;
        var other = source == DecisionCardSource.Deck ? player.Hand : player.Deck;
        Assert.Equal(expected.Where(c => c.GameCardId == 1), p.CardOptions);
        Reject(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId,
            other.First(c => c.GameCardId == 1).InstanceId), DuelError.InvalidDecisionChoice);
    }

    [Fact]
    public void PendingChoicesRejectUnrelatedWrongActorAndInvalidAnswersAtomically()
    {
        var engine = Engine([Flow()]);
        EnterMain(engine);
        var p = Begin(engine);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.DecisionPending);
        Reject(engine, new NormalSummon(PlayerId.One, 1, 0), DuelError.DecisionPending);
        Reject(engine, new DeclareAttack(PlayerId.One, 0), DuelError.DecisionPending);
        Reject(engine, new EndTurn(PlayerId.One), DuelError.DecisionPending);
        Reject(engine, new BeginDecisionSequence(PlayerId.One, "select"), DuelError.DecisionPending);
        Reject(engine, new ChooseDecisionCard(PlayerId.Two, p.DecisionId, p.CardOptions[0].InstanceId), DuelError.NotActivePlayer);
        Reject(engine, new ChooseDecisionCard((PlayerId)9, p.DecisionId, 1), DuelError.InvalidPlayer);
        Reject(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId + 1, p.CardOptions[0].InstanceId), DuelError.StaleDecision);
        Reject(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId, int.MaxValue), DuelError.InvalidDecisionChoice);
        Reject(engine, new ChooseDecisionPosition(PlayerId.One, p.DecisionId, BattlePosition.FaceUpAttack), DuelError.WrongDecisionKind);
        Ok(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId, p.CardOptions[0].InstanceId));
        Reject(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId, p.CardOptions[0].InstanceId), DuelError.StaleDecision);
        p = engine.State.PendingDecision!;
        Reject(engine, new ChooseDecisionPosition(PlayerId.One, p.DecisionId, BattlePosition.FaceUpDefense), DuelError.InvalidDecisionChoice);
        Reject(engine, new ChooseDecisionPosition(PlayerId.One, p.DecisionId, (BattlePosition)99), DuelError.InvalidDecisionChoice);
        Ok(engine, new ChooseDecisionPosition(PlayerId.One, p.DecisionId, BattlePosition.FaceUpAttack));
        p = engine.State.PendingDecision!;
        Reject(engine, new ChooseDecisionZone(PlayerId.One, p.DecisionId, -1), DuelError.InvalidDecisionChoice);
        Reject(engine, new ChooseDecisionZone(PlayerId.One, p.DecisionId, 5), DuelError.InvalidDecisionChoice);
    }

    [Fact]
    public void DefaultEngineAndUnavailableOrWrongPhaseSequencesCannotBegin()
    {
        var engine = Engine();
        Reject(engine, new BeginDecisionSequence(PlayerId.One, "select"), DuelError.UnsupportedDecisionSequence);
        Reject(engine, new ChooseDecisionCard(PlayerId.One, 1, 1), DuelError.NoPendingDecision);
        engine = Engine([Flow()]);
        Reject(engine, new BeginDecisionSequence(PlayerId.One, "select"), DuelError.WrongPhase);
        EnterMain(engine);
        Reject(engine, new BeginDecisionSequence(PlayerId.One, "missing"), DuelError.UnsupportedDecisionSequence);
        engine = Engine([new("missing-card", [DecisionStep.Card(DecisionCardSource.Deck, [2])])]);
        EnterMain(engine);
        Reject(engine, new BeginDecisionSequence(PlayerId.One, "missing-card"), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void EmptyZoneOptionsExcludeOccupiedSlotsAndPreserveSummonAllowance()
    {
        var engine = Engine([Flow()]);
        EnterMain(engine);
        Ok(engine, new NormalSummon(PlayerId.One, 1, 2));
        Begin(engine);
        var p = engine.State.PendingDecision!;
        Ok(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId, p.CardOptions[0].InstanceId));
        p = engine.State.PendingDecision!;
        Ok(engine, new ChooseDecisionPosition(PlayerId.One, p.DecisionId, BattlePosition.FaceUpAttack));
        p = engine.State.PendingDecision!;
        Assert.Equal([0, 1, 3, 4], p.MonsterZoneOptions);
        Reject(engine, new ChooseDecisionZone(PlayerId.One, p.DecisionId, 2), DuelError.InvalidDecisionChoice);
        Ok(engine, new ChooseDecisionZone(PlayerId.One, p.DecisionId, 1));
        Assert.True(engine.State.NormalSummonUsed);
        Assert.NotNull(engine.State.GetPlayer(PlayerId.One).MonsterZones[2]);
        Assert.Null(engine.State.GetPlayer(PlayerId.One).MonsterZones[1]); // Selecting a zone is not summoning.
    }

    [Fact]
    public void PreflightRejectsAFullFieldBeforeOpeningAnyChoice()
    {
        var engine = Engine([Flow()]);
        EnterMain(engine);
        for (int zone = 0; zone < 5; zone++)
        {
            Ok(engine, new NormalSummon(PlayerId.One, engine.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, zone));
            if (zone == 4) break;
            for (int turn = 0; turn < 2; turn++)
            {
                var actor = engine.State.ActivePlayer;
                Ok(engine, new ChangePhase(actor, DuelPhase.End));
                while (engine.State.GetPlayer(actor).Hand.Count > 6)
                    Ok(engine, new DiscardForHandLimit(actor, engine.State.GetPlayer(actor).Hand[^1].InstanceId));
                Ok(engine, new EndTurn(actor));
                EnterMain(engine);
            }
        }
        Reject(engine, new BeginDecisionSequence(PlayerId.One, "select"), DuelError.DecisionUnavailable);
        Assert.Null(engine.State.PendingDecision);
    }

    [Fact]
    public void ResearchCompletionFreezesWithoutClaimingDefeatOrExecutingEffects()
    {
        var engine = Engine([Flow(stop: true)]);
        EnterMain(engine);
        var before = engine.State;
        Begin(engine);
        Complete(engine);
        Assert.True(engine.State.ResearchSliceComplete);
        Assert.Null(engine.State.Outcome);
        Assert.All(before.Players, p => Assert.Same(p, engine.State.GetPlayer(p.Id)));
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.ResearchSliceComplete);
        Reject(engine, new BeginDecisionSequence(PlayerId.One, "select"), DuelError.ResearchSliceComplete);
    }

    [Fact]
    public void OldDecisionCannotAnswerANewSequenceAndRejectionsDoNotConsumeIds()
    {
        var engine = Engine([Flow()]);
        EnterMain(engine);
        Reject(engine, new BeginDecisionSequence(PlayerId.One, "missing"), DuelError.UnsupportedDecisionSequence);
        long nextId = engine.State.Revision + 1;
        var first = Begin(engine);
        Assert.Equal(nextId, first.DecisionId);
        Complete(engine);
        var completed = engine.State;
        Reject(engine, new ChooseDecisionZone(PlayerId.One, completed.DecisionResult!.FinalDecisionId, 0), DuelError.NoPendingDecision);
        var second = Begin(engine);
        Assert.Null(engine.State.DecisionResult);
        Assert.NotNull(completed.DecisionResult);
        Assert.True(second.DecisionId > first.DecisionId);
        Reject(engine, new ChooseDecisionCard(PlayerId.One, first.DecisionId, first.CardOptions[0].InstanceId), DuelError.StaleDecision);
    }

    [Fact]
    public void DefinitionsRequestsAndResultsDoNotExposeMutableLists()
    {
        uint[] ids = [1];
        BattlePosition[] positions = [BattlePosition.FaceUpAttack];
        DecisionStep[] steps = [DecisionStep.Card(DecisionCardSource.Deck, ids), DecisionStep.Position(positions), DecisionStep.EmptyMonsterZone()];
        var flow = new DecisionSequence("select", steps, false);
        var engine = Engine([flow]);
        ids[0] = 2;
        positions[0] = BattlePosition.FaceUpDefense;
        steps[0] = DecisionStep.EmptyMonsterZone();
        EnterMain(engine);
        var request = Begin(engine);
        Assert.All(request.CardOptions, c => Assert.Equal(1u, c.GameCardId));
        Assert.Throws<NotSupportedException>(() => ((IList<CardInstance>)request.CardOptions).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<DecisionStep>)flow.Steps).Clear());
        Complete(engine);
        Assert.Throws<NotSupportedException>(() => ((IList<DecisionAnswer>)engine.State.DecisionResult!.Answers).Clear());
        Assert.Empty(request.Answers);
    }

    [Fact]
    public void SameCommandsReplayDecisionIdsStatesAndEventsDeterministically()
    {
        var a = Engine([Flow()]);
        var b = Engine([Flow()]);
        EnterMain(a); EnterMain(b);
        Begin(a); Begin(b);
        Assert.Equal(JsonSerializer.Serialize(a.State), JsonSerializer.Serialize(b.State));
        var first = Complete(a);
        var second = Complete(b);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public void InvalidDefinitionsFailBeforeAnEngineCanUseThem()
    {
        Assert.Throws<ArgumentException>(() => DecisionStep.Card((DecisionCardSource)9, [1]));
        Assert.Throws<ArgumentException>(() => DecisionStep.Card(DecisionCardSource.Deck, [1, 1]));
        Assert.Throws<ArgumentException>(() => DecisionStep.Position([(BattlePosition)9]));
        Assert.Throws<ArgumentException>(() => new DecisionSequence("empty", []));
        Assert.Throws<ArgumentException>(() => Engine([new("unknown", [DecisionStep.Card(DecisionCardSource.Deck, [99])])]));
        Assert.Throws<ArgumentException>(() => Engine([Flow(), Flow()]));
    }
}
