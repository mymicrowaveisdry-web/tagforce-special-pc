using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

public sealed class SynchroSummonTests
{
    private const uint Tuner = 10001, NonTuner = 10002, Target = 10003, OtherTuner = 10004;

    private static DuelEngine Make(uint targetLevel = 7, uint tunerLevel = 3, bool window = false)
    {
        uint[] ids = [Tuner, NonTuner, Target, OtherTuner];
        var catalog = new CardCatalog(new(null, "synthetic-synchro"), ids.Select(id =>
            new CardDefinition(id, $"{id}", "", new(1000, 1000,
                id == Target ? targetLevel : id == Tuner ? tunerLevel : 2))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(CardCategory.Monster,
            AllowsNormalSummon: id != Target, AllowsBasicBattle: true,
            AllowsExtraDeck: id == Target, AllowsSummonMaterial: id != Target));
        uint[] deck = [Tuner, NonTuner, NonTuner, OtherTuner, NonTuner, .. Enumerable.Repeat(NonTuner, 35)];
        return new DuelEngine(catalog, rules, deck, deck, playerOneExtraDeck: [Target],
            experimentalSpecialSummonCards: [new(Target)],
            experimentalMaterialProcedures: [new("synchro", [Target], [Tuner, OtherTuner, NonTuner], 2,
                synchro: new([Tuner, OtherTuner], [NonTuner]))],
            experimentalSummonWindows: window ? [new(Target)] : null);
    }

    private static void Prepare(DuelEngine e, params uint[] ids)
    {
        EnterMain(e);
        for (int i = 0; i < ids.Length; i++)
        {
            if (i != 0) { NextMain(e); NextMain(e); }
            var card = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == ids[i]);
            Ok(e, new NormalSummon(PlayerId.One, card.InstanceId, i));
            Drain(e);
        }
    }

    private static SpecialSummonWithMaterials Command(DuelEngine e, params int[] zones)
    {
        var player = e.State.GetPlayer(PlayerId.One);
        return new(PlayerId.One, e.State.Revision, "synchro", player.ExtraDeck[0].InstanceId, 0,
            zones.Select(z => new MonsterBinding(PlayerId.One, z, player.MonsterZones[z]!.Card, player.MonsterZones[z]!.PlacementId)));
    }

    [Fact]
    public void OneTunerAndMultipleNonTunersWithExactTotalSummonAndMoveMaterials()
    {
        var e = Make(); Prepare(e, Tuner, NonTuner, NonTuner);
        var before = e.State; var snapshot = JsonSerializer.Serialize(before);
        var result = Ok(e, Command(e, 2, 0, 1));
        Assert.Equal(snapshot, JsonSerializer.Serialize(before));
        Assert.Equal(new[] { 0, 1, 2 }, result.Events.OfType<SummonMaterialSentToGraveyard>().Select(x => x.Material.Zone));
        Assert.Equal(3, e.State.GetPlayer(PlayerId.One).Graveyard.Count);
        Assert.Equal(Target, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card.GameCardId);
        Assert.Equal("synchro", Assert.Single(e.State.SummonHistory, h => h.Card.GameCardId == Target).ProperProcedureId);
    }

    [Fact]
    public void WrongTotalAndMissingTunerRejectWithoutChangingState()
    {
        var e = Make(); Prepare(e, Tuner, NonTuner, NonTuner);
        Reject(e, Command(e, 0, 1), DuelError.InvalidSummonMaterials);
        Reject(e, Command(e, 1, 2), DuelError.InvalidSummonMaterials);
        Ok(e, Command(e, 0, 1, 2));
    }

    [Fact]
    public void TwoTunersAndLevelLessTargetsAreRejected()
    {
        var e = Make(); Prepare(e, Tuner, OtherTuner, NonTuner);
        Reject(e, Command(e, 0, 1, 2), DuelError.InvalidSummonMaterials);
        // A zero-level target is also ineligible even if the total could otherwise match.
        var noTargetLevel = Make(targetLevel: 0); Prepare(noTargetLevel, Tuner, NonTuner);
        Reject(noTargetLevel, Command(noTargetLevel, 0, 1), DuelError.InvalidSummonMaterials);
    }

