using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Representative cards that stay on the field or act from it: Field, Equip, revival, negation, Toon, Union, activated and counter effects.</summary>
public static class LastingCardFamily
{
    public const string FamilyId = "lasting-card-definition";
    public const string BatchId = "tfsp-family-batch-10c-07";

    private const uint SpellKind = 13, TrapKind = 14, EffectMonsterKind = 1, UnionKind = 8, ToonKind = 28;
    private const uint IconEquip = 3, IconField = 2, IconContinuous = 4;
    private const uint Beastwarrior = 12, Warrior = 15, Fiend = 3, Spellcaster = 18, Fairy = 17;
    private const uint Machine = 7, XHeadCannon = 5504, YDragonHead = 5552, ZMetalTank = 5555, HeavyMech = 6181;
    private const string Cleanup = "Activation negated: sent to the Graveyard. Effect negated: stays and does not apply.";
    private const string MonsterCleanup = "Negation skips the link; the monster stays unless the negating effect destroys it.";

    private sealed record Entry(DefinitionCardProfile Card, EffectDefinition[] Definitions, TriggerEffectProfile[] Triggers, uint GetterKind, uint Icon, DuelCardRules Rules);

    private static readonly DuelCardRules SpellRules = new(CardCategory.Spell, AllowsSpellTrapSet: true);
    private static readonly DuelCardRules TrapRules = new(CardCategory.Trap, AllowsSpellTrapSet: true);
    private static readonly DuelCardRules MonsterRules = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
    private static readonly DuelCardRules ToonRules = new(CardCategory.Monster, AllowsBasicBattle: true, AllowsTributeForSummon: true, IsToon: true);

    // Toons have no activated effect; their entries point at the shared rules they follow.
    private static readonly EffectDefinition ToonRulesAnchor = new("lasting-toon-rules-v1", 3834, EffectKind.ContinuousSpell, [], isMarker: true);

    private static readonly FieldTarget FaceUpMonster = new(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp);
    private static ZoneTarget OwnGraveyardMonster() => new(CardZone.Graveyard, TargetSide.Own, new AnyMonsterFilter(), mustBeSummonable: true);

    private static DefinitionCardProfile Profile(uint id, string name, CardKind kind, EffectDefinition definition, string operation, string conditions, string costs, string targets, string exceptions = "none") =>
        new(id, name, kind, definition, operation, conditions, costs, targets, targets == "none" ? "none" : "Target when activated.", "Default.",
            kind == CardKind.Monster ? MonsterCleanup : Cleanup, exceptions, [], []);

    private static Entry Field(uint id, string name, string operation, params LastingStatModifier[] modifiers)
    {
        var definition = new EffectDefinition($"lasting-{id}-field-v1", id, EffectKind.FieldSpell, [], lastingModifiers: modifiers);
        return new(Profile(id, name, CardKind.Spell, definition, operation, "Own Main Phase; placed in the Field Zone.", "none", "none",
            "A new Field Spell sends its controller's old one to the Graveyard; that card is not destroyed."), [definition], [], SpellKind, IconField, SpellRules);
    }

    private static Entry Equip(uint id, string name, string operation, int perCount, LastingCount count)
    {
        var definition = new EffectDefinition($"lasting-{id}-equip-v1", id, EffectKind.EquipSpell, [new EquipTargetStep()], fieldTargets: [FaceUpMonster],
            lastingModifiers: [new LastingStatModifier(TargetSide.Either, perCount, perCount, equippedOnly: true, perCount: count)]);
        return new(Profile(id, name, CardKind.Spell, definition, operation, "Own Main Phase, with a face-up monster to equip.", "none", "1 face-up monster on the field.",
            "Destroyed when the equipped monster leaves the field or is flipped face-down."), [definition], [], SpellKind, IconEquip, SpellRules);
    }

