using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Authored profiles and procedural timing tests, not PSP gameplay comparisons.</summary>
public sealed class SummonProfileTests
{
    private const uint Low = uint.MaxValue - 1, High = uint.MaxValue, Filler = 100_000;
    private const uint Negate = 100_001, Counter = 100_002, Other = 100_003;

    private static DuelEngine Engine(uint level = 8, IEnumerable<SummonLevelModifierProfile>? levels = null,
        IEnumerable<TributeProcedureProfile>? procedures = null, bool restricted = false,
        IEnumerable<SummonWindowProfile>? windows = null, IEnumerable<SummonNegationProfile>? negations = null,
        IEnumerable<ChainResponseProfile>? responses = null, IEnumerable<TriggerEffectProfile>? triggers = null,
        IEnumerable<ContinuousNegationProfile>? continuousNegations = null, bool eligible = true, bool secondHigh = false,
        bool counterIsSpell = false)
    {
        uint[] ids = [Low, High, Filler, Negate, Counter, Other];
        var catalog = new CardCatalog(new(null, "synthetic-summon-profiles"), ids.Select(id =>
            new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, id == High ? level : 4))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(id == Counter && counterIsSpell ? CardCategory.Spell
            : id is Negate or Counter or Other ? CardCategory.Trap : CardCategory.Monster,
            AllowsNormalSummon: id is Low or High or Filler, AllowsBasicBattle: id is Low or High or Filler,
            AllowsSpellTrapSet: id is Negate or Counter or Other, AllowsTributeSummon: id == High && eligible,
            AllowsTributeForSummon: id is Low or Filler, RequiresRegisteredTributeProcedure: id == High && restricted));
        uint[] first = [Low, Low, High, Counter, Other, secondHigh ? High : Filler, .. Enumerable.Repeat(Filler, 34)];
        uint[] second = [Low, Low, Negate, Counter, Filler, .. Enumerable.Repeat(Filler, 35)];
        return new(catalog, rules, first, second, experimentalSummonLevelModifiers: levels,
            experimentalTributeProcedures: procedures, experimentalSummonWindows: windows,
            experimentalSummonNegations: negations, experimentalChainResponses: responses,
            experimentalTriggerEffects: triggers, experimentalContinuousNegations: continuousNegations);
    }

    private static int Hand(DuelEngine e, uint id, PlayerId player = PlayerId.One) =>
        e.State.GetPlayer(player).Hand.First(c => c.GameCardId == id).InstanceId;
    private static MonsterBinding Bound(DuelEngine e, int zone)
    {
        var m = e.State.GetPlayer(PlayerId.One).MonsterZones[zone]!;
        return new(PlayerId.One, zone, m.Card, m.PlacementId);
    }
    private static void Set(DuelEngine e, uint id, int zone) =>
        Ok(e, new SetSpellTrap(e.State.ActivePlayer, Hand(e, id, e.State.ActivePlayer), zone));
    private static void Prepare(DuelEngine e, int donors = 1, bool faceDown = false, bool setNegator = true)
    {
        EnterMain(e);
        Set(e, Counter, 0); Set(e, Other, 1);
        if (donors != 0) Ok(e, faceDown ? new SetMonster(PlayerId.One, Hand(e, Low), 0)
            : new NormalSummon(PlayerId.One, Hand(e, Low), 0));
        NextMain(e);
        if (setNegator) Set(e, Negate, 0);
        Set(e, Counter, 1);
        NextMain(e);
        if (donors == 2)
        {
            Ok(e, new NormalSummon(PlayerId.One, Hand(e, Low), 1));
            NextMain(e); NextMain(e);
        }
    }
    private static TributeSummon Tribute(DuelEngine e, int[]? zones = null, string? procedure = null, bool set = false) =>
        new(PlayerId.One, e.State.Revision, Hand(e, High), 0, (zones ?? [0]).Select(z => Bound(e, z)), set, procedure);
    private static SummonLevelModifierProfile Delta(int delta, EffectRelation relation = EffectRelation.Controller, string id = "level") =>
        new(id, Low, relation, [High], delta);
    private static void PassSummon(DuelEngine e)
    {
        var p = e.State.PendingSummon!;
        Ok(e, new PassSummonResponse(p.Responder, p.DecisionId));
    }
    private static void NegateSummon(DuelEngine e)
    {
        var p = e.State.PendingSummon!;
        Ok(e, new ActivateSummonNegation(p.Responder, p.DecisionId, 0));
    }
    private static TriggerEffectProfile Grave(string id, uint card = Low, TriggerTiming timing = TriggerTiming.If) =>
        new(id, card, MonsterEventKind.SentToGraveyard, EffectRelation.Self, TriggerRequirement.Optional,
            TriggerOperation.GainLifePoints, lifePointGain: 100, sourceLocation: TriggerSourceLocation.GraveyardSelf, timing: timing);

    [Theory]
    [InlineData(8u, -2, 6u, 1)]
    [InlineData(8u, -20, 1u, 0)]
    [InlineData(uint.MaxValue, int.MaxValue, uint.MaxValue, 2)]
    public void EffectiveHandLevelControlsOrdinaryCountAndClampsWithoutChangingCatalog(uint raw, int delta, uint effective, int count)
    {
        var e = Engine(raw, levels: [Delta(delta)]); Prepare(e, donors: count == 2 ? 2 : 1);
        // Two identical sources each contribute in the full-width case; it still saturates safely.
        Assert.Equal(effective, e.GetHandSummonLevel(PlayerId.One, Hand(e, High)));
        var command = count == 0
            ? new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, High), 4, [])
            : Tribute(e, count == 2 ? [0, 1] : [0]);
        var r = Ok(e, command);
        Assert.Equal(count, r.Events.OfType<MonsterTributed>().Count());
        Assert.Equal(High, e.State.GetPlayer(PlayerId.One).MonsterZones[count == 0 ? 4 : 0]!.Card.GameCardId);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReducedHandLevelPermitsStandardNoTributeNormalSummonOrSet(bool set)
    {
        var e = Engine(5, levels: [Delta(-1)]); Prepare(e);
        Ok(e, set ? new SetMonster(PlayerId.One, Hand(e, High), 4) : new NormalSummon(PlayerId.One, Hand(e, High), 4));
        Assert.Empty(e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.NotNull(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
    }

    [Fact]
    public void FaceDownSourcesAndWrongRelationsDoNotModifyHandLevel()
    {
        var faceDown = Engine(levels: [Delta(-4)]); Prepare(faceDown, faceDown: true);
        Assert.Equal(8u, faceDown.GetHandSummonLevel(PlayerId.One, Hand(faceDown, High)));
        var opponent = Engine(levels: [Delta(-4, EffectRelation.Opponent)]); Prepare(opponent);
        Assert.Equal(8u, opponent.GetHandSummonLevel(PlayerId.One, Hand(opponent, High)));
        Assert.Null(opponent.GetHandSummonLevel(PlayerId.One, -1));
    }

    [Fact]
    public void SourceNegationChangesLevelAndCountingCommitsBeforeRemovingTheSource()
    {
        var e = Engine(levels: [Delta(-2)], continuousNegations: [new("negate-low", Filler, EffectRelation.Controller, [Low])]);
        Prepare(e);
        Assert.Equal(6u, e.GetHandSummonLevel(PlayerId.One, Hand(e, High)));
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, Filler), 1));
        Assert.Equal(8u, e.GetHandSummonLevel(PlayerId.One, Hand(e, High)));
        NextMain(e); NextMain(e);
        Reject(e, Tribute(e), DuelError.InvalidTributeSelection);
        // A different engine removes the reducing source as the sole tribute; no mid-procedure recount.
        var active = Engine(levels: [Delta(-2)], secondHigh: true); Prepare(active);
        Ok(active, Tribute(active));
        Assert.Single(active.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Equal(8u, active.GetHandSummonLevel(PlayerId.One, Hand(active, High)));
    }

    [Fact]
    public void AdditiveModifiersStackAndClampOnlyAfterTheSum()
    {
        var e = Engine(5, levels: [Delta(-10, id: "lower"), Delta(8, id: "raise")]); Prepare(e);
        Assert.Equal(3u, e.GetHandSummonLevel(PlayerId.One, Hand(e, High)));
        var reverse = Engine(5, levels: [Delta(8, id: "raise"), Delta(-10, id: "lower")]); Prepare(reverse);
        Assert.Equal(3u, reverse.GetHandSummonLevel(PlayerId.One, Hand(reverse, High)));
    }

    [Fact]
    public void ALevelLessMonsterAndIneligibleTargetRemainUnsupported()
    {
        var e = Engine(0, levels: [Delta(5)]); Prepare(e);
        Assert.Equal(0u, e.GetHandSummonLevel(PlayerId.One, Hand(e, High)));
        Reject(e, Tribute(e), DuelError.UnsupportedMonsterLevel);
        e = Engine(5, levels: [Delta(1)], eligible: false); Prepare(e);
        Reject(e, Tribute(e), DuelError.TributeSummonNotSupported);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExplicitTwoTributeProcedureCanOverrideLevelFiveForSummonAndSet(bool set)
    {
        var e = Engine(5, procedures: [new("two", High, 2, [Low])], restricted: true); Prepare(e, donors: 2);
        Reject(e, Tribute(e, [0, 1]), DuelError.UnsupportedTributeProcedure);
        Reject(e, new NormalSummon(PlayerId.One, Hand(e, High), 4), DuelError.UnsupportedTributeProcedure);
        Reject(e, new SetMonster(PlayerId.One, Hand(e, High), 4), DuelError.UnsupportedTributeProcedure);
        Reject(e, Tribute(e, [0], "two"), DuelError.InvalidTributeSelection);
        var result = Ok(e, Tribute(e, [1, 0], "two", set));
        Assert.Equal(2, result.Events.OfType<MonsterTributed>().Count());
    }

    [Fact]
    public void NamedProcedureIsBoundToTargetAndEnforcesFaceUpAndSetPolicy()
    {
        var e = Engine(procedures: [new("one", High, 1, [Low], requireFaceUpTributes: true, allowsSet: false)]);
        Prepare(e, faceDown: true);
        Reject(e, Tribute(e, procedure: "unknown"), DuelError.UnsupportedTributeProcedure);
        Reject(e, Tribute(e, procedure: "one", set: true), DuelError.UnsupportedTributeProcedure);
        Reject(e, Tribute(e, procedure: "one"), DuelError.TributeNotAllowed);
        Ok(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack));
        Drain(e);
        Ok(e, Tribute(e, procedure: "one"));
    }

    [Fact]
    public void OptionalZeroTributeProcedureDoesNotSpendDonorsAndCanForbidSet()
    {
        var e = Engine(procedures: [new("free", High, 0, allowsSet: false)]); Prepare(e);
        Reject(e, Tribute(e, [], "free", set: true), DuelError.UnsupportedTributeProcedure);
        Reject(e, Tribute(e, [0], "free"), DuelError.InvalidTributeSelection);
        // The zero-tribute procedure cannot overwrite a field monster.
        Reject(e, Tribute(e, [], "free"), DuelError.MonsterZoneOccupied);
        Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, High), 4, [], procedureId: "free"));
        Assert.Empty(e.State.GetPlayer(PlayerId.One).Graveyard);
    }

    [Fact]
    public void PendingAttemptReservesTheTargetWithoutFieldEntryAndOnlySuccessOffersSummonTriggers()
    {
        var trigger = new TriggerEffectProfile("on-summon", High, MonsterEventKind.NormalSummoned,
            EffectRelation.Self, TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 500);
        var e = Engine(windows: [new(High)], triggers: [trigger, Grave("if"), Grave("when", timing: TriggerTiming.When)]);
        Prepare(e, donors: 2);
        var before = e.State; string saved = JsonSerializer.Serialize(before);
        var attempt = Ok(e, Tribute(e, [0, 1]));
        Assert.NotNull(e.State.PendingSummon);
        Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.DoesNotContain(e.State.GetPlayer(PlayerId.One).Hand, c => c.GameCardId == High);
        Assert.True(e.State.NormalSummonUsed);
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.DoesNotContain(attempt.Events, x => x is MonsterNormalSummoned);
        Assert.Equal(saved, JsonSerializer.Serialize(before));
        PassSummon(e);
        var p = e.State.PendingSummon!;
        var result = Ok(e, new PassSummonResponse(p.Responder, p.DecisionId));
        Assert.Null(e.State.PendingSummon);
        Assert.Single(result.Events.OfType<MonsterNormalSummoned>());
        Assert.Equal(2, result.Events.OfType<TriggerMissedTiming>().Count());
        Assert.Equal(3, e.State.EventEffects.PendingTriggers!.Remaining.Count);
        Drain(e);
        Assert.Equal(8700, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void SummonWindowRejectsWrongActorsStaleAnswersAndUnrelatedCommandsAtomically()
    {
        var e = Engine(windows: [new(High)]); Prepare(e, donors: 2); Ok(e, Tribute(e, [0, 1]));
        var pending = e.State.PendingSummon!;
        Reject(e, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.SummonWindowPending);
        Reject(e, new PassSummonResponse(PlayerId.One, pending.DecisionId), DuelError.NotSummonResponder);
        Reject(e, new PassSummonResponse(PlayerId.Two, pending.DecisionId - 1), DuelError.StaleSummonDecision);
        Reject(e, new ActivateSummonNegation(PlayerId.Two, pending.DecisionId, -1), DuelError.InvalidSpellTrapZone);
        Reject(e, new ActivateSummonNegation(PlayerId.Two, pending.DecisionId, 0), DuelError.UnsupportedSummonResponse);
        PassSummon(e);
        Reject(e, new PassSummonResponse(PlayerId.Two, pending.DecisionId), DuelError.StaleSummonDecision);
        PassSummon(e);
        Reject(e, new PassSummonResponse(PlayerId.One, pending.DecisionId), DuelError.NoPendingSummon);
    }

    [Fact]
    public void NegatingSummonDoesNotRefundTributesAllowanceOrCostOrCreateAFieldPlacement()
    {
        var e = Engine(windows: [new(High)], negations: [new("negate", Negate, 1000)], triggers: [Grave("target-grave", High)]);
        Prepare(e, donors: 2); Ok(e, Tribute(e, [0, 1]));
        var target = e.State.PendingSummon!.Card;
        NegateSummon(e);
        Assert.Equal(7000, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Pass(e); Pass(e);
        var result = Resolve(e);
        Assert.Single(result.Events.OfType<SummonNegated>());
        Assert.DoesNotContain(result.Events, x => x is MonsterNormalSummoned or MonsterDestroyed);
        Assert.Null(e.State.PendingSummon); Assert.Null(e.State.PendingChain);
        Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Equal(3, e.State.GetPlayer(PlayerId.One).Graveyard.Count);
        Assert.Contains(target, e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.True(e.State.NormalSummonUsed);
        Assert.Equal(-1, Assert.Single(e.State.EventEffects.PendingTriggers!.Remaining).Occurrence.Subject.Zone);
        Drain(e);
        Assert.False(e.State.ResearchSliceComplete);
        Assert.Equal(8100, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Single(e.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == Negate);
    }

    [Theory]
    [InlineData(ChainOperation.NegateActivation)] [InlineData(ChainOperation.NegateEffect)]
    public void CounterNegationOfTheNegatorAllowsSummonWhileKeepingPaidCosts(ChainOperation operation)
    {
        var e = Engine(windows: [new(High)], negations: [new("negate", Negate, 1000)],
            responses: [new("counter", Counter, operation, ChainSpellSpeed.Three, respondsToCategory: CardCategory.Trap,
                destroyNegatedSource: true)]);
        Prepare(e, donors: 2); Ok(e, Tribute(e, [0, 1])); NegateSummon(e);
        var chain = e.State.PendingChain!;
        Ok(e, new ActivateChainResponse(PlayerId.One, chain.DecisionId, 0));
        Pass(e); Pass(e); Resolve(e); var result = Resolve(e);
        Assert.Single(result.Events.OfType<MonsterNormalSummoned>());
        Assert.DoesNotContain(result.Events, x => x is SummonNegated);
        Assert.Equal(7000, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Equal(2, e.State.GetPlayer(PlayerId.One).Graveyard.Count(c => c.GameCardId == Low));
        Assert.Equal(High, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card.GameCardId);
        Assert.Null(e.State.PendingSummon); Assert.Null(e.State.PendingChain);
        Assert.False(e.State.ResearchSliceComplete);
    }

    [Fact]
    public void OnlyCounterSpeedNegationResponsesMayChainDuringThePendingSummon()
    {
        var e = Engine(windows: [new(High)], negations: [new("negate", Negate)],
            responses: [new("too-slow", Counter, ChainOperation.NegateActivation, ChainSpellSpeed.Two),
                new("move-target", Other, ChainOperation.DestroyBoundSpellTrap, ChainSpellSpeed.Three)]);
        Prepare(e, donors: 2); Ok(e, Tribute(e, [0, 1])); NegateSummon(e);
        var chain = e.State.PendingChain!;
        Reject(e, new ActivateChainResponse(PlayerId.One, chain.DecisionId, 0), DuelError.ChainResponseSpeedNotAllowed);
        Reject(e, new ActivateChainResponse(PlayerId.One, chain.DecisionId, 1), DuelError.UnsupportedSummonResponse);
        Reject(e, new PassSummonResponse(PlayerId.One, e.State.PendingSummon!.DecisionId), DuelError.ChainPending);
    }

    [Theory]
    [InlineData(8000)] [InlineData(int.MaxValue)]
    public void UnpayableNegationCostLeavesTheAttemptAndTrapUnchanged(int cost)
    {
        var e = Engine(windows: [new(High)], negations: [new("negate", Negate, cost)]);
        Prepare(e, donors: 2); Ok(e, Tribute(e, [0, 1]));
        var p = e.State.PendingSummon!;
        Reject(e, new ActivateSummonNegation(p.Responder, p.DecisionId, 0), DuelError.InsufficientLifePoints);
        PassSummon(e); PassSummon(e);
    }

    [Fact]
    public void MissingNegationSourceAndSelfNegationReject()
    {
        var e = Engine(windows: [new(High)], negations: [new("negate", Negate)]);
        Prepare(e, donors: 2, setNegator: false);
        // Opponent has not set the negator; the slot is empty.
        Ok(e, Tribute(e, [0, 1]));
        var p = e.State.PendingSummon!;
        Reject(e, new ActivateSummonNegation(p.Responder, p.DecisionId, 0), DuelError.UnsupportedSummonResponse);
        PassSummon(e);
        p = e.State.PendingSummon!;
        Reject(e, new ActivateSummonNegation(p.Responder, p.DecisionId, 0), DuelError.UnsupportedSummonResponse);
    }

    [Fact]
    public void TributeSetNeverOpensSummonNegationAndDefaultProfilesRemainOff()
    {
        var e = Engine(windows: [new(High)], negations: [new("negate", Negate)]); Prepare(e, donors: 2);
        var result = Ok(e, Tribute(e, [0, 1], set: true));
        Assert.Single(result.Events.OfType<MonsterSet>());
        Assert.Null(e.State.PendingSummon); Assert.DoesNotContain(result.Events, x => x is SummonAttempted);
        var original = Engine(); Prepare(original, donors: 2);
        Ok(original, Tribute(original, [0, 1]));
        Assert.Null(original.State.PendingSummon);
    }

    [Fact]
    public void LowLevelNormalSummonCanOptIntoTheSamePreSuccessWindow()
    {
        var e = Engine(windows: [new(Low)]); EnterMain(e);
        var result = Ok(e, new NormalSummon(PlayerId.One, Hand(e, Low), 0));
        Assert.Single(result.Events.OfType<SummonAttempted>());
        Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        PassSummon(e); PassSummon(e);
        Assert.Equal(Low, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card.GameCardId);
    }

    [Fact]
    public void InvalidRegistrationsFailAtConstructionAndProfileCollectionsAreSnapshots()
    {
        Assert.Throws<ArgumentException>(() => Engine(restricted: true));
        Assert.Throws<ArgumentException>(() => Engine(procedures: [new("x", High, 1), new("x", High, 2)]));
        Assert.Throws<ArgumentException>(() => Engine(procedures: [new("x", High, 1, [Negate])]));
        Assert.Throws<ArgumentException>(() => Engine(levels: [new("x", Negate, EffectRelation.Any, [High], 1)]));
        Assert.Throws<ArgumentException>(() => Engine(windows: [new(Negate)]));
        Assert.Throws<ArgumentException>(() => Engine(negations: [new("x", High)]));
        Assert.Throws<ArgumentException>(() => Engine(levels: [Delta(-1)], negations: [new("level", Negate)]));
        Assert.Throws<ArgumentException>(() => new TributeProcedureProfile("x", High, 6));
        Assert.Throws<ArgumentException>(() => new SummonLevelModifierProfile("x", Low, EffectRelation.Self, [High], 1));
        uint[] targets = [High];
        var profile = new SummonLevelModifierProfile("copy", Low, EffectRelation.Controller, targets, -2);
        targets[0] = Filler;
        Assert.Equal(High, Assert.Single(profile.TargetCardIds));
        Assert.Throws<NotSupportedException>(() => ((IList<uint>)profile.TargetCardIds).Clear());
        SummonLevelModifierProfile[] profiles = [profile];
        var e = Engine(levels: profiles); profiles[0] = Delta(5);
        Prepare(e);
        Assert.Equal(6u, e.GetHandSummonLevel(PlayerId.One, Hand(e, High)));
    }

    [Fact]
    public void ThreeTributeProcedureRequiresExactlyThreeEligiblePlacements()
    {
        var e = Engine(procedures: [new("three", High, 3)], restricted: true); Prepare(e, donors: 2);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, Filler), 2));
        NextMain(e); NextMain(e);
        Reject(e, Tribute(e, [0, 1], "three"), DuelError.InvalidTributeSelection);
        var result = Ok(e, Tribute(e, [2, 0, 1], "three"));
        Assert.Equal(3, result.Events.OfType<MonsterTributed>().Count());
        Assert.Equal(3, e.State.GetPlayer(PlayerId.One).Graveyard.Count);
    }

    [Fact]
    public void AlternativeProcedureRejectsWrongDonorCardEvenWhenItIsGenerallyTributable()
    {
        var e = Engine(procedures: [new("only-low", High, 1, [Low])]); Prepare(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, Filler), 1));
        NextMain(e); NextMain(e);
        Reject(e, Tribute(e, [1], "only-low"), DuelError.TributeNotAllowed);
        Ok(e, Tribute(e, [0], "only-low"));
    }

    [Fact]
    public void SummonAndChainContextSurviveDiscardDecisionsAndStaleAnswersRejectAtomically()
    {
        var e = Engine(windows: [new(High)], negations: [new("negate", Negate, 500)],
            responses: [new("counter-cost", Counter, ChainOperation.NegateActivation, ChainSpellSpeed.Three,
                discardCardIds: [Filler])], triggers: [Grave("discard-if", Filler)]);
        Prepare(e, donors: 2); Ok(e, Tribute(e, [0, 1])); NegateSummon(e);
        var pending = e.State.PendingSummon!;
        var chain = e.State.PendingChain!;
        Ok(e, new ActivateChainResponse(PlayerId.One, chain.DecisionId, 0));
        var decision = e.State.PendingDecision!;
        Assert.Equal(pending.AttemptId, e.State.PendingSummon!.AttemptId);
        Reject(e, new ChooseDecisionCard(PlayerId.One, decision.DecisionId - 1, Hand(e, Filler)), DuelError.StaleDecision);
        var before = e.State; string saved = JsonSerializer.Serialize(before);
        Ok(e, new ChooseDecisionCard(PlayerId.One, decision.DecisionId, Hand(e, Filler)));
        Assert.Equal(saved, JsonSerializer.Serialize(before));
        Assert.Equal(pending.AttemptId, e.State.PendingSummon!.AttemptId);
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Pass(e); Pass(e); Resolve(e); Resolve(e);
        Assert.Null(e.State.PendingSummon); Assert.Null(e.State.PendingDecision);
        Assert.NotNull(e.State.EventEffects.PendingTriggers);
        Drain(e);
        Assert.Equal(8100, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(7500, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void AttemptedMonsterDoesNotApplyItsAuraOrTriggerAsAFieldSourceBeforeSuccess()
    {
        var modifier = new SummonLevelModifierProfile("pending-is-not-source", High, EffectRelation.Controller, [Filler], 2);
        var e = Engine(windows: [new(High)], levels: [modifier]); Prepare(e, donors: 2);
        int filler = Hand(e, Filler);
        Ok(e, Tribute(e, [0, 1]));
        Assert.Equal(4u, e.GetHandSummonLevel(PlayerId.One, filler));
        PassSummon(e); PassSummon(e);
        Assert.Equal(6u, e.GetHandSummonLevel(PlayerId.One, filler));
    }

    [Fact]
    public void IndependentEnginesReplayTheNegationPathDeterministically()
    {
        DuelEngine Make() => Engine(windows: [new(High)], negations: [new("negate", Negate, 750)]);
        var first = Make(); var second = Make();
        foreach (var e in new[] { first, second })
        {
            Prepare(e, donors: 2); Ok(e, Tribute(e, [0, 1])); NegateSummon(e);
            Pass(e); Pass(e); Resolve(e);
        }
        Assert.Equal(JsonSerializer.Serialize(first.State), JsonSerializer.Serialize(second.State));
    }

    [Fact]
    public void SpellSourcesCannotEnterTheCounterTrapOnlySummonChain()
    {
        var e = Engine(windows: [new(High)], negations: [new("negate", Negate)], counterIsSpell: true,
            responses: [new("spell-counter", Counter, ChainOperation.NegateActivation, ChainSpellSpeed.Three)]);
        Prepare(e, donors: 2); Ok(e, Tribute(e, [0, 1])); NegateSummon(e);
        Reject(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 0), DuelError.UnsupportedSummonResponse);
    }
}
