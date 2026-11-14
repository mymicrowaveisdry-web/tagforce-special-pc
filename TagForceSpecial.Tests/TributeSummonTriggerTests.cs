using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Successful Tribute Summons share the Normal Summon timing point without treating Sets or other Summons as Tributes.</summary>
public sealed class TributeSummonTriggerTests
{
    private const uint Donor = 901, Other = 902, High = 903, Spell = 910, Response = 911, FaceSpell = 912, SetSpell = 913, FaceTrap = 914,
        Salvage = 9474, Prima = 6666;

    private static DuelEngine Engine(bool probes = true, bool window = false, bool negateSummon = false, bool scales = false)
    {
        uint[] ids = [Donor, Other, High, Spell, Response, FaceSpell, SetSpell, FaceTrap, Salvage, Prima];
        var catalog = new CardCatalog(new(null, "synthetic-tribute-triggers"), ids.Select(id =>
            new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, id == Salvage ? 5u : id is High or Prima ? 6u : 4u),
                new CardNumericCodes(MonsterAttributes.Earth, MonsterTypes.Warrior, id == Donor ? 16u : 1u, id == Donor ? 16u : 1u, 0, 0, 0))));
        var family = ShortTextCardFamily.Family;
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(
            id is Response or FaceTrap ? CardCategory.Trap : id is Spell or FaceSpell or SetSpell ? CardCategory.Spell : CardCategory.Monster,
            AllowsNormalSummon: id is Donor or Other or High, AllowsBasicBattle: id is Donor or Other or High, AllowsTributeForSummon: id is Donor or Other,
            AllowsTributeSummon: id == High, AllowsSpellTrapSet: id is Spell or Response or FaceSpell or SetSpell or FaceTrap, IsTuner: id == Donor));
        rules[Salvage] = family.Rules[Salvage]; rules[Prima] = family.Rules[Prima];
        return new(catalog, rules,
            [Donor, High, Salvage, Prima, Spell, Donor, FaceSpell, .. Enumerable.Repeat(Other, 33)],
            [Response, FaceSpell, SetSpell, FaceTrap, Other, .. Enumerable.Repeat(Other, 35)],
            experimentalTriggerEffects: probes
                ? [Gain("normal", High, gain: 100), Gain("tribute", High, MonsterEventKind.TributeSummoned, gain: 200),
                   Gain("donor", Donor, MonsterEventKind.TributedForSummon, gain: 400, location: TriggerSourceLocation.GraveyardSelf)]
                : family.Triggers.Where(t => t.CardId is Salvage or Prima),
            experimentalTributeProcedures: [new("no-tribute", High, 0)],
            experimentalPendulumCards: scales ? [new(Donor, 1, 8)] : null,
            experimentalSummonWindows: window ? [new(High)] : null,
            effectDefinitions:
            [.. family.Definitions.Where(d => d.CardId is Salvage or Prima),
                new("special", Spell, EffectKind.NormalSpell, [new SpecialSummonStep(SpecialSummonSources.Hand, [High])]),
                new("face-spell", FaceSpell, EffectKind.ContinuousSpell, [], isMarker: true),
                new("face-trap", FaceTrap, EffectKind.ContinuousTrap, [], isMarker: true),
                new("union", Donor, EffectKind.MonsterIgnition, [new UnionEquipStep()],
                    fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp,
                        cardFilter: new CardIdFilter([Other]), noUnionEquipped: true)], oncePerTurn: true),
                negateSummon ? ResponseProbe.NegateSummon("response", Response, 500, SummonKinds.Normal)
                    : ResponseProbe.Negate("response", Response, ResponseNegation.Effect, ChainSpellSpeed.Two)], shuffleSeed: 9);
    }

    private static int Hand(DuelEngine e, uint id, PlayerId player = PlayerId.One) =>
        e.State.GetPlayer(player).Hand.First(c => c.GameCardId == id).InstanceId;

    private static DuelEngine Prepared(bool probes = true, bool window = false, bool negateSummon = false, bool scales = false)
    {
        var e = Engine(probes, window, negateSummon, scales); EnterMain(e); Summon(e, Donor);
        NextMain(e); Set(e, Response); NextMain(e);
        return e;
    }

    private static DuelActionResult Tribute(DuelEngine e, uint id = High, bool set = false)
    {
        var m = e.State.GetPlayer(PlayerId.One).MonsterZones[0]!;
        return Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, id), 0,
            [new MonsterBinding(PlayerId.One, 0, m.Card, m.PlacementId)], set));
    }

    [Fact]
    public void SuccessfulTributeKeepsNormalAndDonorTriggersAtTheSameTimingPoint()
    {
        var e = Prepared(); var before = e.State; var saved = JsonSerializer.Serialize(before);
        Tribute(e);
        var options = e.State.EventEffects.PendingTriggers!.Options;
        Assert.Equal(new[] { "donor", "normal", "tribute" }, options.Select(t => t.Profile.EffectId).Order());
        Assert.Single(options.Select(t => t.Occurrence.TimingPoint).Distinct());
        Drain(e);
        Assert.Equal(8700, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(saved, JsonSerializer.Serialize(before));
    }

    [Fact]
    public void NoTributeNormalSummonTriggersOnlyNormalSummoned()
    {
        var e = Engine(); EnterMain(e);
        Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, High), 0, [], procedureId: "no-tribute"));
        Assert.Equal("normal", Assert.Single(e.State.EventEffects.PendingTriggers!.Options).Profile.EffectId);
        Drain(e); Assert.Equal(8100, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void SpecialSummonDoesNotBecomeATributeSummon()
    {
        var e = Engine(); EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Spell), 0)); Drain(e);
        Assert.Equal(High, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card.GameCardId);
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void TributeSetAndLaterFlipSummonDoNotFireTheTributeTrigger()
    {
        var e = Prepared(); Tribute(e, set: true);
        Assert.Null(e.State.EventEffects.PendingTriggers);
        NextMain(e); NextMain(e);
        Ok(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack)); Drain(e);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SummonResponseWindowDefersTriggersAndNegationPreventsThem(bool negate)
    {
        var e = Prepared(window: true, negateSummon: true); Tribute(e);
        Assert.Null(e.State.EventEffects.PendingTriggers);
        var pending = e.State.PendingSummon!;
        if (negate)
        {
            Ok(e, new ActivateSummonNegation(PlayerId.Two, pending.DecisionId, 0)); Drain(e);
            Assert.Null(e.State.EventEffects.PendingTriggers);
            Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
            Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        }
        else
        {
            while (e.State.PendingSummon is { } p) Ok(e, new PassSummonResponse(p.Responder, p.DecisionId));
            Assert.Contains(e.State.EventEffects.PendingTriggers!.Options, t => t.Profile.EventKind == MonsterEventKind.TributeSummoned);
            Drain(e); Assert.Equal(8700, e.State.GetPlayer(PlayerId.One).LifePoints);
        }
    }

    [Theory]
    [InlineData(DecisionCardSource.Hand)]
    [InlineData(DecisionCardSource.Graveyard)]
    public void TributeTriggerChoosesATunerFromHandOrTheJustTributedGraveyardCard(DecisionCardSource source)
    {
        var e = Prepared(probes: false); Tribute(e, Salvage); Order(e);
        Assert.Null(e.State.PendingDecision);
        while (e.State.PendingChain is { DefinitionSummon: null } chain)
            if (chain.NextLink == 0) Pass(e); else Resolve(e);
        var p = e.State.PendingChain!.DefinitionSummon!;
        Assert.Equal(2, p.Options.Count);
        Assert.All(p.Options, o => Assert.Equal(Donor, o.Card.GameCardId));
        var pick = p.Options.Single(o => o.Source == source);
        Ok(e, new ChooseDefinitionSummon(PlayerId.One, p.DecisionId, pick.Card.InstanceId, 1, BattlePosition.FaceUpDefense));
        Drain(e);
        Assert.Equal(pick.Card, e.State.GetPlayer(PlayerId.One).MonsterZones[1]!.Card);
    }

    [Fact]
    public void NegatedTributeTriggerNeverRequestsItsRevivalChoice()
    {
        var e = Prepared(probes: false); Tribute(e, Salvage); Order(e);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        var events = Drain(e);
        Assert.Empty(events.OfType<DefinitionSummonRequested>());
        Assert.Empty(events.OfType<MonsterSpecialSummoned>());
        Assert.Single(e.State.GetPlayer(PlayerId.One).MonsterZones, m => m is not null);
    }

    [Fact]
    public void MandatoryTributeTriggerDestroysFaceUpSpellsOnBothSidesAndKeepsTrapsAndSets()
    {
        var e = Prepared(probes: false);
        NextMain(e); NextMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, FaceSpell), 0)); Drain(e);
        NextMain(e);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, FaceSpell, PlayerId.Two), 1)); Drain(e);
        Set(e, SetSpell, 2); Set(e, FaceTrap, 3); NextMain(e);
        NextMain(e); Ok(e, new ActivateSetSpell(PlayerId.Two, 3)); Drain(e); NextMain(e);
        Tribute(e, Prima);
        Assert.Equal(TriggerRequirement.Mandatory, Assert.Single(e.State.EventEffects.PendingTriggers!.Options).Profile.Requirement);
        Drain(e);
        Assert.Null(e.State.GetPlayer(PlayerId.One).SpellTrapFields[0]);
        var opponent = e.State.GetPlayer(PlayerId.Two);
        Assert.Null(opponent.SpellTrapFields[1]);
        Assert.NotNull(opponent.SpellTrapFields[0]); Assert.NotNull(opponent.SpellTrapFields[2]); Assert.NotNull(opponent.SpellTrapFields[3]);
    }

    [Fact]
    public void SpellFieldScopeDestroysAPendulumScaleWithoutChangingItsPrintedMonsterCategory()
    {
        var e = Prepared(probes: false, scales: true);
        var scale = e.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == Donor);
        Ok(e, new ActivatePendulumScale(PlayerId.One, scale.InstanceId, 0)); Drain(e);
        Tribute(e, Prima); Drain(e);
        var player = e.State.GetPlayer(PlayerId.One);
        Assert.Null(player.PendulumZones[0]);
        Assert.Contains(scale, player.FaceUpExtraDeck);
        Assert.Equal(Prima, player.MonsterZones[0]!.Card.GameCardId);
    }

    [Fact]
    public void SpellFieldScopeDestroysAUnionEquipAndKeepsItsMonsterHost()
    {
        var e = Prepared(probes: false);
        NextMain(e); NextMain(e); NextMain(e); NextMain(e);
        Summon(e, Other, 2);
        var equipped = e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card;
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
        var p = e.State.PendingDecision!;
        var target = Assert.Single(p.FieldTargetOptions!);
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, target.Player, target.Location, target.Zone, target.Card.InstanceId)); Drain(e);
        Assert.True(e.State.GetPlayer(PlayerId.One).SpellTrapFields[0]!.IsUnion);
        NextMain(e); NextMain(e); Summon(e, Donor, 0);
        NextMain(e); NextMain(e); Tribute(e, Prima); Drain(e);
        var player = e.State.GetPlayer(PlayerId.One);
        Assert.Null(player.SpellTrapFields[0]);
        Assert.Contains(equipped, player.Graveyard);
        Assert.Equal(Other, player.MonsterZones[2]!.Card.GameCardId);
    }
}
