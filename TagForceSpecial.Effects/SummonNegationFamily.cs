using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Counter Traps that negate a Summon before it happens.</summary>
public static class SummonNegationFamily
{
    public const string FamilyId = "summon-negation-definition";
    public const string BatchId = "tfsp-family-batch-10c-04d-3";

    private const string Timing = "Set card, not the turn it was set: the window of a Normal, Tribute, Flip or Special Summon attempt (opened only when a negator can answer); Spell Speed 3.";
    private const string Negation = "Negates the Summon; the monster is destroyed and sent to its owner's Graveyard without entering the field. Ordinary cleanup.";
    private const string NoFlip = "A negated Flip Summon sends the still face-down monster to the Graveyard.";

    private static DefinitionCardProfile Card(uint id, string name, string operation, MonsterWouldBeSummonedCondition condition,
        EffectCost? cost, string costs, string requirement, string negation = Negation) =>
        new(id, name, CardKind.Trap,
            new($"summon-negation-{id}-v1", id, EffectKind.CounterTrap, [new NegateSummonStep()], [condition], cost),
            operation, $"{Timing} {requirement}", costs, "The Summon attempt (or the preceding activation).",
            cost is TributeCost ? "Tribute at activation." : "none", "Default.", negation, NoFlip, [], []);

    public static IReadOnlyList<DefinitionCardProfile> Cards { get; } = Array.AsReadOnly(new[]
    {
        Card(4864, "Horn of Heaven", "Counter Trap: when a monster would be Summoned, Tribute 1 monster; negate the Summon and destroy that monster.",
            new(SummonKinds.Any), new TributeCost(), "Tribute 1 monster you control (never refunded).",
            "A monster would be Normal, Flip or Special Summoned by either player; needs a monster to Tribute."),
        Card(6918, "Black Horn of Heaven", "Counter Trap: when the opponent would Special Summon a monster, negate it and destroy that monster.",
            new(SummonKinds.Special, ByOpponent: true), null, "none", "The opponent would Special Summon a monster."),
        Card(4861, "Solemn Judgment", "Counter Trap: when a monster would be Summoned or a Spell/Trap is activated, pay half your LP; negate it and destroy that card.",
            new(SummonKinds.Any, OrSpellTrapActivation: true), new PayHalfLifePointsCost(),
            "Pay half your LP, rounded down (never refunded).",
            "A monster would be Normal, Flip or Special Summoned, or as a chain response to a Spell/Trap activation.",
            Negation[..^"Ordinary cleanup.".Length] + "As a chain response, negates that activation and destroys the card. Ordinary cleanup.")
    });

    public static IReadOnlyList<EffectDefinition> Definitions { get; } = Array.AsReadOnly(Cards.Select(c => c.Definition).ToArray());

    public static IReadOnlyDictionary<uint, DuelCardRules> Rules { get; } = Cards.ToFrozenDictionary(c => c.CardId,
        _ => new DuelCardRules(CardCategory.Trap, AllowsSpellTrapSet: true));

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
                actual.Codes is { } codes && (codes.GetterKind != 14u || codes.Icon != 1u))
                throw new InvalidDataException($"Card {card.CardId} metadata differs from this family.");
        }
    }
}
