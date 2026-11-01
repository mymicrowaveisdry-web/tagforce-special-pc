using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Generic authored material procedures; no card-family or PSP verification claim.</summary>
public sealed class ExtraDeckMaterialTests
{
    private const uint Material = uint.MaxValue - 1, Other = 100_000, Target = uint.MaxValue, OtherTarget = 100_001;
    private const uint Negate = 100_002, Counter = 100_003;

    private static DuelEngine Engine(int materialCount = 2, MaterialDestination destination = MaterialDestination.Graveyard,
        bool window = false, bool faceUp = true, IEnumerable<TriggerEffectProfile>? triggers = null,
        IEnumerable<ContinuousEffectProfile>? continuous = null, IEnumerable<ChainResponseProfile>? responses = null,
        IEnumerable<uint>? extraOne = null, IEnumerable<uint>? extraTwo = null,
        IEnumerable<ExtraDeckMaterialProcedureProfile>? procedures = null)
    {
        uint[] ids = [Material, Other, Target, OtherTarget, Negate, Counter];
        var catalog = new CardCatalog(new(null, "synthetic-extra-deck"), ids.Select(id =>
            new CardDefinition(id, $"Probe {id}", "", new(id == Other ? 3000 : 1000, 1000, 4))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(id is Negate or Counter ? CardCategory.Trap : CardCategory.Monster,
            AllowsNormalSummon: id is Material or Other, AllowsBasicBattle: id is Material or Other or Target or OtherTarget,
            AllowsSpellTrapSet: id is Negate or Counter, AllowsExtraDeck: id is Target or OtherTarget,
            AllowsSummonMaterial: id is Material or Other));
        uint[] first = [Material, Material, Material, Material, Material, Counter, Other, .. Enumerable.Repeat(Other, 33)];
        uint[] second = [Other, Negate, Counter, Other, Other, .. Enumerable.Repeat(Other, 35)];
        return new(catalog, rules, first, second,
            playerOneExtraDeck: extraOne ?? [Target, Target, OtherTarget], playerTwoExtraDeck: extraTwo ?? [Target],
            experimentalSpecialSummonCards: [new(Target, SpecialSummonSources.Graveyard, "material"), new(OtherTarget)],
            experimentalSpecialSummonProcedures: [new("revive", [Target], DecisionCardSource.Graveyard)],
            experimentalMaterialProcedures: procedures ?? [new("material", [Target], [Material], materialCount, destination, faceUp)],
            experimentalSummonWindows: window ? [new(Target)] : null,
            experimentalSummonNegations: [new("summon-negator", Negate, 500, SummonNegationKinds.Special)],
            experimentalChainResponses: responses, experimentalTriggerEffects: triggers, experimentalContinuousEffects: continuous);
    }

    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(e.State.ActivePlayer).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Prepare(DuelEngine e, int count = 2, bool setFirst = false, bool setResponses = false)
    {
        EnterMain(e);
        for (int zone = 0; zone < count; zone++)
        {
            if (zone > 0) { NextMain(e); NextMain(e); }
            int card = Hand(e, Material);
            Ok(e, setFirst && zone == 0 ? new SetMonster(PlayerId.One, card, zone) : new NormalSummon(PlayerId.One, card, zone));
            Drain(e);
        }
        // Counter is drawn at the start of player one's second turn.
        if (setResponses && count >= 2) Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, Counter), 0));
        NextMain(e);
        if (setResponses) Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Negate), 0));
        NextMain(e);
    }
    private static MonsterBinding Bound(DuelEngine e, int zone)
    {
        var m = e.State.GetPlayer(PlayerId.One).MonsterZones[zone]!;
        return new(PlayerId.One, zone, m.Card, m.PlacementId);
    }
    private static SpecialSummonWithMaterials Command(DuelEngine e, int[]? zones = null, int destination = 0,
        uint target = Target, string procedure = "material", BattlePosition position = BattlePosition.FaceUpAttack) =>
        new(PlayerId.One, e.State.Revision, procedure,
            e.State.GetPlayer(PlayerId.One).ExtraDeck.First(c => c.GameCardId == target).InstanceId,
            destination, (zones ?? [0, 1]).Select(z => Bound(e, z)), position);
    private static void PassSummon(DuelEngine e)
    {
        var pending = e.State.PendingSummon!; Ok(e, new PassSummonResponse(pending.Responder, pending.DecisionId));
    }
    private static TriggerEffectProfile Grave(string id, TriggerTiming timing) =>
        new(id, Material, MonsterEventKind.SentToGraveyard, EffectRelation.Self, TriggerRequirement.Optional,
            TriggerOperation.GainLifePoints, lifePointGain: 100, sourceLocation: TriggerSourceLocation.GraveyardSelf, timing: timing);

    [Fact]
    public void SetupKeepsExtraDeckInstancesSeparateOwnedAndImmutable()
    {
        uint[] supplied = [Target, Target];
        var e = Engine(extraOne: supplied);
        supplied[0] = OtherTarget;
        var first = e.State.GetPlayer(PlayerId.One); var second = e.State.GetPlayer(PlayerId.Two);
        Assert.Equal(new uint[] { Target, Target }, first.ExtraDeck.Select(c => c.GameCardId));
        Assert.All(first.ExtraDeck, c => Assert.Equal(PlayerId.One, c.Owner));
        Assert.Equal(PlayerId.Two, Assert.Single(second.ExtraDeck).Owner);
        Assert.Equal(83, e.State.Players.SelectMany(p => p.Hand.Concat(p.Deck).Concat(p.ExtraDeck))
            .Select(c => c.InstanceId).Distinct().Count());
        Assert.DoesNotContain(first.Hand, c => c.GameCardId == Target);
        Assert.Throws<NotSupportedException>(() => ((IList<CardInstance>)first.ExtraDeck).Clear());
    }

    [Theory]
    [InlineData(BattlePosition.FaceUpAttack)] [InlineData(BattlePosition.FaceUpDefense)]
    public void MaterialsMoveToOwnerGraveyardAndFreshTargetUsesFreedSlot(BattlePosition position)
    {
        var e = Engine(); Prepare(e); var before = e.State; string snapshot = JsonSerializer.Serialize(before);
        var chosen = new[] { Bound(e, 1), Bound(e, 0) };
        int target = before.GetPlayer(PlayerId.One).ExtraDeck[0].InstanceId;
        var command = new SpecialSummonWithMaterials(PlayerId.One, before.Revision, "material", target, 1, chosen, position);
        chosen[0] = chosen[0] with { Zone = 4 };
        Assert.Throws<NotSupportedException>(() => ((IList<MonsterBinding>)command.Materials).Clear());
        var result = Ok(e, command); var player = e.State.GetPlayer(PlayerId.One);
        Assert.Equal(snapshot, JsonSerializer.Serialize(before));
        Assert.Equal(2, player.ExtraDeck.Count); Assert.DoesNotContain(player.ExtraDeck, c => c.InstanceId == target);
        Assert.Null(player.MonsterZones[0]); Assert.Equal(target, player.MonsterZones[1]!.Card.InstanceId);
        Assert.Equal(position, player.MonsterZones[1]!.Position); Assert.Equal(e.State.Revision, player.MonsterZones[1]!.PlacementId);
        Assert.Equal(new[] { 0, 1 }, result.Events.OfType<SummonMaterialSentToGraveyard>().Select(x => x.Material.Zone));
        Assert.Collection(result.Events, x => Assert.IsType<SummonMaterialSentToGraveyard>(x),
            x => Assert.IsType<SummonMaterialSentToGraveyard>(x), x => Assert.IsType<MonsterSpecialSummoned>(x));
        Assert.Equal(2, player.Graveyard.Count); Assert.All(player.Graveyard, c => Assert.Equal(PlayerId.One, c.Owner));
        Assert.DoesNotContain(result.Events, x => x is MonsterDestroyed or MonsterTributed);
        Assert.Equal(DecisionCardSource.ExtraDeck, Assert.Single(result.Events.OfType<MonsterSpecialSummoned>()).Source);
        var record = Assert.Single(e.State.SummonHistory, h => h.Card.InstanceId == target);
        Assert.Equal(SummonMethod.SpecialProcedure, record.Method); Assert.Equal("material", record.ProperProcedureId);
        Assert.Equal(DecisionCardSource.ExtraDeck, record.Source); Assert.False(e.State.NormalSummonUsed);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, Other), 2));
    }

    [Fact]
    public void BanishMaterialsAreNeitherGraveyardMovementNorDestruction()
    {
        var e = Engine(destination: MaterialDestination.Banished, triggers: [Grave("grave", TriggerTiming.If)]); Prepare(e);
        var result = Ok(e, Command(e)); var p = e.State.GetPlayer(PlayerId.One);
        Assert.Equal(2, p.Banished.Count); Assert.Empty(p.Graveyard);
        Assert.Equal(2, result.Events.OfType<SummonMaterialBanished>().Count());
        Assert.Empty(result.Events.OfType<SummonMaterialSentToGraveyard>());
        Assert.DoesNotContain(result.Events, x => x is MonsterDestroyed or MonsterTributed);
        Assert.Null(e.State.EventEffects.PendingTriggers);
    }

    [Fact]
    public void AllInvalidSelectionsPreserveStateAndEvents()
    {
        var e = Engine(); Prepare(e); var a = Bound(e, 0); var b = Bound(e, 1); var command = Command(e);
        Reject(e, new SpecialSummonWithMaterials(PlayerId.Two, e.State.Revision, "material", command.CardInstanceId, 0, [a, b]), DuelError.NotActivePlayer);
        Reject(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision - 1, "material", command.CardInstanceId, 0, [a, b]), DuelError.StaleSummonSelection);
        Reject(e, Command(e, procedure: "unknown"), DuelError.UnsupportedMaterialProcedure);
        Reject(e, Command(e, target: OtherTarget), DuelError.UnsupportedMaterialProcedure);
        Reject(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, "material", -1, 0, [a, b]), DuelError.SpecialSummonSourceUnavailable);
        int opponentTarget = e.State.GetPlayer(PlayerId.Two).ExtraDeck[0].InstanceId;
        Reject(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, "material", opponentTarget, 0, [a, b]), DuelError.SpecialSummonSourceUnavailable);
        Reject(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, "material", command.CardInstanceId, -1, [a, b]), DuelError.InvalidMonsterZone);
        Reject(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, "material", command.CardInstanceId, 0, [a, b], BattlePosition.FaceDownDefense), DuelError.InvalidBattlePosition);
        Reject(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, "material", command.CardInstanceId, 0, []), DuelError.InvalidSummonMaterials);
        Reject(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, "material", command.CardInstanceId, 0, [a, a]), DuelError.InvalidSummonMaterials);
        Reject(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, "material", command.CardInstanceId, 0, [a, null!]), DuelError.InvalidSummonMaterials);
        foreach (var invalid in new[] { b with { Player = PlayerId.Two }, b with { Zone = 5 },
            b with { Zone = 4 }, b with { PlacementId = b.PlacementId + 1 }, b with { Card = a.Card } })
            Reject(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, "material", command.CardInstanceId, 0, [a, invalid]), DuelError.InvalidSummonMaterials);
        Ok(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense));
        Reject(e, command, DuelError.StaleSummonSelection);
        Ok(e, Command(e));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FaceDownMaterialRequiresExplicitProcedurePermission(bool allowed)
    {
        var e = Engine(faceUp: !allowed); Prepare(e, setFirst: true);
        if (allowed) Ok(e, Command(e));
        else Reject(e, Command(e), DuelError.SummonMaterialNotAllowed);
    }

    [Fact]
    public void MaterialAllowlistRestrictsOtherwiseEligibleMonsters()
    {
        var e = Engine(); Prepare(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, Other), 2));
        NextMain(e); NextMain(e);
        Reject(e, Command(e, [0, 2]), DuelError.SummonMaterialNotAllowed);
        Ok(e, Command(e));
    }

    [Fact]
    public void FullFieldCanReuseSelectedMaterialSlotButNotAnotherOccupiedSlot()
    {
        var e = Engine(); Prepare(e, count: 5);
        Reject(e, Command(e, [0, 4], 3), DuelError.MonsterZoneOccupied);
        Ok(e, Command(e, [4, 0], 4));
        Assert.Equal(4, e.State.GetPlayer(PlayerId.One).MonsterZones.Count(m => m is not null));
    }

    [Fact]
    public void MaterialProcedureDoesNotAlterAnAlreadySpentNormalAllowance()
    {
        var e = Engine(); Prepare(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, Other), 2));
        Assert.True(e.State.NormalSummonUsed); Ok(e, Command(e)); Assert.True(e.State.NormalSummonUsed);
    }

    [Fact]
    public void GraveTriggersAreSimultaneousAndOptionalWhenMissesTheLaterSummon()
    {
        var summoned = new TriggerEffectProfile("summoned", Target, MonsterEventKind.SpecialSummoned,
            EffectRelation.Self, TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 50);
        var e = Engine(triggers: [Grave("if", TriggerTiming.If), Grave("when", TriggerTiming.When), summoned]); Prepare(e);
        var result = Ok(e, Command(e));
        Assert.Equal(2, result.Events.OfType<TriggerMissedTiming>().Count());
        Assert.Equal(3, e.State.EventEffects.PendingTriggers!.Remaining.Count);
        Drain(e); Assert.Equal(8250, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void MaterialDepartureRemovesContinuousSourcesBeforeTheNewPlacement()
    {
        var e = Engine(continuous: [new("aura", Material, EffectRelation.Controller, 100, 0)]); Prepare(e);
        Assert.Equal(1200, e.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        var result = Ok(e, Command(e));
        Assert.Equal(4, result.Events.OfType<StatModifierRemoved>().Count());
        Assert.Equal(1000, e.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Empty(e.State.EventEffects.AppliedModifiers);
    }

    [Fact]
    public void QualifiedExtraDeckInstanceCanBeRevivedAfterBattleDestruction()
    {
        var e = Engine(); Prepare(e); int target = Command(e).CardInstanceId; Ok(e, Command(e));
        var first = Assert.Single(e.State.SummonHistory, r => r.Card.InstanceId == target);
        NextMain(e); Ok(e, new NormalSummon(PlayerId.Two, Hand(e, Other), 0));
        Ok(e, new ChangePhase(PlayerId.Two, DuelPhase.Battle)); Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        NextMain(e);
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.InstanceId == target);
        Ok(e, new SpecialSummonByProcedure(PlayerId.One, e.State.Revision, "revive", target, 0));
        var revived = Assert.Single(e.State.SummonHistory, r => r.Card.InstanceId == target);
        Assert.Equal("material", revived.ProperProcedureId); Assert.Equal(DecisionCardSource.Graveyard, revived.Source);
        Assert.NotEqual(first.PlacementId, revived.PlacementId);
    }

    [Fact]
    public void NegatedAttemptLeavesMaterialsCommittedAndDoesNotQualifyForRevival()
    {
        var e = Engine(window: true); Prepare(e, setResponses: true); int target = Command(e).CardInstanceId;
        var begin = Ok(e, Command(e)); var pending = e.State.PendingSummon!;
        Assert.Equal(2, begin.Events.OfType<SummonMaterialSentToGraveyard>().Count());
        Assert.Equal(DecisionCardSource.ExtraDeck, pending.Source);
        Assert.Null(e.State.SummonHistory.Single(h => h.Card.InstanceId == target).ProperProcedureId);
        Ok(e, new ActivateSummonNegation(PlayerId.Two, pending.DecisionId, 0)); Pass(e); Pass(e);
        var resolved = Resolve(e);
        Assert.Single(resolved.Events.OfType<SummonNegated>());
        Assert.DoesNotContain(resolved.Events, x => x is MonsterSpecialSummoned);
        Assert.Equal(3, e.State.GetPlayer(PlayerId.One).Graveyard.Count);
        Assert.Equal(SummonStatus.Negated, e.State.SummonHistory.Single(h => h.Card.InstanceId == target).Status);
        Assert.Null(e.State.SummonHistory.Single(h => h.Card.InstanceId == target).ProperProcedureId);
        Reject(e, new SpecialSummonByProcedure(PlayerId.One, e.State.Revision, "revive", target, 0), DuelError.ProperSummonRequired);
    }

    [Fact]
    public void CounterNegationCompletesTheSameAttemptAndDefersMaterialTriggers()
    {
        var response = new ChainResponseProfile("counter", Counter, ChainOperation.NegateActivation, ChainSpellSpeed.Three);
        var e = Engine(window: true, responses: [response], triggers: [Grave("if", TriggerTiming.If)]); Prepare(e, setResponses: true);
        int target = Command(e).CardInstanceId; Ok(e, Command(e));
        var pending = e.State.PendingSummon!; Assert.Null(e.State.EventEffects.PendingTriggers);
        Ok(e, new ActivateSummonNegation(PlayerId.Two, pending.DecisionId, 0));
        Ok(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 0));
        Pass(e); Pass(e); Resolve(e); var resolved = Resolve(e);
        Assert.Single(resolved.Events.OfType<MonsterSpecialSummoned>());
        var record = e.State.SummonHistory.Single(h => h.Card.InstanceId == target);
        Assert.Equal(pending.AttemptId, record.AttemptId); Assert.Equal("material", record.ProperProcedureId);
        Assert.Equal(2, e.State.EventEffects.PendingTriggers!.Remaining.Count); Drain(e);
        Assert.Equal(8200, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void MainTwoAndDeterministicReplayUseTheSameMaterialContract()
    {
        DuelEngine Run()
        {
            var e = Engine(); Prepare(e); Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Battle));
            Reject(e, Command(e), DuelError.WrongPhase);
            Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Main2)); Ok(e, Command(e)); return e;
        }
        Assert.Equal(JsonSerializer.Serialize(Run().State), JsonSerializer.Serialize(Run().State));
    }

    [Fact]
    public void SetupAndProcedureValidationRejectUnsupportedInputs()
    {
        Assert.Empty(Engine(extraOne: []).State.GetPlayer(PlayerId.One).ExtraDeck);
        Assert.Equal(15, Engine(extraOne: Enumerable.Repeat(Target, 15)).State.GetPlayer(PlayerId.One).ExtraDeck.Count);
        Assert.Throws<ArgumentException>(() => Engine(extraOne: Enumerable.Repeat(Target, 16)));
        Assert.Throws<ArgumentException>(() => Engine(extraOne: [Material]));
        Assert.Throws<ArgumentException>(() => Engine(extraOne: [123456]));
        Assert.Throws<ArgumentException>(() => Engine(procedures: [new("bad", [Material], [Material], 1)]));
        Assert.Throws<ArgumentException>(() => Engine(procedures: [new("bad", [Target], [Negate], 1)]));
        Assert.Throws<ArgumentException>(() => Engine(procedures: [new("revive", [Target], [Material], 1)]));
        Assert.Throws<ArgumentException>(() => new ExtraDeckMaterialProcedureProfile("bad", [Target], [Material], 0));
        Assert.Throws<ArgumentException>(() => new ExtraDeckMaterialProcedureProfile("bad", [Target], [Material], 6));
        Assert.Throws<ArgumentException>(() => new ExtraDeckMaterialProcedureProfile("bad", [Target], [Material], 1, (MaterialDestination)10));
        uint[] ids = [Target]; uint[] materials = [Material];
        var p = new ExtraDeckMaterialProcedureProfile("copy", ids, materials, 1);
        ids[0] = OtherTarget; materials[0] = Other;
        Assert.Equal(Target, Assert.Single(p.TargetCardIds)); Assert.Equal(Material, Assert.Single(p.MaterialCardIds));
        Assert.Throws<NotSupportedException>(() => ((IList<uint>)p.MaterialCardIds).Clear());
    }

    [Fact]
    public void FiveMaterialsMoveAsOneGroupAndOnlyTheNewMonsterRemains()
    {
        var e = Engine(materialCount: 5); Prepare(e, count: 5);
        var result = Ok(e, Command(e, [4, 3, 2, 1, 0]));
        Assert.Equal(5, result.Events.OfType<SummonMaterialSentToGraveyard>().Count());
        Assert.Equal(5, e.State.GetPlayer(PlayerId.One).Graveyard.Count);
        Assert.Single(e.State.GetPlayer(PlayerId.One).MonsterZones, m => m is not null);
    }

    [Fact]
    public void WrongProperProcedureIsRejectedWithoutCommittingMaterials()
    {
        var e = Engine(procedures: [new("wrong", [Target], [Material], 2)]); Prepare(e);
        Reject(e, Command(e, procedure: "wrong"), DuelError.ProperSummonRequired);
        Assert.Empty(e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.NotNull(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.NotNull(e.State.GetPlayer(PlayerId.One).MonsterZones[1]);
    }
}
