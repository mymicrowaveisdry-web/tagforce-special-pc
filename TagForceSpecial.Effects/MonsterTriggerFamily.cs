using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Effect monsters whose effect triggers when they leave the field.</summary>
public static class MonsterTriggerFamily
{
    public const string FamilyId = "monster-trigger-definition";
    public const string BatchId = "tfsp-family-batch-10c-05";

    private const uint Light = 1, Dark = 2, Water = 3, Fire = 4, Earth = 5, Wind = 6;
    private const string Cleanup = "Spell Speed 1 trigger link. Negation skips it; the card stays in the Graveyard.";
    private const string FloaterExceptions = "Monsters that cannot be Special Summoned from the Deck by an effect are never eligible.";

    private sealed record Entry(DefinitionCardProfile Card, TriggerEffectProfile Trigger);

    private static Entry Make(uint id, string name, string operation, EffectStep[] steps, MonsterEventKind kind, TriggerRequirement requirement,
        TriggerSourceLocation location, TriggerEventCause cause, string conditions, string choices, string exceptions = "none")
    {
        var definition = new EffectDefinition($"monster-trigger-{id}-v1", id, EffectKind.MonsterTrigger, steps);
        return new(new(id, name, CardKind.Monster, definition, operation, conditions, "none", "none (not targeting).", choices,
                "Default.", Cleanup, exceptions, [], []),
            new(definition, kind, EffectRelation.Self, requirement, location, TriggerTiming.When, cause));
    }

    private static Entry Search(uint id, string name, MonsterStat stat) =>
        Make(id, name, $"Mandatory trigger: add 1 monster with 1500 or less {(stat == MonsterStat.Attack ? "ATK" : "DEF")} from your Deck to your hand.",
            [new AddFromDeckToHandStep(new MonsterTraitFilter(stat, 1500))], MonsterEventKind.SentToGraveyard, TriggerRequirement.Mandatory,
            TriggerSourceLocation.GraveyardSelf, TriggerEventCause.FromField, "Sent from the field to the Graveyard, by any cause.",
            "At resolution, choose among matching Deck cards; then the Deck is shuffled.", "Variable (\"?\") stats never match.");

    private static Entry Floater(uint id, string name, string attribute, uint code) =>
        Make(id, name, $"Optional trigger: Special Summon 1 {attribute} monster with 1500 or less ATK from your Deck in face-up Attack Position.",
            [new SpecialSummonStep(SpecialSummonSources.Deck, new MonsterTraitFilter(MonsterStat.Attack, 1500, attributeCode: code,
                excludedCardIds: TfspSummonRestrictions.DeckSummonBlockedIds), BattlePosition.FaceUpAttack)],
            MonsterEventKind.SentToGraveyard, TriggerRequirement.Optional, TriggerSourceLocation.GraveyardSelf, TriggerEventCause.ByBattle,
            "Destroyed by battle and sent to the Graveyard; needs a matching Deck monster and a free zone.",
            "Activate or decline; at resolution choose the monster and zone; then the Deck is shuffled.", FloaterExceptions);

    private static Entry Swarm(uint id, string name, TriggerRequirement requirement, string operation, EffectStep[] first, BattlePosition? position, int minCount) =>
        Make(id, name, operation, [.. first, new SpecialSummonStep(SpecialSummonSources.Deck, new CardIdFilter([id]), position, anyNumber: true, minCount: minCount)],
            MonsterEventKind.SentToGraveyard, requirement, TriggerSourceLocation.GraveyardSelf, TriggerEventCause.ByBattle,
            "Destroyed by battle and sent to the Graveyard.", "Choose monsters and zones one at a time; they are Summoned together, then the Deck is shuffled once.");

    private static readonly Entry[] Entries =
    [
        Search(4054, "Sangan", MonsterStat.Attack),
        Search(4580, "Witch of the Black Forest", MonsterStat.Defense),
        Make(5363, "Zolga", "Mandatory trigger: this card's owner gains 2000 LP.", [new GainLifePointsStep(2000)], MonsterEventKind.TributedForSummon,
            TriggerRequirement.Mandatory, TriggerSourceLocation.GraveyardSelf, TriggerEventCause.Any,
            "Tributed for a Tribute Summon that succeeds; a negated Summon does not trigger it.", "none"),
        Make(5966, "Atomic Firefly", "Mandatory trigger: inflict 1000 damage to the opponent of this card's owner.", [new DealDamageStep(1000)], MonsterEventKind.Destroyed,
            TriggerRequirement.Mandatory, TriggerSourceLocation.DestroyedSelf, TriggerEventCause.ByBattle, "Destroyed by battle.", "none",
            "If its owner destroys it while the opponent controls it, the damage still goes to the owner's opponent."),
        Floater(4930, "Mystic Tomato", "DARK", Dark),
        Floater(4924, "Shining Angel", "LIGHT", Light),
        Floater(4915, "Giant Rat", "EARTH", Earth),
        Floater(4917, "UFO Turtle", "FIRE", Fire),
        Floater(4926, "Mother Grizzly", "WATER", Water),
        Floater(4927, "Flying Kamakiri #1", "WIND", Wind),
        Swarm(6247, "Hyena", TriggerRequirement.Optional, "Optional trigger: Special Summon any number of copies of this card from your Deck.", [], null, 1),
        Swarm(4921, "Giant Germ", TriggerRequirement.Mandatory, "Mandatory trigger: inflict 500 damage to your opponent, then you can Special Summon any number of copies in face-up Attack Position.",
            [new DealDamageStep(500)], BattlePosition.FaceUpAttack, 0),
        Swarm(4922, "Nimble Momonga", TriggerRequirement.Mandatory, "Mandatory trigger: gain 1000 LP, then you can Special Summon any number of copies in face-down Defense Position.",
            [new GainLifePointsStep(1000)], BattlePosition.FaceDownDefense, 0)    ];

    public static IReadOnlyList<DefinitionCardProfile> Cards { get; } = Array.AsReadOnly(Entries.Select(e => e.Card).ToArray());

    public static IReadOnlyList<TriggerEffectProfile> Triggers { get; } = Array.AsReadOnly(Entries.Select(e => e.Trigger).ToArray());

    public static IReadOnlyDictionary<uint, DuelCardRules> Rules { get; } = Cards.ToFrozenDictionary(c => c.CardId,
        _ => new DuelCardRules(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true));

    public static IReadOnlyDictionary<uint, CardStaticMetadata> Reviewed { get; } = Cards.ToFrozenDictionary(c => c.CardId,
        c => new CardStaticMetadata(c.Kind, new(CardEffectState.Referenced, [c.Definition.EffectId])));

    /// <summary>Throws when the catalog's metadata or decoded kind codes differ from this family.</summary>
    public static void ValidateCatalog(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 is { } hash && hash != TfspCardMetadata.CardInfoSha256)
            throw new InvalidDataException("This family has not been reviewed against this source build.");
        var byId = catalog.Cards.ToDictionary(c => c.GameCardId);
        foreach (var card in Cards)
        {
            if (!byId.TryGetValue(card.CardId, out var actual) || actual.Name != card.Name || actual.Kind != card.Kind ||
                actual.Effects is not { State: CardEffectState.Referenced } effects || !effects.References.SequenceEqual([card.Definition.EffectId]) ||
                actual.Codes is { } codes && (codes.GetterKind != 1u || codes.Icon != 0u) || actual.Statistics.Level > 4)
                throw new InvalidDataException($"Card {card.CardId} metadata differs from this family.");
        }
    }
}
