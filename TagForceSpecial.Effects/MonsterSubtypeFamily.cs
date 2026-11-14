using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Flip, Spirit and Gemini monsters built on trigger definitions.</summary>
public static class MonsterSubtypeFamily
{
    public const string FamilyId = "monster-subtype-definition";
    public const string BatchId = "tfsp-family-batch-10c-06";

    private const uint FlipKind = 24, SpiritKind = 7, GeminiKind = 9;
    private const string Cleanup = "Spell Speed 1 trigger link. Negation skips it; a target that left is not replaced.";

    /// <summary>Every monster with the Spirit kind code.</summary>
    public static IReadOnlyList<uint> SpiritIds { get; } = Array.AsReadOnly(new uint[]
    {
        5373, 5374, 5375, 5376, 5377, 5378, 5379, 5380, 5381, 5382, 5414, 5780,
        7677, 7728, 8572, 8645, 9328, 9592, 9751, 9982, 10766, 10767, 10923
    });

    private sealed record Entry(DefinitionCardProfile Card, EffectDefinition[] Definitions, TriggerEffectProfile[] Triggers, uint GetterKind, DuelCardRules Rules);

    private static readonly MonsterTargetPolicy AnyMonster = new(EffectRelation.Any, requireFaceUpAtResolution: false, allowFaceDown: true);
    private static readonly MonsterTargetPolicy OpponentMonster = new(EffectRelation.Opponent, requireFaceUpAtResolution: false, allowFaceDown: true);

