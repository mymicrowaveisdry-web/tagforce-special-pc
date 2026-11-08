using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Normal Traps activated when a monster is successfully Summoned.</summary>
public static class SummonTrapFamily
{
    public const string FamilyId = "summon-trap-definition";
    public const string BatchId = "tfsp-family-batch-10c-04d-2";

    private const string Timing = "Set card, not the turn it was set: the summon-response window after the opponent's successful Summon command or chain; Spell Speed 2.";
    private const string TargetCleanup = "If the Summoned monster left the field, the effect does not resolve. Ordinary cleanup.";
    private const string Summoned = "The Summoned monster (bound automatically).";

    private static DefinitionCardProfile Card(uint id, string name, string operation, EffectStep[] steps, MonsterSummonedCondition condition,
        string targets, string requirement, string negation = TargetCleanup, bool bindsSummoned = true) =>
        new(id, name, CardKind.Trap,
            new($"summon-trap-{id}-v1", id, EffectKind.NormalTrap, steps, [condition],
                fieldTargets: bindsSummoned ? [new SummonedMonsterTarget()] : null),
            operation, $"{Timing} {requirement}", "none", targets, "none", "Default.", negation, "none", [], []);

    public static IReadOnlyList<DefinitionCardProfile> Cards { get; } = Array.AsReadOnly(new[]
    {
        Card(4836, "Trap Hole", "Normal Trap: target the opponent's Normal or Flip Summoned monster with 1000 or more ATK; destroy it.",
            [new DestroyTargetsStep()], new(SummonKinds.Normal | SummonKinds.Flip, Single: true, Min: 1000), Summoned,
            "The opponent Normal or Flip Summoned 1 monster with 1000 or more ATK."),
        Card(4688, "Eatgaboon", "Normal Trap: target the opponent's Normal or Flip Summoned monster with 500 or less ATK; destroy it.",
            [new DestroyTargetsStep()], new(SummonKinds.Normal | SummonKinds.Flip, Single: true, Max: 500), Summoned,
            "The opponent Normal or Flip Summoned 1 monster with 500 or less ATK."),
        Card(4687, "House of Adhesive Tape", "Normal Trap: target the opponent's Normal or Flip Summoned monster with 500 or less DEF; destroy it.",
            [new DestroyTargetsStep()], new(SummonKinds.Normal | SummonKinds.Flip, Single: true, Stat: MonsterStat.Defense, Max: 500), Summoned,
            "The opponent Normal or Flip Summoned 1 monster with 500 or less DEF."),
        Card(5400, "Bottomless Trap Hole", "Normal Trap: destroy the opponent's Summoned monsters with 1500 or more ATK and banish them.",
            [new DestroyAndBanishTargetsStep()], new(SummonKinds.Any, Min: 1500), "The Summoned monsters with 1500 or more ATK (bound automatically, not targeted).",
            "The opponent Summoned a monster with 1500 or more ATK.", "Bound monsters that left the field are skipped. Ordinary cleanup."),
        Card(5114, "Torrential Tribute", "Normal Trap: destroy all monsters on the field.",
            [new DestroyAllStep(FieldCardKind.Monster)], new(SummonKinds.Any, ByOpponent: false), "none (not targeting).",
            "A monster was Summoned; only the summoner's opponent is offered the window.",
            "Affects every monster at resolution. Ordinary cleanup.", bindsSummoned: false)
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
                actual.Codes is { } codes && (codes.GetterKind != 14u || codes.Icon != 0u))
                throw new InvalidDataException($"Card {card.CardId} metadata differs from this family.");
        }
    }
}
