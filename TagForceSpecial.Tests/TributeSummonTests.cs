using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Ordinary Tribute Summon tests.</summary>
public sealed class TributeSummonTests
{
    private const uint Low = uint.MaxValue - 1, High = uint.MaxValue, OtherCard = 100_000;

    private static DuelEngine Setup(uint level = 5, int donors = 1, bool faceDown = false,
        bool eligibleDonor = true, bool eligibleTarget = true,
        IEnumerable<TriggerEffectProfile>? triggers = null,
        IEnumerable<ContinuousEffectProfile>? continuous = null,
        IEnumerable<ContinuousNegationProfile>? negations = null, bool staged = false,
        IEnumerable<DecisionSequence>? sequences = null)
    {
        uint[] ids = [Low, High, OtherCard];
        var catalog = new CardCatalog(new(null, "synthetic-stage9-tributes"), ids.Select(id =>
            new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, id == High ? level : 4))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(CardCategory.Monster,
            AllowsNormalSummon: true, AllowsBasicBattle: true,
            AllowsTributeSummon: id == High && eligibleTarget,
            AllowsTributeForSummon: id == Low && eligibleDonor));
        uint[] first = [.. Enumerable.Repeat(Low, donors), High, High, .. Enumerable.Repeat(OtherCard, 38 - donors)];
        var engine = new DuelEngine(catalog, rules, first, Enumerable.Repeat(OtherCard, 40),
            decisionSequences: sequences,
            experimentalTriggerEffects: triggers, experimentalContinuousEffects: continuous,
            experimentalContinuousNegations: negations, experimentalBattleFlow: staged);
        EnterMain(engine);
        for (int zone = 0; zone < donors; zone++)
        {
            if (zone > 0) { NextMain(engine); NextMain(engine); }
            var card = engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Low);
            Ok(engine, faceDown ? new SetMonster(PlayerId.One, card.InstanceId, zone)
                : new NormalSummon(PlayerId.One, card.InstanceId, zone));
            Drain(engine);
        }
        if (donors > 0) { NextMain(engine); NextMain(engine); }
        return engine;
    }

    private static MonsterBinding Binding(DuelEngine engine, int zone, PlayerId player = PlayerId.One)
    {
        var monster = engine.State.GetPlayer(player).MonsterZones[zone]!;
        return new(player, zone, monster.Card, monster.PlacementId);
    }

    private static TributeSummon Command(DuelEngine engine, int zone = 0, bool set = false,
        IEnumerable<MonsterBinding>? tributes = null) => new(PlayerId.One, engine.State.Revision,
        engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == High).InstanceId,
        zone, tributes ?? [Binding(engine, 0)], set);

    [Theory]
    [InlineData(5u, false)] [InlineData(5u, true)]
    [InlineData(6u, false)] [InlineData(6u, true)]
    [InlineData(7u, false)] [InlineData(7u, true)]
    [InlineData(8u, false)] [InlineData(8u, true)]
    [InlineData(uint.MaxValue, false)] [InlineData(uint.MaxValue, true)]
    public void SupportedLevelsSummonOrSetWithExactTributesAndSharedAllowance(uint level, bool set)
    {
        int count = level <= 6 ? 1 : 2;
        var engine = Setup(level, count, faceDown: set);
        var before = engine.State;
        string snapshot = JsonSerializer.Serialize(before);
        var selected = Enumerable.Range(0, count).Reverse().Select(z => Binding(engine, z)).ToArray();
        var result = Ok(engine, Command(engine, set: set, tributes: selected));
        var player = engine.State.GetPlayer(PlayerId.One);
        Assert.Equal(before.GetPlayer(PlayerId.One).Hand.Count - 1, player.Hand.Count);
        Assert.Equal(before.GetPlayer(PlayerId.One).Deck, player.Deck);
        Assert.Equal(selected.OrderBy(t => t.Zone).Select(t => t.Card), player.Graveyard);
        Assert.Single(player.MonsterZones, m => m is not null);
        var target = player.MonsterZones[0]!;
        Assert.Equal(High, target.Card.GameCardId);
        Assert.Equal(set ? BattlePosition.FaceDownDefense : BattlePosition.FaceUpAttack, target.Position);
        Assert.Equal(before.Revision + 1, target.PlacementId);
        Assert.Equal(before.TurnNumber, target.SummonedOnTurn);
        Assert.True(engine.State.NormalSummonUsed);
        Assert.Equal(count + 1, result.Events.Count);
        Assert.Equal(Enumerable.Range(0, count), result.Events.OfType<MonsterTributed>().Select(e => e.Monster.Zone));
        if (set) Assert.IsType<MonsterSet>(result.Events[^1]);
        else Assert.IsType<MonsterNormalSummoned>(result.Events[^1]);
        Assert.DoesNotContain(result.Events, e => e is MonsterDestroyed);
        Assert.Equal(snapshot, JsonSerializer.Serialize(before));
        Reject(engine, Command(engine, zone: 4, tributes: []), DuelError.NormalSummonLimitReached);
        var filler = player.Hand.First(c => c.GameCardId == OtherCard);
        Reject(engine, new NormalSummon(PlayerId.One, filler.InstanceId, 4), DuelError.NormalSummonLimitReached);
        Reject(engine, new SetMonster(PlayerId.One, filler.InstanceId, 4), DuelError.NormalSummonLimitReached);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FullFieldCanReuseFreedZoneButCannotOverwriteAnUnselectedMonster(bool set)
    {
        var engine = Setup(donors: 5);
        Reject(engine, Command(engine, zone: 4, set: set), DuelError.MonsterZoneOccupied);
        Ok(engine, Command(engine, zone: 0, set: set));
        Assert.Equal(5, engine.State.GetPlayer(PlayerId.One).MonsterZones.Count(m => m is not null));
        Assert.Single(engine.State.GetPlayer(PlayerId.One).Graveyard);
    }

    [Fact]
    public void DestinationMayBeAnUnoccupiedZone()
    {
        var engine = Setup();
        Ok(engine, Command(engine, zone: 4));
        Assert.Null(engine.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Equal(High, engine.State.GetPlayer(PlayerId.One).MonsterZones[4]!.Card.GameCardId);
    }

    [Fact]
    public void SelectionIsImmutableAndReplayingACompletedSelectionIsStale()
    {
        var engine = Setup();
        MonsterBinding[] input = [Binding(engine, 0)];
        var command = Command(engine, tributes: input);
        input[0] = input[0] with { Zone = 4 };
        Assert.Throws<NotSupportedException>(() => ((IList<MonsterBinding>)command.Tributes).Clear());
        Ok(engine, command);
        Reject(engine, command, DuelError.StaleSummonSelection);
    }

    [Fact]
    public void BadSelectionsRejectWithoutSpendingResourcesOrEmittingEvents()
    {
        var engine = Setup(7, 2);
        var first = Binding(engine, 0); var second = Binding(engine, 1);
        Reject(engine, Command(engine, tributes: []), DuelError.InvalidTributeSelection);
        Reject(engine, Command(engine, tributes: [first]), DuelError.InvalidTributeSelection);
        Reject(engine, Command(engine, tributes: [first, first]), DuelError.InvalidTributeSelection);
        Reject(engine, Command(engine, tributes: [first, second, second]), DuelError.InvalidTributeSelection);
        Reject(engine, Command(engine, tributes: [first, null!]), DuelError.InvalidTributeSelection);
        foreach (var invalid in new[] { second with { Player = PlayerId.Two }, second with { Player = (PlayerId)42 },
            second with { Zone = 5 }, second with { Zone = -1 }, second with { Zone = 4 },
            second with { PlacementId = second.PlacementId + 1 },
            second with { Card = first.Card }, second with { Card = second.Card with { Owner = PlayerId.Two } } })
            Reject(engine, Command(engine, tributes: [first, invalid]), DuelError.InvalidTributeSelection);
        var valid = Command(engine, tributes: [first, second]);
        Ok(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense));
        Reject(engine, valid, DuelError.StaleSummonSelection);
        Ok(engine, Command(engine, tributes: [first, second]));
    }

    [Fact]
    public void EligibilityAndLegacyHighLevelCommandsRemainExplicit()
    {
        var engine = Setup(eligibleDonor: false);
        Reject(engine, Command(engine), DuelError.TributeNotAllowed);
        engine = Setup(eligibleTarget: false);
        Reject(engine, Command(engine), DuelError.TributeSummonNotSupported);
        engine = Setup();
        int card = Command(engine).CardInstanceId;
        Reject(engine, new NormalSummon(PlayerId.One, card, 4), DuelError.TributeSummonNotSupported);
        Reject(engine, new SetMonster(PlayerId.One, card, 4), DuelError.TributeSummonNotSupported);
    }

    [Theory]
    [InlineData(0u)] [InlineData(4u)]
    public void LowLevelsDoNotGainAnAlternativeTributeProcedure(uint level)
    {
        var engine = Setup(level);
        Reject(engine, Command(engine), DuelError.UnsupportedMonsterLevel);
    }

    [Fact]
    public void WrongActorPhaseInstanceAndZoneRejectAtomically()
    {
        var engine = Setup();
        var c = Command(engine);
        Reject(engine, c with { Player = PlayerId.Two }, DuelError.NotActivePlayer);
        Reject(engine, new TributeSummon(PlayerId.One, engine.State.Revision, -1, 0, c.Tributes), DuelError.CardNotInHand);
        Reject(engine, Command(engine, zone: -1), DuelError.InvalidMonsterZone);
        Reject(engine, Command(engine, zone: 5), DuelError.InvalidMonsterZone);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Reject(engine, Command(engine), DuelError.WrongPhase);
    }

    [Fact]
    public void InsufficientFieldDoesNotConsumeTheAllowance()
    {
        var engine = Setup(donors: 0);
        Reject(engine, Command(engine, tributes: []), DuelError.InvalidTributeSelection);
        Assert.False(engine.State.NormalSummonUsed);
    }

    private static TriggerEffectProfile Grave(string id, TriggerTiming timing, TriggerRequirement requirement = TriggerRequirement.Optional) =>
        new(id, Low, MonsterEventKind.SentToGraveyard, EffectRelation.Self, requirement,
            TriggerOperation.GainLifePoints, lifePointGain: 100, sourceLocation: TriggerSourceLocation.GraveyardSelf, timing: timing);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TributeIsNotDestructionAndGraveyardWhenMissesTheLaterSummonOrSet(bool set)
    {
        var destroyed = new TriggerEffectProfile("not-destroyed", Low, MonsterEventKind.Destroyed, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 1000,
            sourceLocation: TriggerSourceLocation.DestroyedSelf);
        var summoned = new TriggerEffectProfile("summon", High, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Optional, TriggerOperation.GainLifePoints, lifePointGain: 50, timing: TriggerTiming.When);
        var engine = Setup(triggers: [Grave("if", TriggerTiming.If), Grave("when", TriggerTiming.When),
            Grave("mandatory", TriggerTiming.When, TriggerRequirement.Mandatory), destroyed, summoned]);
        var result = Ok(engine, Command(engine, set: set));
        Assert.Equal("when", Assert.Single(result.Events.OfType<TriggerMissedTiming>()).Trigger.Profile.EffectId);
        Assert.DoesNotContain(engine.State.EventEffects.PendingTriggers!.Remaining, t => t.Profile.EffectId == "not-destroyed");
        Assert.Equal(set ? 2 : 3, engine.State.EventEffects.PendingTriggers.Remaining.Count);
        Reject(engine, Command(engine), DuelError.TriggerOrderPending);
        Drain(engine);
        Assert.Equal(set ? 8200 : 8250, engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void SimultaneousTributesShareATimingPointAndDoNotBecomeActivationCosts()
    {
        var engine = Setup(7, 2, triggers: [Grave("if", TriggerTiming.If)]);
        Ok(engine, Command(engine, tributes: [Binding(engine, 1), Binding(engine, 0)]));
        var triggers = engine.State.EventEffects.PendingTriggers!.Remaining;
        Assert.Equal(2, triggers.Count);
        Assert.Single(triggers.Select(t => t.Occurrence.TimingPoint).Distinct());
        Assert.All(triggers, t => Assert.False(t.Occurrence.IsActivationCost));
        Drain(engine);
        Assert.Equal(8200, engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void RemovingAnAuraAndNegatorRecomputesTheNewPlacements()
    {
        var engine = Setup(continuous: [new("low-aura", Low, EffectRelation.Controller, 100, 0),
                new("high-aura", High, EffectRelation.Self, 500, 0), new("other-aura", OtherCard, EffectRelation.Self, 300, 0)],
            negations: [new("low-negates-other", Low, EffectRelation.Controller, [OtherCard])]);
        int other = engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == OtherCard).InstanceId;
        Ok(engine, new NormalSummon(PlayerId.One, other, 2));
        NextMain(engine); NextMain(engine);
        var before = engine.State;
        Assert.Equal(1100, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal(1100, engine.GetMonsterStatistics(PlayerId.One, 2)!.Attack);
        Assert.Single(before.EventEffects.NegatedMonsters);
        var result = Ok(engine, Command(engine));
        Assert.Equal(2, result.Events.OfType<StatModifierRemoved>().Count());
        Assert.Equal(1500, engine.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal(1300, engine.GetMonsterStatistics(PlayerId.One, 2)!.Attack);
        Assert.Empty(engine.State.EventEffects.NegatedMonsters);
        Assert.False(BindingFrom(before, 0).IsStillPresent(engine.State));
        Assert.Equal(Low, before.GetPlayer(PlayerId.One).MonsterZones[0]!.Card.GameCardId);
    }

    private static MonsterBinding BindingFrom(DuelState state, int zone)
    {
        var m = state.GetPlayer(PlayerId.One).MonsterZones[zone]!;
        return new(PlayerId.One, zone, m.Card, m.PlacementId);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TributeSummonDoesNotBypassBattleGates(bool staged)
    {
        var engine = Setup(staged: staged);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Reject(engine, Command(engine), staged ? DuelError.BattleWindowPending : DuelError.WrongPhase);
    }

    [Fact]
    public void IdenticalCommandSequencesProduceIdenticalStateAndEvents()
    {
        var first = Setup(7, 2); var second = Setup(7, 2);
        var command = Command(first, tributes: [Binding(first, 1), Binding(first, 0)]);
        var a = Ok(first, command); var b = Ok(second, command);
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
    }

    [Fact]
    public void FieldGraveyardWitnessMustHaveBeenPresentBeforeTheTributeAndSurviveIt()
    {
        TriggerEffectProfile Witness(string id, uint card) => new(id, card, MonsterEventKind.SentToGraveyard,
            EffectRelation.Controller, TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100);
        var engine = Setup(donors: 2, triggers: [Witness("existing", Low), Witness("too-late", High)]);
        var result = Ok(engine, Command(engine));
        var trigger = Assert.Single(engine.State.EventEffects.PendingTriggers!.Remaining);
        Assert.Equal("existing", trigger.Profile.EffectId);
        Assert.Equal(1, trigger.Source.Zone);
        Assert.Single(result.Events.OfType<TriggerUnavailable>()); // The departing witness cannot activate on the field.
        Drain(engine);
        Assert.Equal(8100, engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void MainTwoUsesTheSameProcedureAndNewMonsterCannotChangePositionImmediately()
    {
        var engine = Setup();
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2));
        Ok(engine, Command(engine));
        Reject(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense), DuelError.PositionChangeNotAllowed);
    }

    [Fact]
    public void TributeCommandsCannotBypassPendingSelections()
    {
        var sequence = new DecisionSequence("select", [DecisionStep.Card(DecisionCardSource.Hand, [High])]);
        var engine = Setup(sequences: [sequence]);
        Ok(engine, new BeginDecisionSequence(PlayerId.One, sequence.Id));
        Reject(engine, Command(engine), DuelError.DecisionPending);
    }

    [Fact]
    public void TributeCommandsCannotBypassChains()
    {
        var engine = Setup(triggers: [Grave("if", TriggerTiming.If)]);
        Ok(engine, Command(engine));
        Order(engine);
        Assert.NotNull(engine.State.PendingChain);
        Reject(engine, Command(engine), DuelError.ChainPending);
    }
}
