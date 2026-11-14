using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Xyz Summon tests with authored profiles.</summary>
public sealed class XyzSummonTests
{
    private const uint Material = 100_001, WrongLevel = 100_002, Target = uint.MaxValue, Negator = 100_003, Counter = 100_004, GenericTarget = 100_005;

    private static DuelEngine Make(bool window = false, int detach = 0, bool grave = false,
        ResponseNegation? response = null, bool aura = false, bool staged = false, int count = 2, bool genericWhen = false)
    {
        uint[] ids = [Material, WrongLevel, Target, Negator, Counter, GenericTarget];
        var catalog = new CardCatalog(new(null, "synthetic-xyz"), ids.Select(id =>
            new CardDefinition(id, $"{id}", "", new(id == WrongLevel ? 3000 : 1000, 1000,
                id == Target ? 0u : id == WrongLevel ? 3u : 4u))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(
            id is Negator or Counter ? CardCategory.Trap : CardCategory.Monster,
            AllowsNormalSummon: id is Material or WrongLevel, AllowsBasicBattle: id is Material or WrongLevel or Target,
            AllowsSpellTrapSet: id is Negator or Counter, AllowsExtraDeck: id is Target or GenericTarget,
            AllowsSummonMaterial: id is Material or WrongLevel or Target));
        List<TriggerEffectProfile> triggers = [];
        if (genericWhen)
        {
            triggers.Add(new("generic-when-summoned", GenericTarget, MonsterEventKind.SpecialSummoned, EffectRelation.Self,
                TriggerRequirement.Optional, TriggerOperation.GainLifePoints, lifePointGain: 700, timing: TriggerTiming.When));
            triggers.Add(new("generic-grave-watcher", GenericTarget, MonsterEventKind.SentToGraveyard, EffectRelation.Controller,
                TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 2000));
        }
        if (detach != 0) triggers.Add(new("detach", Target, MonsterEventKind.SpecialSummoned, EffectRelation.Self,
            TriggerRequirement.Optional, TriggerOperation.GainLifePoints, lifePointGain: 500, detachMaterialCount: detach));
        if (grave) triggers.Add(new("grave", Material, MonsterEventKind.SentToGraveyard, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100,
            sourceLocation: TriggerSourceLocation.GraveyardSelf));
        uint[] first = [Material, Material, Material, Material, WrongLevel, Counter, .. Enumerable.Repeat(Material, 34)];
        uint[] second = [WrongLevel, Negator, Counter, Material, Material, .. Enumerable.Repeat(Material, 35)];
        return new(catalog, rules, first, second, playerOneExtraDeck: [Target, Target, GenericTarget],
            experimentalSpecialSummonCards: [new(Target, SpecialSummonSources.Graveyard, "xyz"), new(GenericTarget, SpecialSummonSources.None)],
            experimentalSpecialSummonProcedures: [new("revive", [Target], DecisionCardSource.Graveyard)],
            experimentalMaterialProcedures: [new("xyz", [Target], [Material, WrongLevel, Target], count,
                MaterialDestination.Attached, xyz: new(4)), new("generic", [GenericTarget], [Material, Target], 2)],
            experimentalSummonWindows: window ? [new(Target)] : null,
            experimentalTriggerEffects: triggers,
            effectDefinitions: [.. window ? [ResponseProbe.NegateSummon("negate-summon", Negator, 0, SummonKinds.Special)] : Array.Empty<EffectDefinition>(),
                .. response is { } op ? [ResponseProbe.Negate("counter", Counter, op, ChainSpellSpeed.Three)] : Array.Empty<EffectDefinition>()],
            experimentalContinuousEffects: aura ? [new("aura", Material, EffectRelation.Controller, 100, 0)] : null,
            experimentalBattleFlow: staged);
    }

    private static MonsterBinding Bound(DuelEngine e, int zone)
    {
        var monster = e.State.GetPlayer(PlayerId.One).MonsterZones[zone]!;
        return new(PlayerId.One, zone, monster.Card, monster.PlacementId);
    }

    private static void Prepare(DuelEngine e, bool wrong = false, bool faceDown = false, bool responses = false, int count = 2)
    {
        EnterMain(e);
        for (int zone = 0; zone < count; zone++)
        {
            if (zone != 0) { NextMain(e); NextMain(e); }
            var id = wrong && zone == 1 ? WrongLevel : Material;
            var card = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == id);
            Ok(e, faceDown && zone == 0 ? new SetMonster(PlayerId.One, card.InstanceId, zone) :
                new NormalSummon(PlayerId.One, card.InstanceId, zone));
        }
        if (responses)
        {
            Set(e, Counter); NextMain(e); Set(e, Negator, 0); Set(e, Counter, 1); NextMain(e);
        }
    }

