using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>The short-text family's wiring, through representative cards.</summary>
public sealed class ShortTextCardFamilyTests
{
    private const uint DarkMonster = 901, EarthMonster = 902, FishMonster = 903, MachineMonster = 904,
        DarkSword = 4308, Umi = 4340, ThunderShort = 9664, CrystalSkull = 3611;

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