    /// <summary>A plain Equip that also activates from the Graveyard when sent there from the field.</summary>
    private static Entry EquipWithGraveyardEffect(uint id, string name, int attack, int defense, string operation, EffectStep[] steps, TriggerRequirement requirement,
        string costs = "none", int lifePoints = 0, bool tribute = false)
    {
        var equip = new EffectDefinition($"lasting-{id}-equip-v1", id, EffectKind.EquipSpell, [new EquipTargetStep()], fieldTargets: [FaceUpMonster],
            lastingModifiers: [new LastingStatModifier(TargetSide.Either, attack, defense, equippedOnly: true)]);
        var trigger = new EffectDefinition($"lasting-{id}-graveyard-v1", id, EffectKind.MonsterTrigger, steps);
        return new(Profile(id, name, CardKind.Spell, equip, operation, "Own Main Phase, with a face-up monster to equip.", costs, "1 face-up monster on the field.",
                "Its Graveyard effect does not activate if its activation was negated."),
            [equip, trigger], [new(trigger, MonsterEventKind.SentToGraveyard, EffectRelation.Self, requirement, TriggerSourceLocation.GraveyardSelf, TriggerTiming.When,
                TriggerEventCause.FromField, lifePointCost: lifePoints) { TributeCost = tribute }], SpellKind, IconEquip, SpellRules);
    }