    private static SpecialSummonWithMaterials Command(DuelEngine e, params int[] zones) =>
        new(PlayerId.One, e.State.Revision, "xyz", e.State.GetPlayer(PlayerId.One).ExtraDeck[0].InstanceId,
            0, (zones.Length == 0 ? new[] { 0, 1 } : zones).Select(z => Bound(e, z)));

    [Fact]
    public void EqualLevelsAttachInZoneOrderWithoutGraveyardEventsAndRemoveAuras()
    {
        var e = Make(grave: true, aura: true); Prepare(e);
        var before = e.State; var json = JsonSerializer.Serialize(before); var selected = new[] { Bound(e, 1), Bound(e, 0) };
        var result = Ok(e, Command(e, 1, 0));
        var host = e.State.GetPlayer(PlayerId.One).MonsterZones[0]!;
        Assert.Equal(selected.Reverse().Select(m => m.Card), host.XyzMaterials);
        Assert.Equal(json, JsonSerializer.Serialize(before));
        Assert.Empty(e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Empty(result.Events.OfType<SummonMaterialSentToGraveyard>());
        Assert.Empty(result.Events.OfType<MonsterDestroyed>());
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Equal(new[] { 0, 1 }, result.Events.OfType<XyzMaterialAttached>().Select(x => x.Material.Zone));
        Assert.Equal(1000, e.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal("xyz", e.State.SummonHistory.Single(h => h.Card == host.Card).ProperProcedureId);
        Assert.True(e.State.NormalSummonUsed); // The prior Normal Summon allowance is preserved.
        Assert.Throws<NotSupportedException>(() => ((IList<CardInstance>)host.XyzMaterials).Clear());
    }

    [Fact]
    public void WrongLevelsFaceDownDuplicateStaleAndWrongActorSelectionsRejectAtomically()
    {
        var wrong = Make(); Prepare(wrong, wrong: true);
        Reject(wrong, Command(wrong), DuelError.InvalidSummonMaterials);
        var down = Make(); Prepare(down, faceDown: true);
        Reject(down, Command(down), DuelError.SummonMaterialNotAllowed);
        var e = Make(); Prepare(e);
        Reject(e, Command(e, 0, 0), DuelError.InvalidSummonMaterials);
        Reject(e, Command(e, 0), DuelError.InvalidSummonMaterials);
        var command = Command(e); Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Main2));
        Reject(e, command, DuelError.StaleSummonSelection);
        Reject(e, new SpecialSummonWithMaterials(PlayerId.Two, e.State.Revision, "xyz", command.CardInstanceId, 0, command.Materials), DuelError.NotActivePlayer);
        Ok(e, Command(e));
    }

    [Fact]
    public void RankIsNotALevelAndExistingAttachmentsDoNotTransferToANewSummon()
    {
        var e = Make(); Prepare(e); Ok(e, Command(e));
        NextMain(e); NextMain(e); Summon(e, Material, 1);
        Reject(e, Command(e), DuelError.InvalidSummonMaterials);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PassingSummonWindowPreservesAttachmentsAndCounterNegationDoesToo(bool counter)
    {
        var e = Make(window: true, response: ResponseNegation.Activation); Prepare(e, responses: true);
        Ok(e, Command(e)); var pending = e.State.PendingSummon!;
        Assert.Equal(2, pending.XyzMaterials.Count); Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Empty(e.State.GetPlayer(PlayerId.One).Graveyard);
        if (counter)
        {
            Ok(e, new ActivateSummonNegation(PlayerId.Two, pending.DecisionId, 0));
            Ok(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 0));
            Pass(e); Pass(e); Resolve(e); Resolve(e);
        }
        else
        {
            Ok(e, new PassSummonResponse(pending.Responder, pending.DecisionId));
            pending = e.State.PendingSummon!; Ok(e, new PassSummonResponse(pending.Responder, pending.DecisionId));
        }
        Assert.Equal(2, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.XyzMaterials.Count);
        Assert.DoesNotContain(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == Material);
    }

