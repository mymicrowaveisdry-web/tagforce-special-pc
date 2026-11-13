using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>The short-text family's wiring, through representative cards.</summary>
public sealed class ShortTextCardFamilyTests
{
    private const uint DarkMonster = 901, EarthMonster = 902, FishMonster = 903, MachineMonster = 904,
        DarkSword = 4308, Umi = 4340, ThunderShort = 9664, CrystalSkull = 3611, BigMonster = 905, ChangeOfHeart = 4860, Recruiter = 9282,
        FusionMonster = 906, FieldCard = 907, EquipCard = 908, PlanetPathfinder = 10232, FusionWeapon = 6138, Bowganian = 5687;

    private static CardNumericCodes Codes(uint type, uint attribute) => new(attribute, type, 1, 1, 0, 0, 0);

    private static CardCatalog Catalog() => new(new(null, "synthetic-short-text"),
        ShortTextCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(1000, 1000, 1), Codes(0, 0))).Concat(
        [
            new(DarkMonster, "Dark", "", new(1000, 1000, 4), Codes(MonsterTypes.Fiend, MonsterAttributes.Dark)),
            new(EarthMonster, "Earth", "", new(1000, 1000, 4), Codes(MonsterTypes.Warrior, MonsterAttributes.Earth)),
            new(FishMonster, "Fish", "", new(1000, 1000, 4), Codes(MonsterTypes.Fish, MonsterAttributes.Water)),
            new(MachineMonster, "Machine", "", new(1000, 1000, 4), Codes(MonsterTypes.Machine, MonsterAttributes.Earth))
        ]));

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        var rules = ShortTextCardFamily.Family.Rules.ToDictionary();
        foreach (uint id in new[] { DarkMonster, EarthMonster, FishMonster, MachineMonster })
            rules[id] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        return new(Catalog(), rules, first, second, experimentalTriggerEffects: ShortTextCardFamily.Family.Triggers,
            effectDefinitions: ShortTextCardFamily.Family.Definitions, shuffleSeed: 5);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(EarthMonster, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Summon(DuelEngine e, uint id, int zone) => Ok(e, new NormalSummon(Turn(e), Hand(e, id), zone));
    private static int? Atk(DuelEngine e, PlayerId p, int zone) => e.GetMonsterStatistics(p, zone)!.Attack;
    private static int? Def(DuelEngine e, PlayerId p, int zone) => e.GetMonsterStatistics(p, zone)!.Defense;

    [Fact]
    public void AttributeEquipRaisesAttackAndLowersDefenseOnItsAttributeOnly()
    {
        var e = Engine(Deck(DarkSword, DarkMonster), Deck());
        EnterMain(e); Summon(e, EarthMonster, 0);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, DarkSword), 4), DuelError.DecisionUnavailable);
        Next(e); Next(e);
        Summon(e, DarkMonster, 1);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, DarkSword), 4));
        var pending = e.State.PendingDecision!;
        var target = Assert.Single(pending.FieldTargetOptions!);
        Ok(e, new ChooseDecisionFieldTarget(pending.Player, pending.DecisionId, PlayerId.One, FieldLocation.MonsterZone, 1, target.Card.InstanceId));
        Resolve(e);
        Assert.Equal(1400, Atk(e, PlayerId.One, 1));
        Assert.Equal(800, Def(e, PlayerId.One, 1));
    }

    [Fact]
    public void UmiRaisesSeaTypesAndLowersMachinesOnBothSides()
    {
        var e = Engine(Deck(Umi, FishMonster), Deck(MachineMonster));
        EnterMain(e); Summon(e, FishMonster, 0);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Umi), DuelPlayerState.FieldZoneIndex)); Resolve(e);
        Next(e); Summon(e, MachineMonster, 0);
        Assert.Equal(1200, Atk(e, PlayerId.One, 0));
        Assert.Equal(1200, Def(e, PlayerId.One, 0));
        Assert.Equal(800, Atk(e, PlayerId.Two, 0));
        Assert.Equal(800, Def(e, PlayerId.Two, 0));
    }

    [Fact]
    public void CountedDamageNeedsAnOpposingMonsterAndScalesWithThem()
    {
        var e = Engine(Deck(ThunderShort), Deck());
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, ThunderShort), 4), DuelError.ActivationConditionNotMet);
        Next(e);
        Summon(e, EarthMonster, 0); Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, ThunderShort), 4)); Resolve(e);
        Assert.Equal(7600, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    /// <summary>The family plus a Level 6 monster and Change of Heart, for "this card you control is sent to your Graveyard".</summary>
    private static DuelEngine OwnFieldEngine(uint[] first, uint[] second)
    {
        var catalog = new CardCatalog(new(null, "synthetic-short-text-own-field"), Catalog().Cards.Concat(
        [
            new(BigMonster, "Big", "", new(2400, 1000, 6), Codes(MonsterTypes.Warrior, MonsterAttributes.Earth)),
            new(ChangeOfHeart, "Change of Heart", "", new(0, 0, 0), Codes(0, 0))
        ]));
        var rules = ShortTextCardFamily.Family.Rules.ToDictionary();
        rules[EarthMonster] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        rules[BigMonster] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeSummon: true);
        rules[ChangeOfHeart] = TemplateCardFamily.Family.Rules[ChangeOfHeart];
        return new(catalog, rules, first, second, experimentalTriggerEffects: ShortTextCardFamily.Family.Triggers,
            effectDefinitions: [.. ShortTextCardFamily.Family.Definitions, .. TemplateCardFamily.Family.Definitions.Where(d => d.CardId == ChangeOfHeart)],
            shuffleSeed: 5);
    }

    private static List<DuelEvent> TributeFor(DuelEngine e, PlayerId player, uint tributedId)
    {
        int zone = Array.FindIndex(e.State.GetPlayer(player).MonsterZones.ToArray(), m => m?.Card.GameCardId == tributedId);
        var tributed = e.State.GetPlayer(player).MonsterZones[zone]!;
        int free = Array.FindIndex(e.State.GetPlayer(player).MonsterZones.ToArray(), m => m is null);
        var events = Ok(e, new TributeSummon(player, e.State.Revision, Hand(e, BigMonster), free,
            [new MonsterBinding(player, zone, tributed.Card, tributed.PlacementId)])).Events.ToList();
        events.AddRange(EventEffectScenario.Drain(e));
        return events;
    }

    [Fact]
    public void OwnFieldTriggerNeedsItsOwnerToControlTheCard()
    {
        uint[] recruiterDeck = [Recruiter, EarthMonster, EarthMonster, EarthMonster, EarthMonster, EarthMonster, EarthMonster, EarthMonster, Recruiter];
        var owned = OwnFieldEngine(Deck([Recruiter, BigMonster, .. recruiterDeck.Skip(2)]), Deck());
        EnterMain(owned); Summon(owned, Recruiter, 0); Next(owned); Next(owned);
        var searched = Assert.Single(TributeFor(owned, PlayerId.One, Recruiter).OfType<CardSearched>());
        Assert.Equal((PlayerId.One, Recruiter), (searched.Player, searched.Card.GameCardId));

        var taken = OwnFieldEngine(Deck(ChangeOfHeart, BigMonster), Deck(recruiterDeck));
        EnterMain(taken); Next(taken);
        Summon(taken, Recruiter, 0); Next(taken);
        Ok(taken, new ActivateSpell(PlayerId.One, Hand(taken, ChangeOfHeart), 0));
        var pending = taken.State.PendingDecision!;
        var target = Assert.Single(pending.FieldTargetOptions!);
        Ok(taken, new ChooseDecisionFieldTarget(pending.Player, pending.DecisionId, target.Player, target.Location, target.Zone, target.Card.InstanceId));
        Resolve(taken);
        var events = TributeFor(taken, PlayerId.One, Recruiter);
        Assert.Empty(events.OfType<CardSearched>());
        Assert.Empty(events.OfType<TriggerActivated>());
        Assert.Contains(taken.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == Recruiter);
    }

    /// <summary>The family plus a Fusion-coded monster and Field and Equip Spells, for the decoded icon and kind filters.</summary>
    private static DuelEngine KindEngine(uint[] first)
    {
        var catalog = new CardCatalog(new(null, "synthetic-short-text-kinds"), Catalog().Cards.Concat(
        [
            new(FusionMonster, "Fusion", "", new(2000, 1000, 4), new(MonsterAttributes.Earth, MonsterTypes.Warrior, 3, 3, 0, 0, 0)),
            new(FieldCard, "Field", "", new(0, 0, 0), new(8, 25, 13, 13, 2, 0, 0)),
            new(EquipCard, "Equip", "", new(0, 0, 0), new(8, 25, 13, 13, 3, 0, 0))
        ]));
        var rules = ShortTextCardFamily.Family.Rules.ToDictionary();
        rules[EarthMonster] = rules[FusionMonster] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        rules[FieldCard] = rules[EquipCard] = new(CardCategory.Spell);
        return new(catalog, rules, first, Deck(), experimentalTriggerEffects: ShortTextCardFamily.Family.Triggers,
            effectDefinitions: ShortTextCardFamily.Family.Definitions, shuffleSeed: 5);
    }

    [Fact]
    public void IconAndKindFiltersReadDecodedCardData()
    {
        var search = KindEngine(Deck(PlanetPathfinder, EarthMonster, EarthMonster, EarthMonster, EarthMonster, EarthMonster, EarthMonster, EquipCard, FieldCard));
        EnterMain(search); Summon(search, PlanetPathfinder, 0);
        Ok(search, new ActivateMonsterEffect(PlayerId.One, 0));
        IReadOnlyList<CardInstance>? offered = null;
        while (search.State.PendingChain is { } chain)
        {
            if (chain.DefinitionCardChoice is { } pick) { offered = pick.Options; Ok(search, new ChooseDefinitionCard(pick.Player, pick.DecisionId, pick.Options[0].InstanceId)); }
            else if (chain.NextLink == 0) Ok(search, new PassChainResponse(chain.Responder, chain.DecisionId));
            else Ok(search, new ResolveNextChainLink(PlayerId.One, chain.DecisionId));
        }
        Assert.Equal([FieldCard], offered!.Select(c => c.GameCardId).Distinct());

        var equip = KindEngine(Deck(FusionWeapon, FusionMonster, EarthMonster));
        EnterMain(equip); Summon(equip, EarthMonster, 0); Next(equip); Next(equip);
        Summon(equip, FusionMonster, 1);
        Ok(equip, new ActivateSpell(PlayerId.One, Hand(equip, FusionWeapon), 4));
        var target = Assert.Single(equip.State.PendingDecision!.FieldTargetOptions!);
        Assert.Equal(FusionMonster, target.Card.GameCardId);
    }

    [Fact]
    public void MonsterStandbyTriggerResolvesOnlyInItsControllersStandbyPhase()
    {
        var e = Engine(Deck(Bowganian), Deck());
        EnterMain(e); Summon(e, Bowganian, 0);
        Phase(e, DuelPhase.End); Ok(e, new EndTurn(PlayerId.One));
        Ok(e, new ResolveDrawStep(PlayerId.Two)); EventEffectScenario.Drain(e);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Phase(e, DuelPhase.Main1); Phase(e, DuelPhase.End); Ok(e, new EndTurn(PlayerId.Two));
        Ok(e, new ResolveDrawStep(PlayerId.One)); EventEffectScenario.Drain(e);
        Assert.Equal(7400, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void SummonTriggerDamagesItsOwnController()
    {
        var e = Engine(Deck(CrystalSkull), Deck());
        EnterMain(e); Summon(e, CrystalSkull, 0);
        EventEffectScenario.Drain(e);
        Assert.Equal(7000, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }
}
