using System.Collections;
using System.Collections.Frozen;
using System.Reflection;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Assembles authored cards into a registered family, writes their manifest text and checks them against the catalog.</summary>
internal static class AuthoredFamilies
{
    internal static CardFamily Build(string familyId, string batchId, IReadOnlyList<string> tests, IReadOnlyList<AuthoredCard> cards)
    {
        var profiles = cards.Select(Profile).ToArray();
        // Trigger definitions travel inside their trigger profiles; the engine rejects them as standalone definitions.
        var definitions = cards.SelectMany(c => c.Definitions).Where(d => d.Kind != EffectKind.MonsterTrigger).ToArray();
        return new CardFamily(familyId, batchId, Array.AsReadOnly(profiles), cards.ToFrozenDictionary(c => c.Id, c => c.Rules),
            cards.ToFrozenDictionary(c => c.Id, c => new CardStaticMetadata(c.Kind, new(CardEffectState.Referenced, c.References.ToArray()))),
            catalog => Validate(catalog, cards), tests)
        {
            Definitions = Array.AsReadOnly(definitions),
            Triggers = Array.AsReadOnly(cards.SelectMany(c => c.Triggers).ToArray()),
            RuleLocks = Array.AsReadOnly(cards.SelectMany(c => c.Locks).ToArray()),
            BattleAbilities = Array.AsReadOnly(cards.SelectMany(c => c.BattleAbilities).ToArray()),
            DestructionReplacements = Array.AsReadOnly(cards.SelectMany(c => c.Replacements).ToArray()),
            GraveyardRedirects = Array.AsReadOnly(cards.SelectMany(c => c.Redirects).ToArray()),
            DamageRules = Array.AsReadOnly(cards.SelectMany(c => c.DamageRules).ToArray()),
            PendulumCards = Array.AsReadOnly(cards.SelectMany(c => c.Scales).ToArray()),
            SpecialSummonCards = Array.AsReadOnly(cards.SelectMany(c => c.SpecialSummons).ToArray()),
            MonsterStats = Array.AsReadOnly(cards.Select(c => c.Stats).OfType<MonsterStatProfile>().ToArray()),
            TributeProcedures = Array.AsReadOnly(cards.SelectMany(c => c.TributeProcedures).ToArray()),
            Procedures = Array.AsReadOnly(cards.SelectMany(c => c.Procedures).ToArray())
        };
    }

    private static void Validate(CardCatalog catalog, IReadOnlyList<AuthoredCard> cards)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 is { } hash && hash != TfspCardMetadata.CardInfoSha256)
            throw new InvalidDataException("This family has not been reviewed against this source build.");
        var byId = catalog.Cards.ToDictionary(c => c.GameCardId);
        foreach (var card in cards)
            if (!byId.TryGetValue(card.Id, out var actual) || actual.Name != card.Name || actual.Kind != card.Kind ||
                actual.Effects is not { State: CardEffectState.Referenced } effects || !effects.References.SequenceEqual(card.References) ||
                actual.Codes is { } codes && (codes.GetterKind != card.GetterKind || codes.Icon != card.Icon))
                throw new InvalidDataException($"Card {card.Id} metadata differs from this family.");
        foreach (var scale in cards.SelectMany(c => c.Scales))
            if (byId[scale.CardId].Codes is { } codes && (codes.Field23To26 != scale.LeftScale || codes.Field27To30 != scale.RightScale))
                throw new InvalidDataException($"Card {scale.CardId} has different Pendulum Scales.");
    }

    private static DefinitionCardProfile Profile(AuthoredCard card)
    {
        // A card with only continuous rules has no effect to activate; the placeholder is never registered with an engine.
        var effect = card.Definitions.Count != 0 ? card.Definitions[0] : new EffectDefinition($"rule-lock-{card.Id}", card.Id, EffectKind.ContinuousSpell, [], isMarker: true);
        bool targets = effect.FieldTargets.Count != 0;
        string detail = effect.Kind == EffectKind.FieldSpell || effect.LastingModifiers.Count != 0 ? $" Lasting: {Describe(effect.LastingModifiers)}." : "";
        return new(card.Id, card.Name, card.Kind, effect,
            string.Join(" Or: ", card.Definitions.Select(d => $"{d.Kind}: {Describe(d.Steps)}.")) + detail + (card.Locks.Count == 0 ? "" : $" Rules: {Describe(card.Locks.Select(l => l.Lock))}.")
                + (card.Replacements.Count == 0 ? "" : $" Replacement: {Describe(card.Replacements.Select(r => r with { EffectId = "", CardId = 0 }))}.")
                + (card.Redirects.Count == 0 ? "" : $" Redirect: {Describe(card.Redirects.Select(r => r.Rule))}.")
                + (card.DamageRules.Count == 0 ? "" : $" Damage: {Describe(card.DamageRules.Select(r => r with { EffectId = "", CardId = 0 }))}.")
                + (card.BattleAbilities.Count == 0 ? "" : $" Battle: {Describe(card.BattleAbilities.Select(p => p.Abilities))}.")
                + (card.Definitions.Select(d => d.NameLimit).FirstOrDefault(l => l is not null) is { } named ? $" Limit: {named.Counts} by name, once per {(named.PerDuel ? "Duel" : "turn")}." : ""),
            card.Definitions.Count == 0 && card.BattleAbilities.Count != 0 ? "Continuous while the source is face-up and not negated."
                : card.Triggers.Count != 0 ? string.Join("; ", card.Triggers.Select(t => $"{t.Requirement} {t.Timing} on {t.EventKind} from {t.SourceLocation}, cause {t.Cause}")) + "."
                : effect.Conditions.Count == 0 ? $"Default timing for {effect.Kind}." : $"Default timing for {effect.Kind}, with {Describe(effect.Conditions)}.",
            effect.Cost is null ? "none" : $"{Describe(effect.Cost)} (never refunded).",
            targets ? Describe(effect.FieldTargets) : "none",
            card.Definitions.Any(d => d.Steps.Any(s => s is DiscardCardsStep))
                ? (targets ? "Target at activation. " : "") + "Each affected player chooses cards from their own hand at resolution; all choices are discarded together."
                : targets ? "Target at activation." : "none", "Default.",
            card.Definitions.Count == 0 && card.BattleAbilities.Count != 0 ? "Negation suspends continuous battle abilities."
                : targets ? "Negation skips the effect. Targets that no longer qualify are skipped; ordinary cleanup." : "Negation skips the effect; ordinary cleanup.",
            card.Exceptions, [], []);
    }

    /// <summary>Plain text for a Core record, read from its public properties so new steps need no extra code.</summary>
    internal static string Describe(object? value) => value switch
    {
        null => "none",
        string text => text,
        Enum or ValueType => value.ToString()!,
        DestroyTargetsStep { StopUnlessAnyDestroyed: false } => "DestroyTargets",
        DiscardCardsStep { Filter: null, Minimum: null } d => $"DiscardCards(Count={d.Count}; Players={d.Players})",
        IEnumerable list => string.Join(", ", list.Cast<object?>().Select(Describe)),
        _ => DescribeRecord(value)
    };

    private static string DescribeRecord(object value)
    {
        var type = value.GetType();
        string name = type.Name.EndsWith("Step", StringComparison.Ordinal) ? type.Name[..^4] : type.Name;
        var parts = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => $"{p.Name}={Describe(p.GetValue(value))}").ToArray();
        return parts.Length == 0 ? name : $"{name}({string.Join("; ", parts)})";
    }
}
