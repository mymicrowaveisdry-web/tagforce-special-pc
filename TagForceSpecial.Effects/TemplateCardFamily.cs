using TagForceSpecial.Core;
using static TagForceSpecial.Effects.MonsterAttributes;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects;

/// <summary>Cards that follow a shared template: restricted Equips, stat Field Spells, Type wipes, Deck searches and Graveyard recovery.</summary>
public static class TemplateCardFamily
{
    public const string FamilyId = "template-card-definition";
    public const string BatchId = "tfsp-family-batch-10d-01";

    private const string EndsWithEndPhase = "The change ends when the End Phase begins.";
    private static readonly CardTable T = new("template");
    private static readonly FieldTarget FaceUpMonster = new(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp);

    private static MonsterTypeFilter OfType(uint type) => new([type]);
    private static MonsterAttributeFilter OfAttribute(uint attribute) => new(attribute);
    private static LastingStatModifier AttributeBoost(uint attribute) => new(TargetSide.Either, 500, -400, OfAttribute(attribute));

    private static AuthoredCard TypeWipe(uint id, string name, uint type) =>
        T.Normal(id, name, [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp, OfType(type))]);

    private static AuthoredCard Search(uint id, string name, uint maxLevel, uint type) =>
        T.Normal(id, name, [new AddFromDeckToHandStep(new MonsterLevelFilter(maxLevel, type))]);

    /// <summary>"Once per turn: Special Summon 1 monster of this Type from your hand; control no monsters to activate and to resolve."</summary>
    private static AuthoredCard OnlyWithNoMonsters(uint id, string name, uint type) => T.ContinuousWithEffects(id, name, trap: false,
        new EffectOption([new StopUnlessNoOwnMonstersStep(), new SpecialSummonStep(SpecialSummonSources.Hand, OfType(type))], Conditions: [new ControlsNoMonstersCondition()]));

    private static AuthoredCard DiscardToSearch(uint id, string name, uint target) =>
        T.FromZone(id, name, EffectOrigin.Hand, new EffectOption([new AddFromDeckToHandStep(new CardIdFilter([target]))], new SendSelfCost(SelfDestination.Discard)));

    private static EffectStep[] SearchFor(IReadOnlyList<uint> ids) => [new AddFromDeckToHandStep(new CardIdFilter(ids))];

    private static AuthoredCard Recover(uint id, string name, CardFilter filter, int count = 1, CardZone zone = CardZone.Graveyard, EffectCost? cost = null) =>
        T.Normal(id, name, [new AddTargetsToHandStep()], cost, [new ZoneTarget(zone, TargetSide.Own, filter, count, count)]);

    private static readonly AuthoredCard[] Cards =
    [
        T.Equip(4307, "Legendary Sword", 300, 300, OfType(Warrior)),
        T.Equip(4309, "Dark Energy", 300, 300, OfType(Fiend)),
        T.Equip(4311, "Laser Cannon Armor", 300, 300, OfType(Insect)),
        T.Equip(4312, "Insect Armor with Laser Cannon", 700, 0, OfType(Insect)),
        T.Equip(4314, "Beast Fangs", 300, 300, OfType(Beast)),
        T.Equip(4316, "Vile Germs", 300, 300, OfType(Plant)),
        T.Equip(4318, "Silver Bow and Arrow", 300, 300, OfType(Fairy)),
        T.Equip(4321, "Dragon Treasure", 300, 300, OfType(Dragon)),
        T.Equip(4322, "Electro-Whip", 300, 300, OfType(Thunder)),
        T.Equip(4325, "Mystical Moon", 300, 300, OfType(BeastWarrior)),
        T.Equip(4328, "Violet Crystal", 300, 300, OfType(Zombie)),
        T.Equip(4329, "Book of Secret Arts", 300, 300, OfType(Spellcaster)),
        T.Equip(4331, "Machine Conversion Factory", 300, 300, OfType(Machine)),
        T.Equip(4332, "Raise Body Heat", 300, 300, OfType(Dinosaur)),
        T.Equip(4333, "Follow Wind", 300, 300, OfType(WingedBeast)),
        T.Equip(4334, "Power of Kaishin", 300, 300, OfType(Aqua)),
        T.Equip(4660, "Salamandra", 700, 0, OfAttribute(Fire)),
        T.Equip(4674, "Shine Palace", 700, 0, OfAttribute(Light)),
        T.Field(4932, "Gaia Power", AttributeBoost(Earth)),
        T.Field(4933, "Umiiruka", AttributeBoost(Water)),
        T.Field(4934, "Molten Destruction", AttributeBoost(Fire)),
        T.Field(4935, "Rising Air Current", AttributeBoost(Wind)),
        T.Field(4936, "Luminous Spark", AttributeBoost(Light)),
        T.Field(4937, "Mystic Plasma Zone", AttributeBoost(Dark)),
        T.Field(6823, "Jurassic World", new LastingStatModifier(TargetSide.Either, 300, 300, OfType(Dinosaur))),
        TypeWipe(4659, "Warrior Elimination", Warrior),
        TypeWipe(4666, "Acid Rain", Machine),
        TypeWipe(4668, "Eradicating Aerosol", Insect),
        TypeWipe(4669, "Breath of Light", Rock),
        TypeWipe(4670, "Eternal Drought", Fish),
        TypeWipe(4868, "Last Day of Witch", Spellcaster),
        TypeWipe(4869, "Exile of the Wicked", Fiend),
        Search(5328, "Reinforcement of the Army", 4, Warrior),
        Search(8352, "Fossil Dig", 6, Dinosaur),
        Recover(5330, "The Warrior Returning Alive", OfType(Warrior)),
        Recover(7453, "Dark Eruption", new MonsterTraitFilter(MonsterStat.Attack, 1500, attributeCode: Dark)),
        Recover(7151, "Recurring Nightmare", new MonsterTraitFilter(MonsterStat.Defense, 0, attributeCode: Dark), count: 2),
        Recover(5908, "Salvage", new MonsterTraitFilter(MonsterStat.Attack, 1500, attributeCode: Water), count: 2),
        Recover(7630, "Light of Redemption", OfAttribute(Light), zone: CardZone.Banished, cost: new PayLifePointsCost(800)),
        T.Normal(6672, "E - Emergency Call", SearchFor(CardArchetypes.ElementalHero)),
        T.Normal(9518, "Resonator Call", SearchFor(CardArchetypes.Resonator)),
        T.Normal(10079, "Hieratic Seal of Convocation", SearchFor(CardArchetypes.Hieratic)),
        T.Normal(9131, "Medallion of the Ice Barrier", SearchFor(CardArchetypes.IceBarrier)),
        T.Trap(9364, "Gleipnir, the Fetters of Fenrir", SearchFor(CardArchetypes.Nordic)),
        T.Trap(4992, "Enchanted Javelin", [new GainByTargetAttackStep()], targets: TrapTiming.Attacker, conditions: TrapTiming.OpponentAttacks),
        T.WhenSummoned(5510, "Gravekeeper's Curse", [new DealDamageStep(500)]),
        T.WhenBattleDestroyed(6506, "Scarr, Scout of Dark World", SearchFor(CardArchetypes.DarkWorldLevelFourOrLower)),
        T.QuickPlay(4905, "Rush Recklessly", [new ModifyTargetsUntilEndStep(700)], targets: [FaceUpMonster], exceptions: EndsWithEndPhase),
        T.QuickPlay(5432, "Book of Moon", [new FlipTargetsFaceDownStep()], targets: [FaceUpMonster], exceptions: "Tokens and Xyz Monsters stay face-up."),
        T.Normal(5431, "Book of Taiyou", [new FlipTargetsFaceUpStep()], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceDown)],
            exceptions: "Handled as a targeted flip. It counts as flipped for Flip effects; it is not a Flip Summon."),
        T.Normal(7219, "Double Summon", [new GrantNormalSummonStep()], exceptions: "A second copy in the same turn adds nothing."),
        T.Normal(7248, "Trade-In", [new DrawCardsStep(2)], new DiscardCost(new MonsterLevelFilter(8, minLevel: 8))),
        T.Normal(5688, "Magical Stone Excavation", [new AddTargetsToHandStep()], new DiscardCost(Count: 2),
            [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new CardCategoryFilter(CardCategory.Spell))]),
        T.Trap(6709, "Icarus Attack", [new DestroyTargetsStep()], new TributeCost(OfType(WingedBeast)), [new FieldTarget(FieldCardKind.Any, count: 2)]),
        DiscardToSearch(7108, "Destroyersaurus", 6823),
        DiscardToSearch(7572, "Hecatrice", 7573),
        T.FromZone(7359, "Mezuki", EffectOrigin.Graveyard, new EffectOption([new SpecialSummonTargetStep()], new SendSelfCost(SelfDestination.Banish),
            [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, OfType(Zombie), mustBeSummonable: true)])),
        T.FromZone(10735, "Malicevorous Fork", EffectOrigin.Hand, new EffectOption([new SpecialSummonSelfStep()], new DiscardCost(OfType(Fiend)))),
        T.ContinuousTrigger(11147, "Supply Squad", trap: false, MonsterEventKind.Destroyed, EffectRelation.Controller, [new DrawCardsStep(1)], limit: TriggerTurnLimit.PerPlacement),
        T.Modal(5607, "Poison of the Old Man", EffectKind.QuickPlaySpell, "The option is chosen at activation.", new EffectOption([new GainLifePointsStep(1200)]), new EffectOption([new DealDamageStep(800)])),
        OnlyWithNoMonsters(5435, "Call of the Mummy", Zombie),
        OnlyWithNoMonsters(7573, "Valhalla, Hall of the Fallen", Fairy),
        T.Normal(9666, "Surface", [new SpecialSummonTargetStep(BattlePosition.FaceUpDefense)], targets:
            [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new AllOfFilter(new MonsterLevelFilter(3), new MonsterTypeFilter([Fish, SeaSerpent, Aqua])), mustBeSummonable: true)])
    ];

    public static CardFamily Family { get; } = CardTable.Family(FamilyId, BatchId, ["TemplateCardFamilyTests", "CardPoolTests"], Cards);
}
