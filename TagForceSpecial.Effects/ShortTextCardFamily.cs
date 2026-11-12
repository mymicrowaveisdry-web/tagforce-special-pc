using TagForceSpecial.Core;
using TagForceSpecial.Effects.Definitions;
using static TagForceSpecial.Effects.MonsterAttributes;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects;

/// <summary>Cards with short text that existing steps express exactly: equips, searchers, recruiters, simple triggers and one-step Spells and Traps.</summary>
public static class ShortTextCardFamily
{
    public const string FamilyId = "short-text-card-definition";
    public const string BatchId = "tfsp-family-batch-10d-05";

    private const string EndsWithEndPhase = "The change ends when the End Phase begins.";
    private const uint Flip = 24, Tuner = 16;
    private static readonly CardTable T = new("short-text");
    private static readonly DuelCardRules HighLevel = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeSummon: true, AllowsTributeForSummon: true);
    private static readonly DuelCardRules TunerRules = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true,
        AllowsSummonMaterial: true, IsTuner: true);
    private static readonly FieldTarget FaceUpMonster = new(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp);
    private static readonly uint[] BlueEyesWhiteDragon = [3800, 3801, 3802, 3849, 3862, 4007];

    private static MonsterTypeFilter OfType(params uint[] types) => new(types);
    private static MonsterAttributeFilter OfAttribute(uint attribute) => new(attribute);
    private static CardIdFilter Named(params uint[] ids) => new(ids);
    private static MonsterLevelFilter Level(uint level) => new(level, minLevel: level);
    private static AuthoredCard AttributeEquip(uint id, string name, uint attribute) => T.Equip(id, name, 400, -200, OfAttribute(attribute));

    private static EffectStep[] Search(CardFilter filter) => [new AddFromDeckToHandStep(filter)];

    private static AuthoredCard DiscardToSearch(uint id, string name, uint target) =>
        T.FromZone(id, name, EffectOrigin.Hand, new EffectOption([new AddFromDeckToHandStep(Named(target))], new SendSelfCost(SelfDestination.Discard)));

    /// <summary>"When this card is destroyed by battle and sent to the Graveyard".</summary>
    private static AuthoredCard BattleDestroyed(uint id, string name, EffectStep[] steps, bool optional = false, uint getterKind = 1, DuelCardRules? rules = null,
        EffectTarget[]? targets = null, TriggerTiming timing = TriggerTiming.When) =>
        T.Trigger(id, name, MonsterEventKind.SentToGraveyard, steps, optional ? TriggerRequirement.Optional : TriggerRequirement.Mandatory, TriggerSourceLocation.GraveyardSelf,
            TriggerEventCause.ByBattle, timing, targets, getterKind: getterKind, rules: rules);

    /// <summary>"When this card is sent to the Graveyard".</summary>
    private static AuthoredCard SentToGraveyard(uint id, string name, EffectStep[] steps, bool optional = false, TriggerEventCause cause = TriggerEventCause.Any,
        uint getterKind = 1, DuelCardRules? rules = null) =>
        T.Trigger(id, name, MonsterEventKind.SentToGraveyard, steps, optional ? TriggerRequirement.Optional : TriggerRequirement.Mandatory, TriggerSourceLocation.GraveyardSelf,
            cause, getterKind: getterKind, rules: rules);

    /// <summary>"FLIP: ..."; <paramref name="optional"/> for "FLIP: You can ...".</summary>
    private static AuthoredCard FlipEffect(uint id, string name, EffectStep[] steps, bool optional = false) =>
        T.Trigger(id, name, MonsterEventKind.FlippedFaceUp, steps, optional ? TriggerRequirement.Optional : TriggerRequirement.Mandatory, getterKind: Flip);

    private static AuthoredCard BattleTrigger(uint id, string name, MonsterEventKind when, EffectStep[] steps, bool optional = false, uint getterKind = 1,
        DuelCardRules? rules = null, string exceptions = "none") =>
        T.Trigger(id, name, when, steps, optional ? TriggerRequirement.Optional : TriggerRequirement.Mandatory, getterKind: getterKind, rules: rules, exceptions: exceptions);

    private static AuthoredCard TributeSelf(uint id, string name, EffectStep[] steps, EffectTarget[]? targets = null, DuelCardRules? rules = null) =>
        T.Ignition(id, name, steps, new TributeSelfCost(), targets, rules: rules);

    private static AuthoredCard Boost(uint id, string name, bool trap, int attack, int defense) => trap
        ? T.Trap(id, name, [new ModifyTargetsUntilEndStep(attack, defense)], targets: [FaceUpMonster], exceptions: EndsWithEndPhase)
        : T.QuickPlay(id, name, [new ModifyTargetsUntilEndStep(attack, defense)], targets: [FaceUpMonster], exceptions: EndsWithEndPhase);

    public static IReadOnlyList<AuthoredCard> Cards { get; } = Array.AsReadOnly<AuthoredCard>(
    [
        AttributeEquip(4308, "Sword of Dark Destruction", Dark),
        AttributeEquip(4330, "Invigoration", Earth),
        AttributeEquip(4313, "Elf's Light", Light),
        AttributeEquip(4315, "Steel Shell", Water),
        AttributeEquip(4846, "Burning Spear", Fire),
        AttributeEquip(4847, "Gust Fan", Wind),
        T.Field(4340, "Umi", new LastingStatModifier(TargetSide.Either, 200, 200, OfType(Fish, SeaSerpent, Thunder, Aqua)),
            new LastingStatModifier(TargetSide.Either, -200, -200, OfType(Machine, Pyro))),

        BattleDestroyed(8812, "Key Mouse", Search(new MonsterLevelFilter(3, Beast)), optional: true, Tuner, TunerRules),
        BattleDestroyed(8784, "Dread Dragon", Search(new MonsterLevelFilter(3, Dragon)), optional: true, Tuner, TunerRules),
        BattleDestroyed(8082, "Rai-Mei", Search(new AllOfFilter(new MonsterLevelFilter(2), OfAttribute(Light))), optional: true),
        BattleDestroyed(8614, "Tricular", [new SpecialSummonStep(SpecialSummonSources.Hand | SpecialSummonSources.Deck, Named(8613))], optional: true),
        BattleDestroyed(8613, "Bicular", [new SpecialSummonStep(SpecialSummonSources.Hand | SpecialSummonSources.Deck, Named(8612))], optional: true),
        BattleDestroyed(8738, "Stygian Security", [new SpecialSummonStep(SpecialSummonSources.Deck, new MonsterLevelFilter(1, Fiend))], getterKind: Tuner, rules: TunerRules),
        BattleDestroyed(6388, "UFOroid", [new SpecialSummonStep(SpecialSummonSources.Deck, new AllOfFilter(OfType(Machine), new MonsterTraitFilter(MonsterStat.Attack, 1500)),
            BattlePosition.FaceUpAttack)], optional: true, rules: HighLevel),
        BattleDestroyed(8965, "Elephun", [new AddTargetsToHandStep()], optional: true, Tuner, TunerRules,
            [new ZoneTarget(CardZone.Banished, TargetSide.Own, new AllOfFilter(new MonsterLevelFilter(3), OfType(Beast, BeastWarrior, WingedBeast)))]),
        BattleDestroyed(6327, "Mine Golem", [new DealDamageStep(500)]),
        BattleDestroyed(7527, "Blizzed, Defender of the Ice Barrier", [new DrawCardsStep(1)]),
        BattleDestroyed(8088, "Violet Witch", Search(new AllOfFilter(OfType(Plant), new MonsterTraitFilter(MonsterStat.Defense, 1500))), timing: TriggerTiming.If),
        SentToGraveyard(7892, "Botanical Girl", Search(new AllOfFilter(OfType(Plant), new MonsterTraitFilter(MonsterStat.Defense, 1000))), optional: true,
            TriggerEventCause.FromField),
        SentToGraveyard(5877, "Burning Algae", [new GainLifePointsStep(1000, EffectPlayers.Opponent)]),
        SentToGraveyard(4770, "Skull-Mark Ladybug", [new GainLifePointsStep(1000)]),
        SentToGraveyard(7850, "The White Stone of Legend", Search(Named(BlueEyesWhiteDragon)), getterKind: Tuner, rules: TunerRules),

        FlipEffect(8755, "Super-Nimble Mega Hamster", [new SpecialSummonStep(SpecialSummonSources.Deck, new MonsterLevelFilter(3, Beast), BattlePosition.FaceDownDefense)], optional: true),
        FlipEffect(4970, "Bubonic Vermin", [new SpecialSummonStep(SpecialSummonSources.Deck, Named(4970), BattlePosition.FaceDownDefense)], optional: true),
        FlipEffect(9441, "Gem-Turtle", Search(Named(8918)), optional: true),
        FlipEffect(6303, "Machina Defender", Search(Named(6307))),
        FlipEffect(10817, "Tour Bus To Forbidden Realms", Search(new AllOfFilter(OfType(Fiend), new NotFilter(OfAttribute(Dark)), new NotFilter(OfAttribute(Light))))),
        FlipEffect(10746, "Baby Raccoon Tantan", [new SpecialSummonStep(SpecialSummonSources.Deck, new AllOfFilter(new MonsterLevelFilter(2, Beast, minLevel: 2), new NotFilter(Named(10746))))]),
        FlipEffect(10164, "Dust Knight", [new SendFromDeckToGraveyardStep(OfAttribute(Earth))]),
        FlipEffect(5661, "Des Koala", [new DealDamageStep(400, EffectCount.OpponentHand)]),
        FlipEffect(5139, "4-Starred Ladybug of Doom", [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Opponent, FieldCardFilter.FaceUp, Level(4))]),
        FlipEffect(7439, "Magnetic Mosquito", [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp, OfType(Machine))]),

        T.Trigger(5883, "Sacred Crane", MonsterEventKind.SpecialSummoned, [new DrawCardsStep(1)], timing: TriggerTiming.If),
        T.WhenSummoned(3611, "Crystal Skull", [new TakeDamageStep(1000)]),
        BattleTrigger(3740, "Toy Emperor", MonsterEventKind.DestroyedOpponentByBattle, Search(new CardCategoryFilter(CardCategory.Trap)), optional: true, rules: HighLevel,
            exceptions: "Triggers only when the destroyed monster is sent to the Graveyard."),
        BattleTrigger(7837, "Chaos-End Master", MonsterEventKind.DestroyedOpponentByBattle, [new SpecialSummonStep(SpecialSummonSources.Deck,
            new AllOfFilter(new MonsterLevelFilter(12, minLevel: 5), new MonsterTraitFilter(MonsterStat.Attack, 1600)))], optional: true, Tuner, TunerRules),
        BattleTrigger(7338, "Fossil Tusker", MonsterEventKind.DestroyedOpponentByBattle, [new DealDamageStep(400)]),
        BattleTrigger(4529, "The Bistro Butcher", MonsterEventKind.InflictedBattleDamage, [new DrawCardsStep(2, EffectPlayers.Opponent)]),
        BattleTrigger(4226, "Masked Sorcerer", MonsterEventKind.InflictedBattleDamage, [new DrawCardsStep(1)]),
        BattleTrigger(8077, "Rose, Warrior of Revenge", MonsterEventKind.InflictedBattleDamage, [new DealDamageStep(300)], getterKind: Tuner, rules: TunerRules),
        BattleTrigger(5425, "Dark Scorpion Burglars", MonsterEventKind.InflictedBattleDamage, [new OpponentSendsFromDeckStep(new CardCategoryFilter(CardCategory.Spell))]),

        T.Ignition(5953, "The Agent of Creation - Venus", [new SpecialSummonStep(SpecialSummonSources.Hand | SpecialSummonSources.Deck, Named(5949))], new PayLifePointsCost(500)),
        T.Ignition(8376, "Beast Striker", [new SpecialSummonStep(SpecialSummonSources.Deck, Named(8375))], new DiscardCost(), oncePerTurn: true),
        T.Ignition(5205, "Hysteric Fairy", [new GainLifePointsStep(1000)], new TributeCost(Count: 2)),
        TributeSelf(6298, "Kaibaman", [new SpecialSummonStep(SpecialSummonSources.Hand, Named(BlueEyesWhiteDragon))]),
        TributeSelf(6177, "The Creator Incarnate", [new SpecialSummonStep(SpecialSummonSources.Hand, Named(6176))]),
        TributeSelf(9815, "Evilswarm Ketos", [new DestroyTargetsStep()], [new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Opponent)]),
        TributeSelf(9816, "Evilswarm O'lantern", [new DestroyTargetsStep()], [new FieldTarget(FieldCardKind.Monster, TargetSide.Opponent, FieldCardFilter.FaceUp)]),
        TributeSelf(10229, "Deep Sweeper", [new DestroyTargetsStep()], [new FieldTarget(FieldCardKind.SpellTrap)]),
        TributeSelf(8782, "Second Booster", [new ModifyTargetsUntilEndStep(1500)], [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUpAttack)]),
        T.FromZone(6949, "Destiny HERO - Malicious", EffectOrigin.Graveyard, new EffectOption([new SpecialSummonStep(SpecialSummonSources.Deck, Named(6949))],
            new SendSelfCost(SelfDestination.Banish)), rules: HighLevel),
        DiscardToSearch(7107, "Warrior of Atlantis", 5387),
        DiscardToSearch(8022, "Assault Beast", 8058),
        DiscardToSearch(7106, "Gravekeeper's Commandant", 5533),
        T.FromZone(7209, "Neo Space Pathfinder", EffectOrigin.Hand, new EffectOption([new AddFromDeckOrGraveyardStep(Named(6775))], new SendSelfCost(SelfDestination.Discard))),

        T.Trap(3695, "Twin Vortex", [new DestroyTargetsStep()], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp, cardFilter: OfType(Machine)),
            new FieldTarget(FieldCardKind.Monster, TargetSide.Opponent)]),
        T.Trap(7476, "Offering to the Snake Deity", [new DestroyTargetsStep()], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp,
            cardFilter: OfType(Reptile)), new FieldTarget(FieldCardKind.Any, TargetSide.Opponent, count: 2)]),
        T.Trap(8682, "Lair Wire", [new DestroyTargetsStep()], new BanishFromGraveyardCost(OfType(Insect)), [new FieldTarget(FieldCardKind.Monster, TargetSide.Opponent)]),
        T.Trap(9697, "Gravelstorm", [new ReturnTargetsToHandStep()], targets: [new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Own),
            new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Opponent)]),
        T.QuickPlay(8977, "Double Cyclone", [new DestroyTargetsStep()], targets: [new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Own),
            new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Opponent)]),
        T.Normal(4838, "Remove Trap", [new DestroyTargetsStep()], targets: [new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Either, FieldCardFilter.FaceUp,
            cardFilter: new CardCategoryFilter(CardCategory.Trap))]),
        T.QuickPlay(9924, "Galaxy Storm", [new DestroyTargetsStep()], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp,
            cardFilter: new XyzMonsterFilter()) { MaxXyzMaterials = 0 }]),
        T.Trap(6519, "Weed Out", [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUpAttack, Level(3))], new PayLifePointsCost(500)),
        T.Normal(5609, "Dark Core", [new BanishFieldTargetsStep()], new DiscardCost(), [FaceUpMonster]),

        T.Normal(9664, "Thunder Short", [new DealDamageStep(400, EffectCount.OpponentMonsters)]),
        T.Modal(4344, "Mooyan Curry", EffectKind.NormalSpell, "The player who gains LP is chosen at activation.",
            new EffectOption([new GainLifePointsStep(200)]), new EffectOption([new GainLifePointsStep(200, EffectPlayers.Opponent)])),
        T.Trap(3718, "Floral Shield", [new NegateAttackStep(), new DrawCardsStep(1)], targets: TrapTiming.Attacker, conditions: TrapTiming.OpponentAttacks),
        T.Trap(5102, "Mask of Weakness", [new ModifyTargetsUntilEndStep(-700)], targets: TrapTiming.Attacker, conditions: TrapTiming.OpponentAttacks, exceptions: EndsWithEndPhase),
        T.Trap(5490, "Hidden Soldiers", [new SpecialSummonStep(SpecialSummonSources.Hand, new AllOfFilter(new MonsterLevelFilter(4), OfAttribute(Dark)))],
            conditions: TrapTiming.OpponentSummons(SummonKinds.Normal | SummonKinds.Flip)),
        T.Trap(6817, "Hunting Instinct", [new SpecialSummonStep(SpecialSummonSources.Hand, OfType(Dinosaur))], conditions: TrapTiming.OpponentSummons(SummonKinds.Special)),
        T.Normal(3741, "Crowning of the Emperor", [new SpecialSummonStep(SpecialSummonSources.Hand | SpecialSummonSources.Deck, Named(3740))],
            new TributeCost(Named(3739), Position: FieldCardFilter.FaceUp)),
        T.Normal(5909, "Ultra Evolution Pill", [new SpecialSummonStep(SpecialSummonSources.Hand, OfType(Dinosaur))], new TributeCost(OfType(Reptile))),
        T.Normal(8337, "Advance Draw", [new DrawCardsStep(2)], new TributeCost(new MonsterLevelFilter(12, minLevel: 8), Position: FieldCardFilter.FaceUp)),
        T.Normal(9933, "Creeping Darkness", Search(new AllOfFilter(Level(4), OfAttribute(Dark))), new BanishFromGraveyardCost(OfAttribute(Dark), 2)),

        T.Normal(9665, "Aqua Jet", [new ModifyTargetsStep(1000)], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp,
            cardFilter: OfType(Fish, SeaSerpent, Aqua))]),
        T.Trap(6445, "Rising Energy", [new ModifyTargetsUntilEndStep(1500)], new DiscardCost(), [FaceUpMonster], exceptions: EndsWithEndPhase),
        Boost(7230, "Shield Spear", trap: true, 400, 400),
        Boost(4849, "Reinforcements", trap: true, 500, 0),
        Boost(4850, "Castle Walls", trap: true, 0, 500),
        Boost(4912, "Snake Fang", trap: true, 0, -500),
        Boost(4906, "The Reliable Guardian", trap: false, 0, 700),

        T.Trap(5397, "Disappear", [new BanishTargetsStep()], targets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Opponent, new AnyCardFilter())]),
        T.Normal(4845, "Gravedigger Ghoul", [new BanishTargetsStep()], targets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Opponent, new AnyMonsterFilter(), 1, 2)]),
        T.Normal(7751, "Recycling Batteries", [new AddTargetsToHandStep()], targets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own,
            new AllOfFilter(OfType(Thunder), new MonsterTraitFilter(MonsterStat.Attack, 1500)), 2, 2)]),
        T.Trap(10561, "Dragoncarnation", [new AddTargetsToHandStep()], targets: [new ZoneTarget(CardZone.Banished, TargetSide.Own, OfType(Dragon))]),
        T.Normal(8210, "Psychic Path", [new AddTargetsToHandStep()], new PayLifePointsCost(800), [new ZoneTarget(CardZone.Banished, TargetSide.Own, OfType(Psychic), 1, 2)]),
        T.Normal(9847, "Fish and Swaps", [new AddTargetsToHandStep()], new DiscardCost(), [new ZoneTarget(CardZone.Banished, TargetSide.Own, OfType(Fish, SeaSerpent, Aqua), 2, 2)]),
        T.Trap(7789, "Psychic Overload", [new ShuffleTargetsIntoDeckStep(StopUnlessAll: true), new DrawCardsStep(2)],
            targets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, OfType(Psychic), 3, 3)], exceptions: "Draws only if all 3 targets are shuffled into the Deck."),

        .. BoardRemovalDefinitions.Cards(T),
        .. ArchetypeInteractionDefinitions.Cards(T)
    ]);

    public static CardFamily Family { get; } = CardTable.Family(FamilyId, BatchId, ["ShortTextCardFamilyTests", "CardPoolTests"], Cards);
}