    [Fact]
    public void NegatedSummonSendsHostAndAttachmentsToGraveyardWithoutQualification()
    {
        var e = Make(window: true); Prepare(e, responses: true);
        Ok(e, Command(e)); var pending = e.State.PendingSummon!;
        Ok(e, new ActivateSummonNegation(PlayerId.Two, pending.DecisionId, 0)); Pass(e); Pass(e);
        var result = Resolve(e);
        Assert.Equal(2, result.Events.OfType<XyzMaterialSentToGraveyard>().Count());
        Assert.Equal(3, e.State.GetPlayer(PlayerId.One).Graveyard.Count);
        Assert.Null(e.State.PendingSummon); Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Reject(e, new SpecialSummonByProcedure(PlayerId.One, e.State.Revision, "revive", pending.Card.InstanceId, 0), DuelError.ProperSummonRequired);
    }

    [Fact]
    public void DetachChoiceRejectsInvalidSelectionsThenPaysCostAndQueuesGraveyardTriggers()
    {
        var e = Make(detach: 1, grave: true); Prepare(e); Ok(e, Command(e)); Order(e);
        var pending = e.State.EventEffects.PendingCommitment!;
        Assert.Equal(TriggerCommitmentKind.DetachCost, pending.Kind);
        var id = pending.CardOptions[1].InstanceId;
        Reject(e, new ChooseTriggerXyzMaterials(PlayerId.One, pending.DecisionId - 1, [id]), DuelError.StaleTriggerDecision);
        Reject(e, new ChooseTriggerXyzMaterials(PlayerId.Two, pending.DecisionId, [id]), DuelError.NotTriggerPlayer);
        Reject(e, new ChooseTriggerXyzMaterials(PlayerId.One, pending.DecisionId, []), DuelError.InvalidTriggerCommitment);
        Reject(e, new ChooseTriggerXyzMaterials(PlayerId.One, pending.DecisionId, [id, id]), DuelError.InvalidTriggerCommitment);
        Reject(e, new ChooseTriggerXyzMaterials(PlayerId.One, pending.DecisionId, [int.MaxValue]), DuelError.InvalidTriggerCommitment);
        var before = e.State; var json = JsonSerializer.Serialize(before);
        var result = Ok(e, new ChooseTriggerXyzMaterials(PlayerId.One, pending.DecisionId, [id]));
        Assert.Equal(json, JsonSerializer.Serialize(before));
        Assert.True(Assert.Single(result.Events.OfType<XyzMaterialSentToGraveyard>()).IsCost);
        Assert.Single(e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.XyzMaterials);
        Assert.Equal(id, Assert.Single(e.State.GetPlayer(PlayerId.One).Graveyard).InstanceId);
        Assert.Single(e.State.PendingChain!.Links[0].Trigger!.DetachedCost);
        Drain(e); Assert.Equal(8600, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Theory]
    [InlineData(ResponseNegation.Activation)] [InlineData(ResponseNegation.Effect)]
    public void NegatingTriggerDoesNotRefundDetachedCost(ResponseNegation operation)
    {
        var e = Make(detach: 1, response: operation); Prepare(e, responses: true); Ok(e, Command(e)); Order(e);
        var pending = e.State.EventEffects.PendingCommitment!;
        Ok(e, new ChooseTriggerXyzMaterials(PlayerId.One, pending.DecisionId, [pending.CardOptions[0].InstanceId]));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 1));
        Drain(e);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Single(e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Single(e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.XyzMaterials);
    }

