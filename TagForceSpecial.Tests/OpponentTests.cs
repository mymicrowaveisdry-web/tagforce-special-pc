using System.Text.Json;
using TagForceSpecial.AI;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Game;
using TagForceSpecial.PC.Import;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

public sealed class OpponentTests
{
    [Fact]
    public void DiscoveryIsImmutableBoundedAndOffersOnlyExecutableCommands()
    {
        var e = Preview(); EnterMain(e);
        var source = new DuelActionSource(e, Catalog());
        var before = e.State;
        var view = source.Observe(PlayerId.One);
        Assert.Same(view, source.Observe(PlayerId.One));
        Assert.Same(before, e.State);
        Assert.Equal(ActionAvailability.Ready, view.Availability);
        Assert.InRange(view.Actions.Count, 1, DuelActionSource.MaximumCandidates);
        Assert.Equal(view.Actions.Count, view.Actions.Select(a => a.Key).Distinct().Count());
        Assert.Throws<NotSupportedException>(() => ((IList<LegalAction>)view.Actions).Clear());
        Assert.Empty(source.Observe(PlayerId.Two).Actions);
        foreach (var action in view.Actions)
        {
            var copy = Preview(); EnterMain(copy);
            var adapter = new DuelActionSource(copy, Catalog());
            Assert.True(adapter.Execute(PlayerId.One, view.Revision, action.Token).Accepted);
        }
    }