    private static DuelCardRules Rules(bool cannotBeSpecialSummoned = false, bool gemini = false, bool tribute = false) =>
        new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeSummon: tribute, AllowsTributeForSummon: true,
            CannotBeSpecialSummoned: cannotBeSpecialSummoned, IsGemini: gemini);

    private static DefinitionCardProfile Profile(uint id, string name, EffectDefinition definition, string operation, string conditions, string targets, string choices, string exceptions = "none") =>
        new(id, name, CardKind.Monster, definition, operation, conditions, "none", targets, choices, "Default.", Cleanup, exceptions, [], []);

    private static Entry Flip(uint id, string name, string operation, EffectStep[] steps, string targets = "none (not targeting).",
        MonsterTargetPolicy? policy = null, ZoneTarget? zone = null)
    {
        EffectTarget[]? fields = policy is not null ? [new TriggerTargetedMonster()] : zone is not null ? [zone] : null;
        var definition = new EffectDefinition($"subtype-{id}-flip-v1", id, EffectKind.MonsterTrigger, steps, fieldTargets: fields);
        return new(Profile(id, name, definition, $"Mandatory flip effect: {operation}", "Flipped face-up by a Flip Summon or by battle.", targets, fields is null ? "none" : "Target when activated."),
            [definition], [new(definition, MonsterEventKind.FlippedFaceUp, EffectRelation.Self, TriggerRequirement.Mandatory, target: policy)], FlipKind, Rules());
    }

    private static Entry Spirit(uint id, string name, string operation, EffectStep[] steps, TriggerRequirement requirement, bool tribute = false)
    {
        var returns = new EffectDefinition($"subtype-{id}-return-v1", id, EffectKind.MonsterTrigger, [new ReturnTargetsToHandStep()], fieldTargets: [new TriggerSourceMonster()]);
        var summoned = new EffectDefinition($"subtype-{id}-summoned-v1", id, EffectKind.MonsterTrigger, steps);
        var flipped = new EffectDefinition($"subtype-{id}-flipped-v1", id, EffectKind.MonsterTrigger, steps);
        return new(Profile(id, name, summoned, $"{operation} Returns to the hand in the End Phase of a turn it was Normal Summoned or flipped face-up.",
                "Normal Summoned or flipped face-up.", "none (not targeting).", requirement == TriggerRequirement.Optional ? "Activate or decline." : "none", "Cannot be Special Summoned."),
            [summoned, flipped, returns],
            [new(summoned, MonsterEventKind.NormalSummoned, EffectRelation.Self, requirement, timing: TriggerTiming.When),
             new(flipped, MonsterEventKind.FlippedFaceUp, EffectRelation.Self, requirement, timing: TriggerTiming.When),
             new(returns, MonsterEventKind.EndPhaseAfterSummonOrFlip, EffectRelation.Self, TriggerRequirement.Mandatory)],
            SpiritKind, Rules(cannotBeSpecialSummoned: true, tribute: tribute));
    }

    private static Entry Gemini(uint id, string name, string operation, EffectStep[] steps)
    {
        var definition = new EffectDefinition($"subtype-{id}-gemini-v1", id, EffectKind.MonsterTrigger, steps);
        return new(Profile(id, name, definition, $"Once Normal Summoned again: {operation}", "Destroys an opponent's monster by battle and sends it to the Graveyard, with its effect gained.",
                "none (not targeting).", "none", "A monster with its effect not gained is still reviewed as an Effect Monster by Normal-only lists."),
            [definition], [new(definition, MonsterEventKind.DestroyedOpponentByBattle, EffectRelation.Self, TriggerRequirement.Mandatory, requiresGeminiActive: true)],
            GeminiKind, Rules(gemini: true));
    }

    private static ZoneTarget OwnGraveyard(CardCategory category) => new(CardZone.Graveyard, TargetSide.Own, new CardCategoryFilter(category));

    private static readonly Entry[] Entries =
    [
        Flip(4546, "Skelengel", "draw 1 card.", [new DrawCardsStep(1)]),
        Flip(5413, "Poison Mummy", "inflict 500 damage to your opponent.", [new DealDamageStep(500)]),
        Flip(4507, "Man-Eater Bug", "target 1 monster on the field; destroy it.", [new DestroyTargetsStep()], "1 monster on the field, face-up or face-down.", AnyMonster),
        Flip(5651, "Old Vindictive Magician", "target 1 monster your opponent controls; destroy it.", [new DestroyTargetsStep()], "1 monster your opponent controls.", OpponentMonster),
        Flip(4547, "Hane-Hane", "target 1 monster on the field; return it to the hand.", [new ReturnTargetsToHandStep()], "1 monster on the field, face-up or face-down.", AnyMonster),
        Flip(5818, "Gale Lizard", "target 1 monster your opponent controls; return it to the hand.", [new ReturnTargetsToHandStep()], "1 monster your opponent controls.", OpponentMonster),
        Flip(5511, "Gravekeeper's Guard", "target 1 monster your opponent controls; return it to the hand.", [new ReturnTargetsToHandStep()], "1 monster your opponent controls.", OpponentMonster),
        Flip(4434, "Magician of Faith", "target 1 Spell in your Graveyard; add it to your hand.", [new AddTargetsToHandStep()], "1 Spell in your Graveyard.", zone: OwnGraveyard(CardCategory.Spell)),
        Flip(4108, "Mask of Darkness", "target 1 Trap in your Graveyard; add it to your hand.", [new AddTargetsToHandStep()], "1 Trap in your Graveyard.", zone: OwnGraveyard(CardCategory.Trap)),
        Spirit(5414, "Dark Dust Spirit", "Destroys all other face-up monsters.", [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp)], TriggerRequirement.Mandatory, tribute: true),
        Spirit(10766, "Aratama", "You can add 1 Spirit monster from your Deck to your hand, except this card's name.", [new AddFromDeckToHandStep(new CardIdFilter(SpiritIds.Except([10766u])))], TriggerRequirement.Optional),
        Gemini(7198, "Lucky Pied Piper", "draw 1 card.", [new DrawCardsStep(1)])
    ];

    public static IReadOnlyList<DefinitionCardProfile> Cards { get; } = Array.AsReadOnly(Entries.Select(e => e.Card).ToArray());

    public static IReadOnlyList<TriggerEffectProfile> Triggers { get; } = Array.AsReadOnly(Entries.SelectMany(e => e.Triggers).ToArray());

    public static IReadOnlyDictionary<uint, DuelCardRules> CardRules { get; } = Entries.ToFrozenDictionary(e => e.Card.CardId, e => e.Rules);

    public static IReadOnlyDictionary<uint, CardStaticMetadata> Reviewed { get; } = Entries.ToFrozenDictionary(e => e.Card.CardId,
        e => new CardStaticMetadata(e.Card.Kind, new(CardEffectState.Referenced, e.Definitions.Select(d => d.EffectId).ToArray())));

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
                actual.Effects is not { State: CardEffectState.Referenced } effects || !effects.References.SequenceEqual(entry.Definitions.Select(d => d.EffectId)) ||
                actual.Codes is { } codes && (codes.GetterKind != entry.GetterKind || codes.Icon != 0u))
                throw new InvalidDataException($"Card {card.CardId} metadata differs from this family.");
        }
        foreach (uint id in SpiritIds)
            if (!byId.TryGetValue(id, out var spirit) || spirit.Codes is { } codes && codes.GetterKind != SpiritKind)
                throw new InvalidDataException($"Card {id} is not a Spirit monster.");
    }
}
