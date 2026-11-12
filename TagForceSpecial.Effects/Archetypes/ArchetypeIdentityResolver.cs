using System.Collections.Frozen;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects.Archetypes;

public sealed record EffectiveCardIdentity(SourceCardId CardId, string Name, IReadOnlyList<ArchetypeId> Archetypes,
    IReadOnlyList<string> AppliedRules)
{
    public bool IsMember(ArchetypeId archetype) => Archetypes.Contains(archetype);
}

/// <summary>Resolves full-information identity against one immutable content selection and the engine's current state.</summary>
public sealed class ArchetypeIdentityResolver
{
    private readonly ArchetypeContentSnapshot snapshot;
    private readonly FrozenDictionary<uint, SourceCardId> cards;
    private readonly FrozenDictionary<SourceCardId, ArchetypeIdentityRule[]> rules;
    private readonly FrozenDictionary<ArchetypeId, ArchetypeId[]> closure;

    public ArchetypeIdentityResolver(ArchetypeLoadedContent content) : this(content.Snapshot,
        content.Snapshot.Cards.ToDictionary(c => content.GetCatalogCardId(c.CardId)
            ?? throw new InvalidDataException("Identity resolution requires a catalog mapping for every card."), c => c.CardId)) { }

    public ArchetypeIdentityResolver(ArchetypeContentSnapshot snapshot, IReadOnlyDictionary<uint, SourceCardId> catalogMappings)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(catalogMappings);
        this.snapshot = snapshot;
        if (catalogMappings.Values.Distinct().Count() != catalogMappings.Count || catalogMappings.Values.Any(id => snapshot.GetCard(id) is null))
            throw new InvalidDataException("Catalog identity mappings must be unambiguous and reference selected cards.");
        cards = catalogMappings.ToFrozenDictionary();
        rules = snapshot.Packs.SelectMany(p => p.IdentityRules.Rules).GroupBy(r => r.CardId)
            .ToFrozenDictionary(g => g.Key, g => g.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray());
        var parents = snapshot.Packs.SelectMany(p => p.Definitions.Relationships).GroupBy(r => r.Child)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Parent).ToArray());
        ArchetypeId[] Expand(ArchetypeId id)
        {
            HashSet<ArchetypeId> result = []; Stack<ArchetypeId> pending = new([id]);
            while (pending.TryPop(out var current))
                if (result.Add(current) && parents.TryGetValue(current, out var ancestors))
                    foreach (var parent in ancestors) pending.Push(parent);
            return result.OrderBy(a => a.Value, StringComparer.Ordinal).ToArray();
        }
        closure = snapshot.GetAll().ToFrozenDictionary(d => d.Id, d => Expand(d.Id));
        foreach (var group in rules.Values)
            ValidateNames(group.Where(r => r.Treatment == ArchetypeIdentityTreatment.PermanentName).ToArray());
    }

    public EffectiveCardIdentity? Resolve(DuelEngine engine, CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(engine); ArgumentNullException.ThrowIfNull(card);
        var context = engine.GetCardIdentityState(card);
        if (context is null || !cards.TryGetValue(card.GameCardId, out var id)) return null;
        var active = Rules(id).Where(r => r.Condition is null || Applies(r.Condition, context)).ToArray();
        List<string> applied = [];
        var conditionalNames = active.Where(r => r.Treatment == ArchetypeIdentityTreatment.ConditionalName).ToArray();
        var names = conditionalNames.Length == 0 ? active.Where(r => r.Treatment == ArchetypeIdentityTreatment.PermanentName).ToArray() : conditionalNames;
        string name = names.FirstOrDefault()?.Name ?? context.PrintedName;
        IEnumerable<ArchetypeId> membership = names.FirstOrDefault()?.NameArchetypes ?? snapshot.GetArchetypes(id);
        if (context.ChangedNameCardId is { } changed)
        {
            if (!cards.TryGetValue(changed, out var target)) throw new InvalidDataException("The changed name has no selected source-card mapping.");
            // A copied name uses that card's permanent identity, never its unrelated current location or effects.
            var targetNames = Rules(target).Where(r => r.Treatment == ArchetypeIdentityTreatment.PermanentName).ToArray();
            ValidateNames(targetNames);
            name = targetNames.FirstOrDefault()?.Name ?? engine.GetOriginalCardName(changed);
            membership = targetNames.FirstOrDefault()?.NameArchetypes ?? snapshot.GetArchetypes(target);
            applied.AddRange(targetNames.Select(r => r.Id));
            applied.Add("resolved-name:" + target.Value);
        }
        else
        {
            ValidateNames(names);
            applied.AddRange(names.Select(r => r.Id));
        }
        var groups = membership.SelectMany(g => closure[g]).ToHashSet();
        foreach (var rule in active.Where(r => r.Treatment is ArchetypeIdentityTreatment.PermanentArchetype or ArchetypeIdentityTreatment.ConditionalArchetype))
        {
            groups.UnionWith(closure[rule.Archetype!.Value]);
            applied.Add(rule.Id);
        }
        return new(id, name, Array.AsReadOnly(groups.OrderBy(g => g.Value, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(applied.Order(StringComparer.Ordinal).ToArray()));
    }

    private ArchetypeIdentityRule[] Rules(SourceCardId id) => rules.GetValueOrDefault(id) ?? [];
    private static bool Applies(ArchetypeIdentityCondition condition, CardIdentityState context)
    {
        bool field = context.Location is CardIdentityLocation.MonsterZone or CardIdentityLocation.SpellTrapZone or CardIdentityLocation.FieldZone or CardIdentityLocation.PendulumZone;
        return condition.Locations.Contains(context.Location) && (condition.Controller is null || condition.Controller == context.Controller) &&
            (!field || context.FaceUp || !condition.RequireFaceUp && !condition.RequireEffectsActive) &&
            (!condition.RequireEffectsActive || !context.EffectsNegated);
    }
    private static void ValidateNames(ArchetypeIdentityRule[] names)
    {
        if (names.Length < 2) return;
        var first = names[0];
        if (names.Skip(1).Any(r => r.Name != first.Name || (r.NameArchetypes is null) != (first.NameArchetypes is null) ||
            !(r.NameArchetypes ?? []).SequenceEqual(first.NameArchetypes ?? [])))
            throw new InvalidDataException("Simultaneously applicable name rules conflict; explicit precedence is required.");
    }
}
