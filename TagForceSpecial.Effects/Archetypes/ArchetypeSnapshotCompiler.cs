using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TagForceSpecial.Cards;

namespace TagForceSpecial.Effects.Archetypes;

internal static class ArchetypeSnapshotCompiler
{
    public static ArchetypeContentSnapshot Compile(ArchetypeContentProfile profile,
        IEnumerable<ArchetypePackDocument> packs, IEnumerable<ArchetypeCardFacts> cards)
    {
        ArgumentNullException.ThrowIfNull(packs); ArgumentNullException.ThrowIfNull(cards);
        if (!Enum.IsDefined(profile)) throw new ArgumentException("Unknown content profile.", nameof(profile));
        var selected = Copy(packs).Select(Freeze).OrderBy(p => p.Manifest.PackId.Value, StringComparer.Ordinal).ToArray();
        var facts = Copy(cards).OrderBy(c => c.CardId.Value, StringComparer.Ordinal).ToArray();
        ArchetypeCardFacts.ValidateAll(facts);
        if (selected.Select(p => p.Manifest.PackId).Distinct().Count() != selected.Length)
            throw new InvalidDataException("The selected pack IDs must be unique.");
        var exports = selected.Select(p => new ArchetypeDependencyExports(p.Manifest.PackId, p.Manifest.Version,
            p.Definitions.Definitions.Select(d => d.Id).ToArray())).ToArray();
        var context = new ArchetypeValidationContext(facts.Select(c => c.CardId).ToArray(), exports);
        foreach (var pack in selected)
        {
            var issues = ArchetypePackValidator.Validate(pack, context);
            if (issues.Count != 0) throw new InvalidDataException($"{pack.Manifest.PackId}: " +
                string.Join("; ", issues.Select(i => $"{i.Path}: {i.Code}: {i.Message}")));
            if (!pack.Manifest.Profiles.Contains(profile))
                throw new InvalidDataException($"Selected pack {pack.Manifest.PackId} does not support {profile}.");
        }
        var packOrder = Topological(selected.Select(p => p.Manifest.PackId),
            selected.SelectMany(p => p.Manifest.Dependencies.Select(d => (d.PackId, p.Manifest.PackId))),
            Comparer<ArchetypePackId>.Create((a, b) => StringComparer.Ordinal.Compare(a.Value, b.Value)), "Pack dependencies");
        var byPack = selected.ToDictionary(p => p.Manifest.PackId);
        var orderedPacks = Array.AsReadOnly(packOrder.Select(id => byPack[id]).ToArray());
        var definitions = selected.SelectMany(p => p.Definitions.Definitions).OrderBy(d => d.Id.Value, StringComparer.Ordinal).ToArray();
        if (definitions.Select(d => d.Id).Distinct().Count() != definitions.Length)
            throw new InvalidDataException("Different packs cannot define the same archetype ID.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var externalIds = new HashSet<(string System, string Value)>();
        foreach (var definition in definitions)
        {
            foreach (var name in definition.Aliases.Prepend(definition.Name))
                if (!names.Add(name)) throw new InvalidDataException("Selected archetype names and aliases are ambiguous.");
            foreach (var external in definition.ExternalIds)
                if (!externalIds.Add((external.System, external.Value)))
                    throw new InvalidDataException("Selected archetypes have a conflicting external identifier.");
        }
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        var permanentNames = new Dictionary<SourceCardId, string>();
        foreach (var rule in selected.SelectMany(p => p.IdentityRules.Rules))
        {
            if (!ruleIds.Add(rule.Id)) throw new InvalidDataException("Identity rule IDs conflict across selected packs.");
            if (rule.Treatment == ArchetypeIdentityTreatment.PermanentName)
            {
                if (permanentNames.TryGetValue(rule.CardId, out var name) && name != rule.Name)
                    throw new InvalidDataException("Selected packs declare conflicting permanent names for a card.");
                permanentNames[rule.CardId] = rule.Name!;
            }
        }
        var relationships = selected.SelectMany(p => p.Definitions.Relationships).Select(r => (r.Parent, r.Child)).Distinct().ToArray();
        var archetypeOrder = Topological(definitions.Select(d => d.Id), relationships,
            Comparer<ArchetypeId>.Create((a, b) => StringComparer.Ordinal.Compare(a.Value, b.Value)), "Subset relationships");
        var parents = relationships.GroupBy(r => r.Child).ToDictionary(g => g.Key, g => g.Select(r => r.Parent).ToArray());
        var ancestors = new Dictionary<ArchetypeId, HashSet<ArchetypeId>>();
        foreach (var id in archetypeOrder)
        {
            var inherited = new HashSet<ArchetypeId> { id };
            if (parents.TryGetValue(id, out var targets))
                foreach (var parent in targets) inherited.UnionWith(ancestors[parent]);
            ancestors.Add(id, inherited);
        }
        var declared = facts.ToDictionary(c => c.CardId, _ => new HashSet<ArchetypeId>());
        foreach (var membership in selected.SelectMany(p => p.Memberships.Memberships))
            declared[membership.CardId].UnionWith(membership.Archetypes);
        var expanded = declared.ToDictionary(p => p.Key, p => p.Value.SelectMany(id => ancestors[id]).ToHashSet());
        return new(profile, Fingerprint(profile, selected, facts), orderedPacks, Array.AsReadOnly(facts),
            Array.AsReadOnly(definitions), declared, expanded);
    }

    private static ArchetypePackDocument Freeze(ArchetypePackDocument pack)
    {
        if (pack.Manifest is null || pack.Definitions is null || pack.Memberships is null || pack.IdentityRules is null || pack.NativeMappings is null)
            throw new InvalidDataException("Every selected pack requires all authoring documents.");
        return new(pack.Manifest with
        {
            Profiles = Copy(Copy(pack.Manifest.Profiles).Order()),
            Dependencies = Copy(Copy(pack.Manifest.Dependencies).OrderBy(d => d.PackId.Value, StringComparer.Ordinal))
        }, pack.Definitions with
        {
            Definitions = Copy(Copy(pack.Definitions.Definitions).OrderBy(d => d.Id.Value, StringComparer.Ordinal).Select(d => d with
            {
                Aliases = Copy(Copy(d.Aliases).Order(StringComparer.Ordinal)),
                ExternalIds = Copy(Copy(d.ExternalIds).OrderBy(e => e.System, StringComparer.Ordinal).ThenBy(e => e.Value, StringComparer.Ordinal))
            })),
            Relationships = Copy(Copy(pack.Definitions.Relationships).OrderBy(r => r.Child.Value, StringComparer.Ordinal).ThenBy(r => r.Parent.Value, StringComparer.Ordinal))
        }, pack.Memberships with
        {
            Memberships = Copy(Copy(pack.Memberships.Memberships).OrderBy(m => m.CardId.Value, StringComparer.Ordinal)
                .Select(m => m with { Archetypes = Copy(Copy(m.Archetypes).OrderBy(id => id.Value, StringComparer.Ordinal)) }))
        }, pack.IdentityRules with { Rules = Copy(Copy(pack.IdentityRules.Rules).OrderBy(r => r.Id, StringComparer.Ordinal)) },
        pack.NativeMappings with { Mappings = Copy(Copy(pack.NativeMappings.Mappings).OrderBy(m => m.NativeGroupId)) });
    }

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T>? values)
    {
        if (values is null) throw new InvalidDataException("Selected content collections cannot be null.");
        var snapshot = values.ToArray();
        if (snapshot.Any(v => v is null)) throw new InvalidDataException("Selected content entries cannot be null.");
        return Array.AsReadOnly(snapshot);
    }