    private static Entry Union(uint id, string name, int bonus, CardFilter targets, DestructionCause replaces = DestructionCause.Battle)
    {
        var definition = new EffectDefinition($"lasting-{id}-union-v1", id, EffectKind.MonsterIgnition, [new UnionEquipStep()],
            fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp, cardFilter: targets, noUnionEquipped: true)],
            lastingModifiers: [new LastingStatModifier(TargetSide.Either, bonus, bonus, equippedOnly: true)], oncePerTurn: true, unionReplaces: replaces);
        return new(Profile(id, name, CardKind.Monster, definition, $"Equip to a listed monster you control, or unequip and Special Summon in Attack Position. Equipped monster gains {bonus} ATK and DEF.",
            "Own Main Phase; once per turn across both options.", "none", "1 listed monster you control without a Union equip.",
            replaces == DestructionCause.Any
                ? "If the equipped monster would be destroyed, this card is destroyed instead. The unequip uses the lowest free Monster Zone."
                : "If the equipped monster would be destroyed by battle, this card is destroyed instead. The unequip uses the lowest free Monster Zone."), [definition], [], UnionKind, 0, MonsterRules);
    }

    private static Entry Toon(uint id, string name, uint tributes) =>
        new(Profile(id, name, CardKind.Monster, ToonRulesAnchor, $"Special Summoned from the hand by Tributing {tributes}. Cannot attack the turn it is Summoned; pay 500 LP to declare an attack. Attacks directly unless the opponent controls a Toon, which it must then attack.",
                "You control a face-up Toon World.", "Tributes for the procedure; 500 LP per attack.", "none", "Destroyed when any Toon World on the field is destroyed."),
            [], [], ToonKind, 0, ToonRules);

    private static EffectDefinition BreakerEffect { get; } = new("lasting-5655-destroy-v1", 5655, EffectKind.MonsterIgnition, [new DestroyTargetsStep()],
        cost: new RemoveCounterCost(1), fieldTargets: [new FieldTarget(FieldCardKind.SpellTrap)], attackPerCounter: 300);
    private static EffectDefinition BreakerCounter { get; } = new("lasting-5655-counter-v1", 5655, EffectKind.MonsterTrigger, [new PlaceCounterStep(1, 1)],
        fieldTargets: [new TriggerSourceMonster()]);

    private static EffectDefinition Marker(uint id) => new($"lasting-{id}-toon-world-v1", id, EffectKind.ContinuousSpell, [], cost: new PayLifePointsCost(1000), isMarker: true);
    private static readonly EffectDefinition ToonWorldOne = Marker(3834), ToonWorldTwo = Marker(4798);

    private static EffectDefinition CallOfTheHaunted { get; } = new("lasting-4989-revive-v1", 4989, EffectKind.ContinuousTrap,
        [new SpecialSummonTargetStep(BattlePosition.FaceUpAttack, RevivalLink.Haunted)], fieldTargets: [OwnGraveyardMonster()]);
    private static EffectDefinition PrematureBurial { get; } = new("lasting-4966-revive-v1", 4966, EffectKind.EquipSpell,
        [new SpecialSummonTargetStep(BattlePosition.FaceUpAttack, RevivalLink.Equip)], cost: new PayLifePointsCost(800), fieldTargets: [OwnGraveyardMonster()]);
    private static EffectDefinition SkillDrain { get; } = new("lasting-5740-negate-v1", 5740, EffectKind.ContinuousTrap, [], cost: new PayLifePointsCost(1000),
        negation: LastingNegation.FaceUpMonsters);
    private static EffectDefinition ImperialOrder { get; } = new("lasting-4960-negate-v1", 4960, EffectKind.ContinuousTrap, [], negation: LastingNegation.Spells, upkeepLifePoints: 700);
    private static EffectDefinition MaskOfBrutality { get; } = new("lasting-5108-equip-v1", 5108, EffectKind.EquipSpell, [new EquipTargetStep()], fieldTargets: [FaceUpMonster],
        lastingModifiers: [new LastingStatModifier(TargetSide.Either, 1000, -1000, equippedOnly: true)], upkeepLifePoints: 1000);
    private static EffectDefinition MagicReflector { get; } = new("lasting-5395-counter-v1", 5395, EffectKind.NormalSpell, [new PlaceCounterStep(1, int.MaxValue, protective: true)],
        fieldTargets: [new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Own, FieldCardFilter.FaceUp, cardFilter: new CardCategoryFilter(CardCategory.Spell))]);
    private static EffectDefinition CannonSoldier { get; } = new("lasting-4518-burn-v1", 4518, EffectKind.MonsterIgnition, [new DealDamageStep(500)], cost: new TributeCost());
    private static EffectDefinition ExiledForce { get; } = new("lasting-5323-destroy-v1", 5323, EffectKind.MonsterIgnition, [new DestroyTargetsStep()], cost: new TributeSelfCost(),
        fieldTargets: [new FieldTarget(FieldCardKind.Monster)]);

    private static readonly Entry[] Entries =
    [
        Field(4339, "Sogen", "All Beast-Warrior and Warrior monsters gain 200 ATK and DEF.",
            new LastingStatModifier(TargetSide.Either, 200, 200, new MonsterTypeFilter([Beastwarrior, Warrior]))),
        Field(4341, "Yami", "All Fiend and Spellcaster monsters gain 200 ATK and DEF; all Fairy monsters lose 200 ATK and DEF.",
            new LastingStatModifier(TargetSide.Either, 200, 200, new MonsterTypeFilter([Fiend, Spellcaster])),
            new LastingStatModifier(TargetSide.Either, -200, -200, new MonsterTypeFilter([Fairy]))),
        Equip(5212, "United We Stand", "The equipped monster gains 800 ATK and DEF for each face-up monster you control.", 800, LastingCount.OwnFaceUpMonsters),
        Equip(5213, "Mage Power", "The equipped monster gains 500 ATK and DEF for each Spell/Trap Card you control.", 500, LastingCount.OwnSpellTraps),
        new(Profile(4989, "Call of the Haunted", CardKind.Trap, CallOfTheHaunted, "Special Summon the target in Attack Position. Linked in both directions.", "Set a turn earlier; a summonable monster in your Graveyard.", "none",
            "1 monster in your Graveyard.", "When this leaves the field, destroy that monster; when that monster is destroyed, destroy this."), [CallOfTheHaunted], [], TrapKind, IconContinuous, TrapRules),
        new(Profile(4966, "Premature Burial", CardKind.Spell, PrematureBurial, "Special Summon the target in Attack Position and equip it with this card.", "Own Main Phase; a summonable monster in your Graveyard.", "Pay 800 LP.",
            "1 monster in your Graveyard.", "When this is destroyed, destroy the equipped monster."), [PrematureBurial], [], SpellKind, IconEquip, SpellRules),
        new(Profile(5740, "Skill Drain", CardKind.Trap, SkillDrain, "Negates the effects of all face-up monsters while they are face-up; they can still be activated.", "Set a turn earlier.", "Pay 1000 LP.", "none"), [SkillDrain], [], TrapKind, IconContinuous, TrapRules),
        new(Profile(4960, "Imperial Order", CardKind.Trap, ImperialOrder, "Negates all Spell effects on the field. Each of your Standby Phases, pay 700 LP or destroy this card.", "Set a turn earlier.", "none", "none",
            "The upkeep is settled by a command before leaving the Standby Phase, not as a chain link."), [ImperialOrder], [], TrapKind, IconContinuous, TrapRules),
        new(Profile(3834, "Toon World", CardKind.Spell, ToonWorldOne, "Stays on the field; Toon monsters depend on it.", "Own Main Phase.", "Pay 1000 LP.", "none"), [ToonWorldOne], [], SpellKind, IconContinuous, SpellRules with { IsToonWorld = true }),
        new(Profile(4798, "Toon World", CardKind.Spell, ToonWorldTwo, "Stays on the field; Toon monsters depend on it.", "Own Main Phase.", "Pay 1000 LP.", "none"), [ToonWorldTwo], [], SpellKind, IconContinuous, SpellRules with { IsToonWorld = true }),
        Toon(4734, "Toon Mermaid", 0),
        Toon(4735, "Toon Summoned Skull", 1),
        Toon(4773, "Blue-Eyes Toon Dragon", 2),
        Union(YDragonHead, "Y-Dragon Head", 400, new CardIdFilter([XHeadCannon])),
        Union(ZMetalTank, "Z-Metal Tank", 600, new CardIdFilter([XHeadCannon, YDragonHead])),
        Union(HeavyMech, "Heavy Mech Support Platform", 500, new MonsterTypeFilter([Machine]), DestructionCause.Any),
        EquipWithGraveyardEffect(4317, "Black Pendant", 500, 0, "The equipped monster gains 500 ATK. When sent from the field to the Graveyard: inflict 500 damage to your opponent.", [new DealDamageStep(500)], TriggerRequirement.Mandatory),
        EquipWithGraveyardEffect(4320, "Horn of the Unicorn", 700, 700, "The equipped monster gains 700 ATK and DEF. When sent from the field to the Graveyard: place it on top of the Deck.", [new ReturnSourceToDeckTopStep()], TriggerRequirement.Mandatory),
        EquipWithGraveyardEffect(4327, "Malevolent Nuzzler", 700, 0, "The equipped monster gains 700 ATK. When sent from the field to the Graveyard: you can pay 500 LP to place it on top of the Deck.", [new ReturnSourceToDeckTopStep()], TriggerRequirement.Optional, "Graveyard effect: pay 500 LP.", lifePoints: 500),
        EquipWithGraveyardEffect(4319, "Horn of Light", 0, 800, "The equipped monster gains 800 DEF. When sent from the field to the Graveyard: you can pay 500 LP to place it on top of the Deck.", [new ReturnSourceToDeckTopStep()], TriggerRequirement.Optional, "Graveyard effect: pay 500 LP.", lifePoints: 500),
        EquipWithGraveyardEffect(4310, "Axe of Despair", 1000, 0, "The equipped monster gains 1000 ATK. When sent from the field to the Graveyard: you can Tribute 1 monster to place it on top of the Deck.", [new ReturnSourceToDeckTopStep()], TriggerRequirement.Optional, "Graveyard effect: Tribute 1 monster.", tribute: true),
        new(Profile(5108, "Mask of Brutality", CardKind.Spell, MaskOfBrutality, "The equipped monster gains 1000 ATK and loses 1000 DEF. Each of your Standby Phases, pay 1000 LP or destroy this card.", "Own Main Phase, with a face-up monster to equip.", "none", "1 face-up monster on the field.",
            "The upkeep is not an effect: it uses no chain and is still owed while this card is negated."), [MaskOfBrutality], [], SpellKind, IconEquip, SpellRules),
        new(Profile(5395, "Magic Reflector", CardKind.Spell, MagicReflector, "Place 1 counter on the target. If it would be destroyed, remove the counter instead.", "Own Main Phase, with a face-up Spell you control.", "none", "1 face-up Spell Card you control."), [MagicReflector], [], SpellKind, 0, SpellRules),
        new(Profile(4518, "Cannon Soldier", CardKind.Monster, CannonSoldier, "Inflict 500 damage to your opponent.", "Own Main Phase.", "Tribute 1 monster, which can be this card.", "none"), [CannonSoldier], [], EffectMonsterKind, 0, MonsterRules),
        new(Profile(5323, "Exiled Force", CardKind.Monster, ExiledForce, "Destroy the target.", "Own Main Phase.", "Tribute this card.", "1 monster on the field."), [ExiledForce], [], EffectMonsterKind, 0, MonsterRules),
        new(Profile(5655, "Breaker the Magical Warrior", CardKind.Monster, BreakerEffect, "When Normal Summoned, place 1 Spell Counter (max. 1). Gains 300 ATK for each. Remove 1 to destroy the target.", "Own Main Phase; a Spell Counter on it.", "Remove 1 Spell Counter.", "1 Spell/Trap Card on the field."),
            [BreakerEffect, BreakerCounter], [new(BreakerCounter, MonsterEventKind.NormalSummoned, EffectRelation.Self, TriggerRequirement.Mandatory)], EffectMonsterKind, 0, MonsterRules)
    ];

    public static IReadOnlyList<DefinitionCardProfile> Cards { get; } = Array.AsReadOnly(Entries.Select(e => e.Card).ToArray());

    /// <summary>Definitions the engine registers; trigger definitions are carried by <see cref="Triggers"/>.</summary>
    public static IReadOnlyList<EffectDefinition> Definitions { get; } =
        Array.AsReadOnly(Entries.SelectMany(e => e.Definitions).Where(d => d.Kind != EffectKind.MonsterTrigger).ToArray());

    public static IReadOnlyList<TriggerEffectProfile> Triggers { get; } = Array.AsReadOnly(Entries.SelectMany(e => e.Triggers).ToArray());

    public static IReadOnlyDictionary<uint, DuelCardRules> CardRules { get; } = Entries.ToFrozenDictionary(e => e.Card.CardId, e => e.Rules);

    /// <summary>Toons must first be Summoned by their own procedure before an effect can revive them.</summary>
    public static IReadOnlyList<SpecialSummonCardProfile> SpecialSummonCards { get; } = Array.AsReadOnly(Entries.Where(e => e.Rules.IsToon)
        .Select(e => new SpecialSummonCardProfile(e.Card.CardId, SpecialSummonSources.Graveyard | SpecialSummonSources.Banished, DuelEngine.ToonProcedureId)).ToArray());

    private static string[] References(Entry e) => e.Definitions.Length != 0 ? e.Definitions.Select(d => d.EffectId).ToArray() : [ToonRulesAnchor.EffectId];

    public static IReadOnlyDictionary<uint, CardStaticMetadata> Reviewed { get; } = Entries.ToFrozenDictionary(e => e.Card.CardId,
        e => new CardStaticMetadata(e.Card.Kind, new(CardEffectState.Referenced, References(e))));

    /// <summary>Throws when the catalog's metadata or decoded kind codes differ from this family.</summary>
    public static void ValidateCatalog(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 is { } hash && hash != TfspCardMetadata.CardInfoSha256)
            throw new InvalidDataException("This family has not been reviewed against this source build.");
        var byId = catalog.Cards.ToDictionary(c => c.GameCardId);
        foreach (var entry in Entries)
        {
            var card = entry.Card;
            if (!byId.TryGetValue(card.CardId, out var actual) || actual.Name != card.Name || actual.Kind != card.Kind ||
                actual.Effects is not { State: CardEffectState.Referenced } effects || !effects.References.SequenceEqual(References(entry)) ||
                actual.Codes is { } codes && (codes.GetterKind != entry.GetterKind || codes.Icon != entry.Icon))
                throw new InvalidDataException($"Card {card.CardId} metadata differs from this family.");
        }
    }
}