    [Fact]
    public void OpposingHandDeckOrderAndFaceDownIdentityCannotChangePolicyInput()
    {
        string Capture(uint hidden)
        {
            var e = new DuelEngine(Catalog(), PlayablePreview.Rules, Enumerable.Repeat(hidden, 40), Enumerable.Repeat(4032u, 40));
            EnterMain(e);
            Ok(e, new SetMonster(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0));
            Next(e);
            Ok(e, new NormalSummon(PlayerId.Two, e.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 0));
            Ok(e, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
            var view = new DuelActionSource(e, Catalog()).Observe(PlayerId.Two);
            Assert.All(view.Opponent.Hand, c => { Assert.Null(c.InstanceId); Assert.Null(c.CardId); Assert.Empty(c.Description); });
            Assert.Null(view.Opponent.Monsters[0]!.Attack);
            Assert.Null(view.Opponent.Monsters[0]!.CardId);
            var policy = new DeterministicPolicy(17);
            return JsonSerializer.Serialize(new { view, choice = policy.Choose(view) });
        }
        Assert.Equal(Capture(4012), Capture(4030));
        Assert.Equal(Capture(4012), Capture(6960));
    }

    [Fact]
    public void StaleWrongOwnerAndInvalidHandlesRejectAtomicallyAndFreshChoicesRecover()
    {
        var e = Preview(); var source = new DuelActionSource(e, Catalog());
        var initial = source.Observe(PlayerId.One);
        var before = e.State;
        Assert.False(source.Execute(PlayerId.One, 0, 999).Accepted);
        Assert.False(source.Execute(PlayerId.Two, 0, 0).Accepted);
        Assert.False(source.Execute((PlayerId)99, 0, 0).Accepted);
        Assert.Same(before, e.State);
        Assert.True(source.Execute(PlayerId.One, initial.Revision, 0).Accepted);
        before = e.State;
        Assert.Equal(DuelError.StaleDecision, source.Execute(PlayerId.One, initial.Revision, 0).Error);
        Assert.Same(before, e.State);
        Assert.True(source.Execute(PlayerId.One, source.Observe(PlayerId.One).Revision, 0).Accepted);
    }

    [Fact]
    public void DecisionAdapterAuthorizesCardPositionAndZoneOptionsAtEachRevision()
    {
        var deck = Enumerable.Repeat(4012u, 5).Concat(Enumerable.Repeat(4030u, 35));
        var e = new DuelEngine(Catalog(), PlayablePreview.Rules, deck, deck,
            decisionSequences: [new("select", [DecisionStep.Card(DecisionCardSource.Deck, [4030]),
                DecisionStep.Position([BattlePosition.FaceUpAttack, BattlePosition.FaceUpDefense]), DecisionStep.EmptyMonsterZone()], false)]);
        EnterMain(e); Ok(e, new BeginDecisionSequence(PlayerId.One, "select"));
        var source = new DuelActionSource(e, Catalog());
        var policy = new DeterministicPolicy(9);
        foreach (var expected in new[] { ActionKind.ChooseCard, ActionKind.ChoosePosition, ActionKind.ChooseZone })
        {
            var v = source.Observe(PlayerId.One);
            Assert.Null(source.Observe(PlayerId.Two).Decision);
            Assert.All(v.Actions, a => Assert.Equal(expected, a.Kind));
            var c = policy.Choose(v)!;
            Assert.True(source.Execute(v.Player, c.Revision, c.Token).Accepted);
            var before = e.State;
            Assert.False(source.Execute(v.Player, c.Revision, c.Token).Accepted);
            Assert.Same(before, e.State);
        }
        Assert.Null(e.State.PendingDecision);
        Assert.False(e.State.ResearchSliceComplete);
    }

    [Fact]
    public void CostAndTargetBindingsAreCurrentWithoutExposingOpponentSetIdentity()
    {
        var deck = new uint[] { 4350, 4012, 4012, 4030, 4032 }.Concat(Enumerable.Repeat(4012u, 35));
        var e = new DuelEngine(Catalog(), PlayablePreview.Rules, deck, Enumerable.Repeat(4352u, 40), startingPlayer: PlayerId.Two,
            effectDefinitions: [new("cost-target", 4350, EffectKind.NormalSpell, [new DestroyTargetStep()],
                cost: new DiscardCost(), target: new SpellTrapOnFieldTarget())]);
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.Two, e.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 2));
        Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0));
        var source = new DuelActionSource(e, Catalog());
        var cost = source.Observe(PlayerId.One);
        Assert.All(cost.Actions, a => Assert.Equal(ActionKind.ChooseCard, a.Kind));
        Assert.True(source.Execute(PlayerId.One, cost.Revision, cost.Actions[0].Token).Accepted);
        var target = source.Observe(PlayerId.One);
        Assert.All(target.Actions, a => { Assert.Equal(ActionKind.ChooseTarget, a.Kind); Assert.Null(a.CardInstanceId); });
        Assert.Null(target.Opponent.SpellTraps[2]!.CardId);
        Assert.True(source.Execute(PlayerId.One, target.Revision, target.Actions[0].Token).Accepted);
        Assert.NotNull(e.State.CommittedActivation);
        Assert.Equal(ActionAvailability.Waiting, source.Observe(PlayerId.One).Availability); // the opponent may respond
        Assert.Equal("Chain", source.Observe(PlayerId.Two).Decision!.Kind);
    }

    [Theory]
    [InlineData(0u)] [InlineData(1u)] [InlineData(7u)] [InlineData(42u)] [InlineData(uint.MaxValue)]
    public void SeedsRepeatLegalStagedDuelsAndReplayToTheSameEngineOutcome(uint seed)
    {
        var first = Play(seed); var repeat = Play(seed);
        Assert.Equal(first.CommandDigest(), repeat.CommandDigest());
        Assert.Equal(first.Outcome, repeat.Outcome);
        Assert.Contains(first.Steps, s => s.Kind == ActionKind.BattlePass);
        Assert.Contains(first.Steps, s => s.Kind == ActionKind.AdvanceBattle);
        var engine = PlayablePreview.CreateEngine(Catalog(), first.FirstDeck, first.SecondDeck, stagedBattle: true);
        var source = new DuelActionSource(engine, Catalog());
        foreach (var entry in first.Steps) Assert.True(ReplayCommands.Apply(source, entry).Accepted);
        Assert.Equal(first.Outcome, engine.State.Outcome);
        Assert.Empty(source.Observe(PlayerId.One).Actions);
        Assert.False(source.Execute(PlayerId.One, engine.State.Revision, 0).Accepted);
    }

    [Fact]
    public void SeededTieBreakingVariesEquivalentChoicesWithoutStatefulRng()
    {
        var engine = Preview(); EnterMain(engine);
        var v = new DuelActionSource(engine, Catalog()).Observe(PlayerId.One);
        var keys = Enumerable.Range(0, 32).Select(seed => new DeterministicPolicy((uint)seed).Choose(v)!.Key).Distinct().Count();
        Assert.True(keys > 1);
        var p = new DeterministicPolicy(0);
        Assert.Equal(p.Choose(v), p.Choose(v));
    }

    [Fact]
    public void PolicyPrefersStrongOwnMonster()
    {
        var engine = Preview(2); EnterMain(engine);
        var source = new DuelActionSource(engine, Catalog()); var view = source.Observe(PlayerId.One);
        var policy = new DeterministicPolicy(0); var choice = policy.Choose(view)!;
        var card = view.Self.Hand.Single(c => c.InstanceId == view.Actions[choice.Token].CardInstanceId);
        Assert.Equal(view.Self.Hand.Max(c => c.Attack), card.Attack);
        Assert.Equal(ActionKind.Summon, choice.Kind);
    }

    [Fact]
    public void PolicyDeclinesAVisibleLosingAttackButDoesNotPeekAtASetDefender()
    {
        foreach (bool hidden in new[] { false, true })
        {
            var e = new DuelEngine(Catalog(), PlayablePreview.Rules, Enumerable.Repeat(4030u, 40), Enumerable.Repeat(6960u, 40));
            EnterMain(e); Ok(e, new NormalSummon(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0));
            Next(e);
            int card = e.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId;
            Ok(e, hidden ? new SetMonster(PlayerId.Two, card, 0) : new NormalSummon(PlayerId.Two, card, 0));
            Next(e); Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Battle));
            var view = new DuelActionSource(e, Catalog()).Observe(PlayerId.One);
            var choice = new DeterministicPolicy(7).Choose(view)!;
            Assert.Equal(hidden ? ActionKind.Attack : ActionKind.Phase, choice.Kind);
        }
    }

    [Fact]
    public void PauseCancellationAndStepBudgetCannotAdvanceOrFakeADuelOutcome()
    {
        var engine = Preview(); var source = new DuelActionSource(engine, Catalog());
        var driver = new OpponentDriver(PlayerId.One, 0, stepBudget: 1) { Paused = true };
        var before = engine.State;
        Assert.Equal(OpponentStatus.Paused, driver.Step(source).Status);
        Assert.Same(before, engine.State);
        driver.Paused = false;
        Assert.Equal(OpponentStatus.Paused, driver.Step(source, new CancellationToken(true)).Status);
        Assert.Same(before, engine.State);
        Assert.Equal(OpponentStatus.Advanced, driver.Step(source).Status);
        before = engine.State;
        Assert.Equal(OpponentStatus.BudgetExceeded, driver.Step(source).Status);
        Assert.Same(before, engine.State);
        Assert.Null(engine.State.Outcome);
    }

    [Fact]
    public void UnsupportedBoundaryAndUnknownCardAreNeverSuccessfulNoOps()
    {
        var engine = new DuelEngine(Catalog(), PlayablePreview.Rules, Enumerable.Repeat(90001u, 40), Enumerable.Repeat(4012u, 40),
            decisionSequences: [new("freeze", [DecisionStep.Position([BattlePosition.FaceUpAttack])])]);
        EnterMain(engine);
        var source = new DuelActionSource(engine, Catalog());
        Assert.DoesNotContain(source.Observe(PlayerId.One).Actions, a => a.Kind is ActionKind.Summon or ActionKind.ActivateSpell);
        Ok(engine, new BeginDecisionSequence(PlayerId.One, "freeze"));
        var v = source.Observe(PlayerId.One);
        Assert.True(source.Execute(PlayerId.One, v.Revision, 0).Accepted);
        var before = engine.State;
        Assert.Equal(OpponentStatus.Unsupported, new OpponentDriver(PlayerId.One, 0).Step(source).Status);
        Assert.Same(before, engine.State); Assert.Null(engine.State.Outcome);
        Assert.Throws<ArgumentException>(() => PlayablePreview.CreateEngine(Catalog(), Enumerable.Repeat(90001u, 40), PlayablePreview.ExampleDeck));
    }

    [Fact]
    public void ReplayTamperingAndReapplyingACommandAreRejectedWithoutMutation()
    {
        var replay = Play(7); var engine = Preview(); var source = new DuelActionSource(engine, Catalog());
        var entry = replay.Steps[0]; var before = engine.State;
        Assert.False(ReplayCommands.Apply(source, entry with { ActionKey = "invented" }).Accepted);
        Assert.Same(before, engine.State);
        Assert.True(ReplayCommands.Apply(source, entry).Accepted);
        before = engine.State;
        Assert.False(ReplayCommands.Apply(source, entry).Accepted);
        Assert.Same(before, engine.State);
    }

    [Theory]
    [InlineData(0, PlayerId.Two, DuelEndReason.LifePointsDepleted)] [InlineData(2, PlayerId.One, DuelEndReason.UnableToDraw)]
    public void HumanControllerPathsReachRealLossAndWinAgainstTheSeededOpponent(int variant, PlayerId expected, DuelEndReason reason)
    {
        var c = new DuelController(Preview(variant), Catalog(), seed: 7);
        for (int i = 0; i < 6000 && c.View.Outcome is null; i++)
            Assert.True(c.View.Choices.Count > 0 ? c.Choose(c.View.Revision, 0) : c.StepOpponent());
        Assert.Equal(expected, c.View.Outcome?.Winner);
        Assert.Equal(reason, c.View.Outcome!.Reason);
        Assert.Equal(c.View.Outcome, c.ExportReplay(InitialOpponentDecks.ManifestId).Outcome);
    }

    [Fact]
    public void NormalSpellResponsesAndResolutionFollowCurrentOwnersAndStopAfterCleanup()
    {
        var e = Preview(1); EnterMain(e);
        var source = new DuelActionSource(e, Catalog()); var policy = new DeterministicPolicy(7);
        var opening = source.Observe(PlayerId.One); var activation = policy.Choose(opening)!;
        Assert.Equal(ActionKind.ActivateSpell, activation.Kind);
        Assert.True(source.Execute(PlayerId.One, activation.Revision, activation.Token).Accepted);
        var kinds = new List<ActionKind>();
        for (int i = 0; i < 8 && e.State.PendingChain is not null; i++)
        {
            var actor = source.Observe(PlayerId.One).Actor;
            var view = source.Observe(actor); var choice = policy.Choose(view)!;
            kinds.Add(choice.Kind);
            Assert.NotNull(view.Decision);
            Assert.True(source.Execute(actor, choice.Revision, choice.Token).Accepted);
            var before = e.State;
            Assert.False(source.Execute(actor, choice.Revision, choice.Token).Accepted);
            Assert.Same(before, e.State);
        }
        Assert.Null(e.State.PendingChain);
        Assert.Equal(2, kinds.Count(k => k == ActionKind.ChainPass));
        Assert.Contains(ActionKind.ResolveChain, kinds);
    }

    [Fact]
    public void ReplayAdapterSelectsACurrentTargetAfterALegalDefenderChange()
    {
        var profile = ResponseProbe.SummonFromHand("synthetic-hand-summon", BattleFlowScenario.Summon, BattleTiming.AttackDeclaration, BattleFlowScenario.B);
        var e = BattleFlowScenario.Engine(defender: false, profiles: [profile]);
        BattleFlowScenario.Ready(e); BattleFlowScenario.Declare(e, null);
        int card = e.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == BattleFlowScenario.B).InstanceId;
        BattleFlowScenario.Respond(e, 2, card, 1); BattleFlowScenario.DrainEffects(e); BattleFlowScenario.Close(e);
        uint[] ids = [BattleFlowScenario.A, BattleFlowScenario.B, BattleFlowScenario.Filler, BattleFlowScenario.Negate,
            BattleFlowScenario.Destroy, BattleFlowScenario.Summon, BattleFlowScenario.Stats, BattleFlowScenario.Counter];
        var catalog = new CardCatalog(new(null, "synthetic-replay-ui-v1"), ids.Select(id => new CardDefinition(id, $"Synthetic {id}", "", new(1000, 1000, 2))));
        var source = new DuelActionSource(e, catalog); var view = source.Observe(PlayerId.One);
        Assert.Equal("AttackReplay", view.Decision!.Kind);
        Assert.Contains(view.Actions, a => a.Kind == ActionKind.CancelReplay);
        var choice = new DeterministicPolicy(7).Choose(view)!;
        Assert.Equal(ActionKind.ReplayAttack, choice.Kind);
        Assert.True(source.Execute(PlayerId.One, choice.Revision, choice.Token).Accepted);
        Assert.False(e.State.Battle!.Attack!.ReplayRequired);
        Assert.Equal(1, e.State.Battle.Attack.Defender!.Zone);
    }

    [Fact]
    public void ManifestVariantsRemainFortySupportedCardsWithTheSameCopyEnvelope()
    {
        foreach (int variant in Enumerable.Range(0, InitialOpponentDecks.Names.Count))
        {
            var deck = InitialOpponentDecks.Create(variant);
            Assert.Equal(40, deck.Main.Count);
            Assert.All(deck.Main, id => Assert.Contains(id, PlayablePreview.Rules.Keys));
            Assert.All(deck.Main.GroupBy(id => id), group => Assert.InRange(group.Count(), 1, 3));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => InitialOpponentDecks.Create(3));
        Assert.Throws<ArgumentException>(() => HostOptions.Parse(["--seed", "-1"]));
        Assert.Equal(uint.MaxValue, HostOptions.Parse(["--seed", "4294967295"]).Seed);
    }

    [Fact]
    public void ControllerPauseResumesTheSameRevisionAndDoesNotExposePrivateActionLabels()
    {
        var c = new DuelController(Preview(), Catalog(), seed: 7);
        c.Paused = true; long revision = c.View.Revision;
        Assert.False(c.Choose(revision, 0)); Assert.False(c.StepOpponent()); Assert.Equal(revision, c.View.Revision);
        c.Paused = false; Assert.True(c.Choose(revision, 0));
        for (int i = 0; i < 400 && c.LastOpponentStatus != OpponentStatus.Advanced; i++)
            Assert.True(c.View.Choices.Count > 0 ? c.Choose(c.View.Revision, 0) : c.StepOpponent());
        Assert.Equal(OpponentStatus.Advanced, c.LastOpponentStatus);
        Assert.StartsWith("Opponent: ", c.Notice);
        Assert.DoesNotContain("Feral Imp", c.Notice);
    }

    [Fact]
    public void LiveSessionCannotExportEitherPlayersHiddenDeckOrder()
    {
        var session = new DuelController(Preview(), Catalog());
        long revision = session.View.Revision;
        Assert.Throws<InvalidOperationException>(() => session.ExportReplay(InitialOpponentDecks.ManifestId));
        Assert.Equal(revision, session.View.Revision);
    }

    [Fact]
    [Trait("Category", "LocalReference")]
    public void ActualValidatedCacheDeckSelectionRunsTheSameSupportedPcSession()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var cache = GameCache.Validate(Path.Combine(root, "analysis/stage13/cache/english-2.1-v1"));
        Assert.True(cache.IsValid);
        var c = SupportedDuelFactory.Create(cache.Cache!, 2, 7);
        for (int i = 0; i < 6000 && c.View.Outcome is null; i++)
            Assert.True(c.View.Choices.Count > 0 ? c.Choose(c.View.Revision, 0) : c.StepOpponent());
        Assert.Equal(PlayerId.One, c.View.Outcome?.Winner);
        Assert.All(c.View.Opponent.Hand, card => Assert.Null(card.CardId));
    }

    private static DuelEngine Preview(int variant = 0) => PlayablePreview.CreateEngine(Catalog(),
        InitialOpponentDecks.Create(variant).Main, InitialOpponentDecks.Create(0).Main, stagedBattle: true);

    private static DuelReplay Play(uint seed)
    {
        var e = Preview(); var source = new DuelActionSource(e, Catalog());
        var policy = new DeterministicPolicy(seed); var entries = new List<ReplayEntry>();
        for (int i = 0; i < 6000 && e.State.Outcome is null; i++)
        {
            var actor = source.Observe(PlayerId.One).Actor;
            var view = source.Observe(actor); var before = e.State;
            var choice = policy.Choose(view);
            Assert.Same(before, e.State); Assert.NotNull(choice);
            var result = source.Execute(actor, choice!.Revision, choice.Token);
            Assert.True(result.Accepted, $"{view.Revision}: {view.Diagnostic}");
            entries.Add(new(view.Revision, result.Revision, actor, choice.Key, choice.Kind, view.Decision?.Id));
        }
        Assert.NotNull(e.State.Outcome);
        return new(DeterministicPolicy.Version, seed, InitialOpponentDecks.ManifestId,
            InitialOpponentDecks.Create(0).Main, InitialOpponentDecks.Create(0).Main, true,
            PlayerProjection.Read(entries), e.State.Outcome);
    }
}
