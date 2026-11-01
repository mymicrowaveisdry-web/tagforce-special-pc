using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Synthetic foundation scenarios reached through legal commands; no PSP summon-procedure claim.</summary>
public sealed class SpecialSummonFoundationTests
{
    private const uint Target = uint.MaxValue, Alternative = uint.MaxValue - 1, Low = 1, Spell = 10, Negate = 20, Counter = 21;

    private static DuelEngine Engine(bool requiredProper = true, bool window = false,
        IEnumerable<SpecialSummonProcedureProfile>? procedures = null,
        IEnumerable<SpecialSummonCardProfile>? cards = null,
        IEnumerable<TriggerEffectProfile>? triggers = null, SummonNegationKinds negateKinds = SummonNegationKinds.Special,
        bool onlyTargetInEffect = false, bool banishAtEndPhase = false, IEnumerable<ChainResponseProfile>? responses = null)
    {
        uint[] ids = [Target, Alternative, Low, Spell, Negate, Counter];
        var catalog = new CardCatalog(new(null, "synthetic-special-foundation"), ids.Select(id =>
            new CardDefinition(id, $"Probe {id}", "", new(id == Low ? 3000 : 1000, 1000, id == Target ? 8u : 4u))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(
            id == Spell ? CardCategory.Spell : id is Negate or Counter ? CardCategory.Trap : CardCategory.Monster,
            AllowsNormalSummon: id is Low or Alternative, AllowsBasicBattle: id is Low or Alternative or Target,
            AllowsSpellTrapSet: id is Spell or Negate or Counter));
        uint[] first = [Target, Target, Spell, Low, Counter, Target, Alternative, .. Enumerable.Repeat(Low, 33)];
        uint[] second = [Low, Low, Negate, Counter, Spell, .. Enumerable.Repeat(Low, 35)];
        return new(catalog, rules, first, second,
            experimentalSpecialSummonCards: cards ?? [new(Target, SpecialSummonSources.All, requiredProper ? "proper" : null),
                new(Alternative, SpecialSummonSources.All)],
            experimentalSpecialSummonProcedures: procedures ?? [new("proper", [Target]), new("revive", [Target], DecisionCardSource.Graveyard),
                new("banished", [Target], DecisionCardSource.Banished)],
            experimentalSummonWindows: window ? [new(Target)] : null,
            experimentalSummonNegations: [new("negate", Negate, 500, negateKinds)],
            experimentalSpellEffects: [new("deck", Spell, onlyTargetInEffect ? [Target] : [Target, Alternative], banishAtEndPhase)],
            experimentalTriggerEffects: triggers, experimentalChainResponses: responses);
    }

    private static int Hand(DuelEngine e, uint id, PlayerId? player = null) =>
        e.State.GetPlayer(player ?? e.State.ActivePlayer).Hand.First(c => c.GameCardId == id).InstanceId;
    private static SpecialSummonByProcedure Command(DuelEngine e, string procedure = "proper", int? card = null,
        int zone = 0, BattlePosition position = BattlePosition.FaceUpAttack) =>
        new(PlayerId.One, e.State.Revision, procedure, card ?? Hand(e, Target, PlayerId.One), zone, position);
    private static SummonRecord Record(DuelEngine e, int instance) => Assert.Single(e.State.SummonHistory, r => r.Card.InstanceId == instance);
    private static void PassSummon(DuelEngine e)
    {
        var p = e.State.PendingSummon!;
        Ok(e, new PassSummonResponse(p.Responder, p.DecisionId));
    }
    private static void SetNegator(DuelEngine e)
    {
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Negate), 0));
    }
    private static void DestroyTargetInBattle(DuelEngine e)
    {
        NextMain(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, Low), 0));
        Ok(e, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        NextMain(e);
    }

    [Theory]
    [InlineData(BattlePosition.FaceUpAttack)] [InlineData(BattlePosition.FaceUpDefense)]
    public void ExplicitProcedureUsesBothFaceUpPositionsAndDoesNotSpendNormalAllowance(BattlePosition position)
    {
        var e = Engine(); EnterMain(e); int instance = Hand(e, Target);
        var before = e.State; string saved = JsonSerializer.Serialize(before);
        var result = Ok(e, Command(e, card: instance, position: position));
        var monster = e.State.GetPlayer(PlayerId.One).MonsterZones[0]!;
        Assert.Equal(instance, monster.Card.InstanceId); Assert.Equal(position, monster.Position);
        Assert.Equal(e.State.Revision, monster.PlacementId); Assert.Equal(e.State.TurnNumber, monster.SummonedOnTurn);
        Assert.False(e.State.NormalSummonUsed);
        var record = Record(e, instance);
        Assert.Equal(SummonMethod.SpecialProcedure, record.Method);
        Assert.Equal(SummonStatus.Completed, record.Status);
        Assert.Equal(DecisionCardSource.Hand, record.Source); Assert.Equal("proper", record.ProperProcedureId);
        Assert.Single(result.Events.OfType<MonsterSpecialSummoned>());
        Assert.DoesNotContain(result.Events, x => x is MonsterNormalSummoned);
        Assert.Equal(saved, JsonSerializer.Serialize(before));
        Assert.Throws<NotSupportedException>(() => ((IList<SummonRecord>)e.State.SummonHistory).Clear());
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, Low), 1));
        Assert.Equal(SummonMethod.Normal, Record(e, e.State.GetPlayer(PlayerId.One).MonsterZones[1]!.Card.InstanceId).Method);
    }

    [Fact]
    public void MultipleSpecialSummonsPreserveDistinctInstancesAndNormalAllowanceAlreadySpent()
    {
        var e = Engine(); EnterMain(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, Low), 4));
        int first = Hand(e, Target); Ok(e, Command(e, card: first));
        int second = Hand(e, Target); Ok(e, Command(e, card: second, zone: 1));
        Assert.True(e.State.NormalSummonUsed);
        Assert.NotEqual(first, second);
        Assert.NotEqual(Record(e, first).PlacementId, Record(e, second).PlacementId);
        Assert.Equal(3, e.State.SummonHistory.Count);
    }

    [Fact]
    public void InvalidCommandsRejectBeforeHistoryOrResourceChanges()
    {
        var e = Engine(); EnterMain(e);
        var c = Command(e);
        Reject(e, c with { Player = PlayerId.Two }, DuelError.NotActivePlayer);
        Reject(e, c with { ExpectedRevision = c.ExpectedRevision - 1 }, DuelError.StaleSummonSelection);
        Reject(e, c with { ProcedureId = "unknown" }, DuelError.UnsupportedSpecialSummon);
        Reject(e, c with { CardInstanceId = -1 }, DuelError.SpecialSummonSourceUnavailable);
        Reject(e, c with { CardInstanceId = Hand(e, Low) }, DuelError.UnsupportedSpecialSummon);
        Reject(e, c with { MonsterZone = -1 }, DuelError.InvalidMonsterZone);
        Reject(e, c with { MonsterZone = 5 }, DuelError.InvalidMonsterZone);
        Reject(e, c with { Position = BattlePosition.FaceDownDefense }, DuelError.InvalidBattlePosition);
        Reject(e, c with { Position = (BattlePosition)99 }, DuelError.InvalidBattlePosition);
        Assert.Empty(e.State.SummonHistory);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, Low), 0));
        Reject(e, Command(e), DuelError.MonsterZoneOccupied);
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        Reject(e, Command(e), DuelError.WrongPhase);
    }

    [Fact]
    public void OrdinaryNormalEligibilityDoesNotAuthorizeAnUnregisteredSpecialProcedure()
    {
        var e = Engine(procedures: []); EnterMain(e);
        Reject(e, Command(e), DuelError.UnsupportedSpecialSummon);
        Reject(e, Command(e, card: Hand(e, Low)), DuelError.UnsupportedSpecialSummon);
    }

    [Fact]
    public void SourceZoneIsBoundByTheProfileAndCannotBeSpoofedByInstanceId()
    {
        var e = Engine(); EnterMain(e);
        Reject(e, Command(e, "revive"), DuelError.SpecialSummonSourceUnavailable);
        Reject(e, Command(e, "banished"), DuelError.SpecialSummonSourceUnavailable);
        int deck = e.State.GetPlayer(PlayerId.One).Deck.First(c => c.GameCardId == Target).InstanceId;
        Reject(e, Command(e, card: deck), DuelError.SpecialSummonSourceUnavailable);
        Reject(e, Command(e, card: Hand(e, Low, PlayerId.Two)), DuelError.SpecialSummonSourceUnavailable);
    }

    [Fact]
    public void EmptyOwnFieldAndOpposingMonsterRequirementsAreEvaluatedBeforeCommit()
    {
        var p = new SpecialSummonProcedureProfile("conditional", [Target], requiresEmptyOwnField: true, requiresOpposingMonster: true);
        var e = Engine(requiredProper: false, procedures: [p]); EnterMain(e);
        Reject(e, Command(e, "conditional"), DuelError.SpecialSummonConditionsNotMet);
        NextMain(e); Ok(e, new NormalSummon(PlayerId.Two, Hand(e, Low), 0)); NextMain(e);
        Ok(e, Command(e, "conditional"));
        Reject(e, Command(e, "conditional", zone: 1), DuelError.SpecialSummonConditionsNotMet);
    }

    [Fact]
    public void PositionAllowlistIsEnforcedWithoutSpendingTheCard()
    {
        var e = Engine(requiredProper: false, procedures: [new("defense", [Target], positions: [BattlePosition.FaceUpDefense])]); EnterMain(e);
        Reject(e, Command(e, "defense"), DuelError.InvalidBattlePosition);
        Ok(e, Command(e, "defense", position: BattlePosition.FaceUpDefense));
    }

    [Fact]
    public void ARestrictedTargetCannotBecomeProperByChoosingTheWrongHandProcedure()
    {
        var e = Engine(procedures: [new("wrong", [Target])]); EnterMain(e);
        Reject(e, Command(e, "wrong"), DuelError.ProperSummonRequired);
        Assert.Empty(e.State.SummonHistory);
    }

    [Fact]
    public void ProperStatusSurvivesDestructionAndRevivalCreatesFreshPlacement()
    {
        var e = Engine(); EnterMain(e); int instance = Hand(e, Target);
        Ok(e, Command(e, card: instance));
        var first = Record(e, instance);
        DestroyTargetInBattle(e);
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.InstanceId == instance);
        Assert.Equal(first, Record(e, instance));
        var result = Ok(e, Command(e, "revive", instance, position: BattlePosition.FaceUpDefense));
        var record = Record(e, instance);
        Assert.Equal("proper", record.ProperProcedureId); Assert.Equal("revive", record.ProcedureId);
        Assert.Equal(DecisionCardSource.Graveyard, record.Source);
        Assert.NotEqual(first.PlacementId, record.PlacementId);
        Assert.DoesNotContain(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.InstanceId == instance);
        Assert.Equal(DecisionCardSource.Graveyard, Assert.Single(result.Events.OfType<MonsterSpecialSummoned>()).Source);
    }

    [Fact]
    public void DiscardedUnqualifiedInstanceCannotBorrowAnotherInstancesProperHistory()
    {
        var e = Engine(); EnterMain(e); int proper = Hand(e, Target); Ok(e, Command(e, card: proper));
        int unqualified = Hand(e, Target);
        // Keep the proper target on the field and naturally draw past the hand limit.
        for (int i = 0; i < 6; i++) NextMain(e);
        Assert.True(e.State.GetPlayer(PlayerId.One).Hand.Count > 6);
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        Ok(e, new DiscardForHandLimit(PlayerId.One, unqualified));
        NextMain(e); NextMain(e);
        Reject(e, Command(e, "revive", unqualified, zone: 1), DuelError.ProperSummonRequired);
        Assert.Equal("proper", Record(e, proper).ProperProcedureId);
        Assert.DoesNotContain(e.State.SummonHistory, r => r.Card.InstanceId == unqualified);
    }

    [Fact]
    public void PendingSpecialAttemptDoesNotQualifyUntilSuccessfulCompletion()
    {
        var e = Engine(window: true); EnterMain(e); int instance = Hand(e, Target);
        Ok(e, Command(e, card: instance, position: BattlePosition.FaceUpDefense));
        var pending = e.State.PendingSummon!;
        Assert.True(pending.IsSpecialSummon); Assert.False(e.State.NormalSummonUsed);
        Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        var initial = Record(e, instance);
        Assert.Equal(SummonStatus.Pending, initial.Status); Assert.Null(initial.ProperProcedureId);
        Assert.Null(initial.PlacementId); Assert.Null(initial.CompletedOnRevision);
        PassSummon(e); PassSummon(e);
        var done = Record(e, instance);
        Assert.Equal(pending.AttemptId, done.AttemptId); Assert.Equal(SummonStatus.Completed, done.Status);
        Assert.Equal("proper", done.ProperProcedureId); Assert.Equal(BattlePosition.FaceUpDefense, done.Position);
        Assert.Equal(e.State.Revision, done.CompletedOnRevision);
    }

    [Fact]
    public void NormalOnlyNegatorCannotNegateASpecialProcedure()
    {
        var e = Engine(window: true, negateKinds: SummonNegationKinds.Normal); EnterMain(e);
        NextMain(e); SetNegator(e); NextMain(e); Ok(e, Command(e));
        var p = e.State.PendingSummon!;
        Reject(e, new ActivateSummonNegation(p.Responder, p.DecisionId, 0), DuelError.UnsupportedSummonResponse);
        PassSummon(e); PassSummon(e);
        Assert.False(e.State.NormalSummonUsed);
    }

    [Fact]
    public void NegatedSpecialProcedureIsNotProperAndCannotBeRevived()
    {
        var e = Engine(window: true); EnterMain(e); int instance = Hand(e, Target);
        NextMain(e); SetNegator(e); NextMain(e); Ok(e, Command(e, card: instance));
        var pending = e.State.PendingSummon!;
        Ok(e, new ActivateSummonNegation(pending.Responder, pending.DecisionId, 0));
        Pass(e); Pass(e); var result = Resolve(e);
        Assert.Single(result.Events.OfType<SummonNegated>());
        Assert.DoesNotContain(result.Events, x => x is MonsterSpecialSummoned);
        Assert.Equal(SummonStatus.Negated, Record(e, instance).Status);
        Assert.Null(Record(e, instance).ProperProcedureId);
        Assert.Equal(7500, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.False(e.State.NormalSummonUsed);
        Reject(e, Command(e, "revive", instance), DuelError.ProperSummonRequired);
    }

    [Fact]
    public void CounterNegationPreservesContextAndGrantsProperStatusOnlyAtCompletion()
    {
        var e = Engine(window: true, responses: [new("counter", Counter, ChainOperation.NegateActivation, ChainSpellSpeed.Three,
            discardCardIds: [Low])]);
        EnterMain(e); int instance = Hand(e, Target);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, Counter), 0));
        NextMain(e); SetNegator(e);
        NextMain(e);
        Ok(e, Command(e, card: instance));
        var p = e.State.PendingSummon!;
        string saved = JsonSerializer.Serialize(e.State); var before = e.State;
        Ok(e, new ActivateSummonNegation(p.Responder, p.DecisionId, 0));
        Assert.Equal(saved, JsonSerializer.Serialize(before));
        Assert.Equal(SummonStatus.Pending, Record(e, instance).Status);
        Ok(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 0));
        Assert.Equal(SummonStatus.Pending, Record(e, instance).Status);
        var decision = e.State.PendingDecision!;
        Ok(e, new ChooseDecisionCard(PlayerId.One, decision.DecisionId, Hand(e, Low)));
        Assert.Null(Record(e, instance).ProperProcedureId);
        Pass(e); Pass(e); Resolve(e);
        Assert.Equal(SummonStatus.Pending, Record(e, instance).Status);
        var result = Resolve(e);
        Assert.Single(result.Events.OfType<MonsterSpecialSummoned>());
        Assert.DoesNotContain(result.Events, x => x is SummonNegated);
        Assert.Equal(SummonStatus.Completed, Record(e, instance).Status);
        Assert.Equal("proper", Record(e, instance).ProperProcedureId);
        Assert.Equal(p.AttemptId, Record(e, instance).AttemptId);
        Assert.Equal(7500, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    private static DuelActionResult ResolveDeckSpell(DuelEngine e, uint choice, int zone = 0)
    {
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Spell), 0));
        var decision = e.State.PendingDecision!;
        int card = decision.CardOptions.First(c => c.GameCardId == choice).InstanceId;
        Ok(e, new ChooseDecisionCard(PlayerId.One, decision.DecisionId, card));
        decision = e.State.PendingDecision!;
        Ok(e, new ChooseDecisionPosition(PlayerId.One, decision.DecisionId, BattlePosition.FaceUpAttack));
        return Ok(e, new ChooseDecisionZone(PlayerId.One, e.State.PendingDecision!.DecisionId, zone));
    }

    [Fact]
    public void ExistingDeckEffectUsesSharedPlacementAndHistoryWithoutASummonNegationWindow()
    {
        var e = Engine(requiredProper: false, window: true); EnterMain(e);
        var result = ResolveDeckSpell(e, Target);
        var monster = Assert.Single(result.Events.OfType<MonsterSpecialSummoned>()).Monster;
        Assert.Null(e.State.PendingSummon);
        Assert.True(e.State.ResearchSliceComplete); // Existing research continuation limit is preserved.
        Assert.Equal(SummonMethod.SpecialEffect, Record(e, monster.Card.InstanceId).Method);
        Assert.Null(Record(e, monster.Card.InstanceId).ProperProcedureId);
        Assert.Equal(DecisionCardSource.Deck, Record(e, monster.Card.InstanceId).Source);
    }

    [Fact]
    public void RestrictedEffectChoicesAreFilteredAndNoLegalChoiceRejectsBeforeActivation()
    {
        var e = Engine(); EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Spell), 0));
        Assert.All(e.State.PendingDecision!.CardOptions, c => Assert.Equal(Alternative, c.GameCardId));
        var blocked = Engine(onlyTargetInEffect: true); EnterMain(blocked);
        Reject(blocked, new ActivateSpell(PlayerId.One, Hand(blocked, Spell), 0), DuelError.DecisionUnavailable);
        Assert.Empty(blocked.State.SummonHistory);
    }

    [Fact]
    public void ExplicitCardPolicyCanDisallowAnOtherwiseAllowlistedEffectSource()
    {
        var e = Engine(cards: [new(Target, SpecialSummonSources.Hand), new(Alternative, SpecialSummonSources.Hand)]);
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Spell), 0), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void HistorySurvivesTheExistingDelayedBanishmentStop()
    {
        var e = Engine(requiredProper: false, banishAtEndPhase: true); EnterMain(e);
        var result = ResolveDeckSpell(e, Target);
        int instance = Assert.Single(result.Events.OfType<MonsterSpecialSummoned>()).Monster.Card.InstanceId;
        var record = Record(e, instance);
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Banished, c => c.InstanceId == instance);
        Assert.Equal(record, Record(e, instance));
        Assert.True(e.State.ResearchSliceComplete);
    }

    [Fact]
    public void SpecialSummonSuccessTriggersRunOnceAndOnlyAfterTheWindow()
    {
        var trigger = new TriggerEffectProfile("special", Target, MonsterEventKind.SpecialSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100);
        var e = Engine(window: true, triggers: [trigger]); EnterMain(e); Ok(e, Command(e));
        Assert.Null(e.State.EventEffects.PendingTriggers);
        PassSummon(e); PassSummon(e);
        Assert.Single(e.State.EventEffects.PendingTriggers!.Remaining);
        Drain(e); Assert.Equal(8100, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Single(e.State.SummonHistory);
    }

    [Fact]
    public void MainTwoUsesTheSameProcedureAndReplayRemainsDeterministic()
    {
        DuelEngine Run()
        {
            var e = Engine(); EnterMain(e); NextMain(e); NextMain(e);
            Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Battle));
            Reject(e, Command(e), DuelError.WrongPhase);
            Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Main2)); Ok(e, Command(e));
            return e;
        }
        Assert.Equal(JsonSerializer.Serialize(Run().State), JsonSerializer.Serialize(Run().State));
    }

    [Fact]
    public void ProfilesAreImmutableAndInvalidOrMissingRegistrationsFailClosed()
    {
        Assert.Throws<ArgumentException>(() => Engine(cards: []));
        Assert.Throws<ArgumentException>(() => Engine(cards: [new(Spell)]));
        Assert.Throws<ArgumentException>(() => Engine(procedures: [new("same", [Target]), new("same", [Target])]));
        Assert.Throws<ArgumentException>(() => new SpecialSummonProcedureProfile("deck", [Target], DecisionCardSource.Deck));
        Assert.Throws<ArgumentException>(() => new SpecialSummonProcedureProfile("set", [Target], positions: [BattlePosition.FaceDownDefense]));
        Assert.Throws<ArgumentException>(() => new SpecialSummonCardProfile(Target, (SpecialSummonSources)32));
        uint[] cards = [Target]; BattlePosition[] positions = [BattlePosition.FaceUpAttack];
        var profile = new SpecialSummonProcedureProfile("copy", cards, positions: positions);
        cards[0] = Low; positions[0] = BattlePosition.FaceDownDefense;
        Assert.Equal(Target, Assert.Single(profile.CardIds));
        Assert.Equal(BattlePosition.FaceUpAttack, Assert.Single(profile.Positions));
        Assert.Throws<NotSupportedException>(() => ((IList<uint>)profile.CardIds).Clear());
    }

    private static BattleResponseProfile BattleSummoning() => new("battle-summon", BattleFlowScenario.Summon,
        BattleResponseOperation.SpecialSummonFromHand, BattleTiming.AttackDeclaration, summonableCardIds: [BattleFlowScenario.B]);

    [Fact]
    public void LegacyBattleResponseUsesSharedHistoryAndDoesNotOpenASummonNegationWindow()
    {
        var e = BattleFlowScenario.Engine(defender: false, profiles: [BattleSummoning()],
            specialCards: [new(BattleFlowScenario.B, SpecialSummonSources.Hand)], summonWindows: [new(BattleFlowScenario.B)]);
        BattleFlowScenario.Ready(e); BattleFlowScenario.Declare(e, null);
        int card = Hand(e, BattleFlowScenario.B, PlayerId.Two);
        BattleFlowScenario.Respond(e, 2, card, 1); BattleFlowScenario.DrainEffects(e);
        Assert.Equal(SummonMethod.SpecialEffect, Record(e, card).Method);
        Assert.Equal(DecisionCardSource.Hand, Record(e, card).Source);
        Assert.Null(Record(e, card).ProperProcedureId); Assert.Null(e.State.PendingSummon);
        Assert.True(e.State.Battle!.Attack!.ReplayRequired);
    }

    [Fact]
    public void BattleResponsePreflightHonorsExplicitEffectSourceRestrictions()
    {
        var e = BattleFlowScenario.Engine(defender: false, profiles: [BattleSummoning()],
            specialCards: [new(BattleFlowScenario.B, SpecialSummonSources.Deck)]);
        BattleFlowScenario.Ready(e); BattleFlowScenario.Declare(e, null);
        var w = e.State.Battle!.Window;
        Reject(e, new ActivateBattleResponse(w.Responder, w.DecisionId, 2, Hand(e, BattleFlowScenario.B, PlayerId.Two), 1),
            DuelError.UnsupportedSpecialSummon);
    }

    [Fact]
    public void BattleResponseRevalidatesTheSelectedHandInstanceWhenItsLinkResolves()
    {
        var e = BattleFlowScenario.Engine(defender: false, profiles: [BattleSummoning()],
            chains: [new("target-other-trap", BattleFlowScenario.Counter, ChainOperation.DestroyBoundSpellTrap, ChainSpellSpeed.Two,
                    battleWindows: BattleTiming.AttackDeclaration),
                new("discard-selected", BattleFlowScenario.Stats, ChainOperation.NegateEffect, ChainSpellSpeed.Three,
                    discardCardIds: [BattleFlowScenario.B], battleWindows: BattleTiming.AttackDeclaration)]);
        BattleFlowScenario.Ready(e); BattleFlowScenario.Declare(e, null);
        int selected = Hand(e, BattleFlowScenario.B, PlayerId.Two);
        BattleFlowScenario.Respond(e, 2, selected, 1);
        Ok(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 1));
        var choice = e.State.PendingDecision!;
        int target = e.State.GetPlayer(PlayerId.Two).SpellTrapFields[1]!.Card.InstanceId;
        Ok(e, new ChooseDecisionTarget(PlayerId.One, choice.DecisionId, PlayerId.Two, 1, target));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 3));
        choice = e.State.PendingDecision!;
        Ok(e, new ChooseDecisionCard(PlayerId.Two, choice.DecisionId, selected));
        Pass(e); Pass(e); Resolve(e); Resolve(e);
        var result = Resolve(e);
        Assert.Contains(result.Events, x => x is ChainLinkSkipped { Number: 1, Reason: ChainSkipReason.TargetUnavailable });
        Assert.DoesNotContain(result.Events, x => x is MonsterSpecialSummoned);
        Assert.Null(e.State.GetPlayer(PlayerId.Two).MonsterZones[1]);
        Assert.DoesNotContain(e.State.SummonHistory, r => r.Card.InstanceId == selected);
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Graveyard, c => c.InstanceId == selected);
    }

    [Fact]
    public void ExistingTokenSliceRecordsCreationWithoutGrantingAProperProcedure()
    {
        var e = ChainScenario.Engine(4909);
        DuelChainTests.Commit(e);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 1));
        DuelChainTests.PassBoth(e);
        var result = DuelChainTests.Resolve(e);
        var tokens = result.Events.OfType<TokenSpecialSummoned>().ToArray();
        Assert.Equal(4, tokens.Length);
        foreach (var token in tokens)
        {
            var record = Record(e, token.Monster.Card.InstanceId);
            Assert.Equal(SummonMethod.Token, record.Method); Assert.Null(record.Source);
            Assert.Null(record.ProperProcedureId); Assert.Equal(SummonStatus.Completed, record.Status);
        }
    }
}