    [Fact]
    public void FaceDownMaterialAndDuplicateBindingAreRejected()
    {
        var e = Make(targetLevel: 5); EnterMain(e);
        var tuner = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Tuner);
        Ok(e, new SetMonster(PlayerId.One, tuner.InstanceId, 0)); Drain(e);
        NextMain(e); NextMain(e);
        var nonTuner = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == NonTuner);
        Ok(e, new NormalSummon(PlayerId.One, nonTuner.InstanceId, 1)); Drain(e);
        Reject(e, Command(e, 0, 1), DuelError.SummonMaterialNotAllowed);
        Reject(e, Command(e, 1, 1), DuelError.InvalidSummonMaterials);
    }

    [Fact]
    public void SummonWindowCommitsMaterialsBeforeSuccessfulSummon()
    {
        var e = Make(targetLevel: 5, window: true); Prepare(e, Tuner, NonTuner);
        var result = Ok(e, Command(e, 0, 1));
        Assert.Equal(2, result.Events.OfType<SummonMaterialSentToGraveyard>().Count());
        Assert.NotNull(e.State.PendingSummon);
        Assert.DoesNotContain(result.Events, x => x is MonsterSpecialSummoned);
        Ok(e, new PassSummonResponse(e.State.PendingSummon!.Responder, e.State.PendingSummon.DecisionId));
        Ok(e, new PassSummonResponse(e.State.PendingSummon!.Responder, e.State.PendingSummon.DecisionId));
        Assert.Equal(Target, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card.GameCardId);
    }

    [Fact]
    public void ProfilesRejectAmbiguousRolesAndUnsupportedMovement()
    {
        Assert.Throws<ArgumentException>(() => new SynchroMaterialRequirements([Tuner], [Tuner]));
        Assert.Throws<ArgumentException>(() => new SynchroMaterialRequirements([Tuner], [NonTuner], 2, 1));
        var requirements = new SynchroMaterialRequirements([Tuner], [NonTuner]);
        Assert.Throws<ArgumentException>(() => new ExtraDeckMaterialProcedureProfile("x", [Target], [Tuner, NonTuner], 2,
            MaterialDestination.Banished, synchro: requirements));
        Assert.Throws<ArgumentException>(() => new ExtraDeckMaterialProcedureProfile("x", [Target], [Tuner], 2, synchro: requirements));
    }

    [Fact]
    public void ObservedKrebonsAndGreenGadgetSummonPoweredInzektron()
    {
        // Original capture projection: analysis/duel-reference/synchro-validation-2026-10-30.json.
        const uint krebons = 7716, gadget = 6151, inzektron = 10991;
        uint[] ids = [krebons, gadget, inzektron];
        var catalog = new CardCatalog(new(null, "synchro-reference-projection"), ids.Select(id =>
            new CardDefinition(id, $"{id}", "", new(1000, 1000, id == krebons ? 2u : id == gadget ? 4u : 6u))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(CardCategory.Monster,
            AllowsNormalSummon: id != inzektron, AllowsBasicBattle: true,
            AllowsExtraDeck: id == inzektron, AllowsSummonMaterial: id != inzektron));
        uint[] deck = [krebons, krebons, gadget, .. Enumerable.Repeat(gadget, 37)];
        var e = new DuelEngine(catalog, rules, deck, deck, playerOneExtraDeck: [inzektron],
            experimentalSpecialSummonCards: [new(inzektron)],
            experimentalMaterialProcedures: [new("synchro", [inzektron], [krebons, gadget], 2,
                synchro: new([krebons], [gadget]))]);
        EnterMain(e);
        for (int zone = 1; zone <= 3; zone++)
        {
            if (zone != 1) { NextMain(e); NextMain(e); }
            uint id = zone == 3 ? gadget : krebons;
            var card = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == id);
            Ok(e, new NormalSummon(PlayerId.One, card.InstanceId, zone)); Drain(e);
        }
        var before = e.State.GetPlayer(PlayerId.One);
        Assert.Equal(new uint[] { 0, krebons, krebons, gadget, 0 }, before.MonsterZones.Select(m => m?.Card.GameCardId ?? 0));
        var materials = new[] { 1, 3 }.Select(z => new MonsterBinding(PlayerId.One, z,
            before.MonsterZones[z]!.Card, before.MonsterZones[z]!.PlacementId));
        Ok(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, "synchro",
            before.ExtraDeck.Single().InstanceId, 3, materials));
        var after = e.State.GetPlayer(PlayerId.One);
        Assert.Equal(new uint[] { 0, 0, krebons, inzektron, 0 }, after.MonsterZones.Select(m => m?.Card.GameCardId ?? 0));
        Assert.Equal(new uint[] { krebons, gadget }, after.Graveyard.Select(c => c.GameCardId));
    }
}