    private static IReadOnlyList<T> Topological<T>(IEnumerable<T> nodes, IEnumerable<(T Before, T After)> edges,
        IComparer<T> comparer, string subject) where T : notnull
    {
        var incoming = nodes.ToDictionary(id => id, _ => 0);
        var outgoing = incoming.Keys.ToDictionary(id => id, _ => new HashSet<T>());
        foreach (var (before, after) in edges)
        {
            if (!incoming.ContainsKey(before) || !incoming.ContainsKey(after)) throw new InvalidDataException(subject + " refer to unknown IDs.");
            if (outgoing[before].Add(after)) incoming[after]++;
        }
        var ready = new SortedSet<T>(incoming.Where(p => p.Value == 0).Select(p => p.Key), comparer);
        var ordered = new List<T>();
        while (ready.Count != 0)
        {
            T next = ready.Min!; ready.Remove(next); ordered.Add(next);
            foreach (var target in outgoing[next]) if (--incoming[target] == 0) ready.Add(target);
        }
        if (ordered.Count != incoming.Count) throw new InvalidDataException(subject + " contain a cycle.");
        return ordered.AsReadOnly();
    }

    private static string Fingerprint(ArchetypeContentProfile profile, ArchetypePackDocument[] packs, ArchetypeCardFacts[] cards)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length); hash.AppendData(bytes);
        }
        Add("archetype-snapshot-v1"); Add(profile.ToString());
        foreach (var pack in packs)
        {
            Add(ArchetypePackJson.WriteDocument(pack.Manifest)); Add(ArchetypePackJson.WriteDocument(pack.Definitions));
            Add(ArchetypePackJson.WriteDocument(pack.Memberships)); Add(ArchetypePackJson.WriteDocument(pack.IdentityRules));
            Add(ArchetypePackJson.WriteDocument(pack.NativeMappings));
        }
        Add(JsonSerializer.Serialize(cards));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