    [Fact]
    public void UnpayableAndDeclinedDetachTriggersDoNotSpendMaterials()
    {
        var e = Make(detach: 3); Prepare(e); Ok(e, Command(e));
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Equal(2, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.XyzMaterials.Count);
        var declined = Make(detach: 1); Prepare(declined); Ok(declined, Command(declined)); Order(declined, decline: true);
        Assert.Null(declined.State.EventEffects.PendingCommitment);
        Assert.Equal(2, declined.State.GetPlayer(PlayerId.One).MonsterZones[0]!.XyzMaterials.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HostBattleDepartureReleasesMaterialsAndRevivalDoesNotRestoreThem(bool staged)
    {
        var e = Make(grave: true, staged: staged); Prepare(e); Ok(e, Command(e));
        var host = e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card;
        NextMain(e); Summon(e, WrongLevel); Ok(e, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        if (staged) BattleFlowScenario.Ready(e);
        var attack = Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        List<DuelEvent> events = [.. attack.Events];
        if (staged)
        {
            for (int i = 0; i < 40 && e.State.Battle is not null; i++)
            {
                Drain(e);
                if (e.State.Battle is not { } battle) break;
                var w = battle.Window;
                DuelCommand command = !w.Closed
                    ? new PassBattleResponse(w.Responder, w.DecisionId) : battle.Step == BattleStep.End
                        ? new ChangePhase(PlayerId.Two, DuelPhase.Main2) : new AdvanceBattle(PlayerId.Two, w.DecisionId);
                events.AddRange(Ok(e, command).Events);
            }
        }
        Assert.Equal(2, events.OfType<XyzMaterialSentToGraveyard>().Count());
        Drain(e); Assert.Equal(3, e.State.GetPlayer(PlayerId.One).Graveyard.Count);
        Assert.Equal(6200, e.State.GetPlayer(PlayerId.One).LifePoints);
        NextMain(e); Ok(e, new SpecialSummonByProcedure(PlayerId.One, e.State.Revision, "revive", host.InstanceId, 0));
        Assert.Empty(e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.XyzMaterials);
    }

    [Fact]
    public void FiveMaterialsFreeASlotOnAFullFieldAndSurviveTheSummonWindowImmutably()
    {
        var e = Make(window: true, count: 5); Prepare(e, count: 5);
        var before = e.State; var beforeJson = JsonSerializer.Serialize(before);
        var cards = before.GetPlayer(PlayerId.One).MonsterZones.Select(m => m!.Card).ToArray();
        Ok(e, Command(e, 4, 2, 0, 3, 1));
        var attempted = e.State; var attemptedJson = JsonSerializer.Serialize(attempted);
        Assert.All(attempted.GetPlayer(PlayerId.One).MonsterZones, Assert.Null);
        Assert.Equal(cards, attempted.PendingSummon!.XyzMaterials);
        for (int pass = 0; pass < 2; pass++)
        {
            var pending = e.State.PendingSummon!;
            Ok(e, new PassSummonResponse(pending.Responder, pending.DecisionId));
        }
        Assert.Equal(cards, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.XyzMaterials);
        Assert.Empty(e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Equal(beforeJson, JsonSerializer.Serialize(before));
        Assert.Equal(attemptedJson, JsonSerializer.Serialize(attempted));
    }

    [Fact]
    public void UsingHostAsGenericMaterialReleasesAttachmentsWithoutTransferringThem()
    {
        var e = Make(genericWhen: true); Prepare(e, count: 3); Ok(e, Command(e));
        var before = e.State; var beforeJson = JsonSerializer.Serialize(before);
        var host = Bound(e, 0);
        var attached = before.GetPlayer(PlayerId.One).MonsterZones[0]!.XyzMaterials;
        var other = Bound(e, 2);
        var result = Ok(e, new SpecialSummonWithMaterials(PlayerId.One, before.Revision, "generic",
            before.GetPlayer(PlayerId.One).ExtraDeck.Single(c => c.GameCardId == GenericTarget).InstanceId, 0, [other, host]));
        Assert.Equal(attached, result.Events.OfType<XyzMaterialSentToGraveyard>().Select(x => x.Material));
        Assert.Equal(new[] { host.Card, other.Card }, result.Events.OfType<SummonMaterialSentToGraveyard>().Select(x => x.Material.Card));
        Assert.Empty(e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.XyzMaterials);
        Assert.Equal(4, e.State.GetPlayer(PlayerId.One).Graveyard.Count);
        Assert.Equal(beforeJson, JsonSerializer.Serialize(before));
        Assert.True(result.Events.ToList().FindLastIndex(x => x is XyzMaterialSentToGraveyard) <
            result.Events.ToList().FindIndex(x => x is MonsterSpecialSummoned));
        Assert.Equal("generic-when-summoned", Assert.Single(e.State.EventEffects.PendingTriggers!.Options).Profile.EffectId);
        Drain(e); Assert.Equal(8700, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void ProfileValidationAndDeterministicReplay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new XyzMaterialRequirements(0));
        Assert.Throws<ArgumentException>(() => new ExtraDeckMaterialProcedureProfile("x", [Target], [Material], 2, xyz: new(4)));
        Assert.Throws<ArgumentException>(() => new ExtraDeckMaterialProcedureProfile("x", [Target], [Material], 2, MaterialDestination.Attached));
        Assert.Throws<ArgumentException>(() => new ExtraDeckMaterialProcedureProfile("x", [Target], [Material], 1, MaterialDestination.Attached, xyz: new(4)));
        string Run() { var e = Make(detach: 2); Prepare(e); Ok(e, Command(e)); Drain(e); return JsonSerializer.Serialize(e.State); }
        Assert.Equal(Run(), Run());
    }
}
