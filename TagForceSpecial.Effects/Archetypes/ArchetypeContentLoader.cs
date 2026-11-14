using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;

namespace TagForceSpecial.Effects.Archetypes;

/// <summary>A pinned startup result whose aliases, catalog IDs and external identifiers cannot change after loading.</summary>
public sealed class ArchetypeLoadedContent
{
    private readonly FrozenDictionary<SourceCardId, ArchetypeCatalogCard> cards;
    private readonly FrozenDictionary<uint, SourceCardId> catalogIds;
    private readonly FrozenDictionary<SourceCardId, SourceCardId> aliases;
    private readonly FrozenDictionary<(string System, string Value), ArchetypeId> external;
    public ArchetypeContentSnapshot Snapshot { get; }
    public string Fingerprint { get; }
    public IReadOnlyList<ArchetypeLoadedPackage> Packages { get; }
    public IReadOnlyList<ArchetypeMappingSource> MappingSources { get; }
    public IReadOnlyDictionary<ArchetypePackId, NativeArchetypeIndex> NativeIndexes { get; }

    internal ArchetypeLoadedContent(ArchetypeContentSnapshot snapshot, IReadOnlyList<ArchetypeLoadedPackage> packages,
        IReadOnlyList<ArchetypePackageDocument> documents)
    {
        Snapshot = snapshot; Packages = packages;
        var entries = documents.SelectMany(d => d.Catalog.Cards).ToArray();
        if (entries.Select(c => c.Facts.CardId).Distinct().Count() != entries.Length || entries.Select(c => c.CatalogCardId).Distinct().Count() != entries.Length)
            throw new InvalidDataException("Selected packages define colliding canonical or application catalog card IDs.");
        cards = entries.ToFrozenDictionary(c => c.Facts.CardId);
        catalogIds = entries.ToFrozenDictionary(c => c.CatalogCardId, c => c.Facts.CardId);
        var sources = new Dictionary<string, ArchetypeMappingSource>(StringComparer.Ordinal);
        foreach (var source in documents.SelectMany(d => d.MappingSources.Sources))
        {
            if (sources.TryGetValue(source.System, out var existing) &&
                (existing.Version != source.Version || !existing.Evidence.Sha256.Equals(source.Evidence.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Selected mappings require conflicting versions of an external dataset.");
            sources.TryAdd(source.System, source);
        }
        MappingSources = Array.AsReadOnly(sources.Values.OrderBy(s => s.System, StringComparer.Ordinal).ToArray());
        var cardAliases = new Dictionary<SourceCardId, SourceCardId>();
        foreach (var mapping in documents.SelectMany(d => d.CardMappings.Mappings))
            if (!cardAliases.TryAdd(mapping.ExternalId, mapping.CardId) ||
                cards.ContainsKey(mapping.ExternalId) && mapping.ExternalId != mapping.CardId)
                throw new InvalidDataException("External card aliases collide or shadow another canonical identity.");
        aliases = cardAliases.ToFrozenDictionary();
        external = snapshot.GetAll().SelectMany(d => d.ExternalIds.Select(e => (e.System, e.Value, d.Id)))
            .ToFrozenDictionary(e => (e.System, e.Value), e => e.Id);
        NativeIndexes = documents.Where(d => d.NativeTable is not null)
            .ToFrozenDictionary(d => d.Pack.Manifest.PackId, d => new NativeArchetypeIndex(d.NativeTable!));
        string identity = "archetype-loaded-content-v1\n" + snapshot.Fingerprint + "\n" +
            string.Join("\n", packages.OrderBy(p => p.PackId.Value, StringComparer.Ordinal)
                .Select(p => p.Sha256.ToLowerInvariant()));
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public SourceCardId? ResolveCard(SourceCardId id) => cards.ContainsKey(id) ? id : aliases.TryGetValue(id, out var canonical) ? canonical : null;
    public uint? GetCatalogCardId(SourceCardId id) => ResolveCard(id) is { } canonical ? cards[canonical].CatalogCardId : null;
    public SourceCardId? GetSourceCard(uint catalogId) => catalogIds.TryGetValue(catalogId, out var id) ? id : null;
    public ArchetypeId? ResolveExternalArchetype(string system, string value) => external.TryGetValue((system, value), out var id) ? id : null;
    public bool IsMember(SourceCardId card, ArchetypeId archetype) => ResolveCard(card) is { } canonical && Snapshot.IsMember(canonical, archetype);
}

/// <summary>Loads only the pinned dependency closure of explicit startup roots; unselected packages are never opened.</summary>
public static class ArchetypeContentLoader
{
    public const long MaximumContentBytes = 256L * 1024 * 1024;

    public static ArchetypeLoadedContent LoadFile(string configurationPath)
    {
        string path = Path.GetFullPath(configurationPath);
        var configuration = ArchetypePackJson.ReadDocument<ArchetypeStartupConfiguration>(ArchetypePackageStore.Text(
            ArchetypePackageStore.ReadBytes(path, ArchetypePackJson.MaximumDocumentBytes)));
        if (configuration.Packages is null || configuration.Packages.Any(p => p is null || string.IsNullOrWhiteSpace(p.Directory)))
            throw new InvalidDataException("Startup package references cannot be null.");
        string parent = Path.GetDirectoryName(path)!;
        return Load(configuration with { Packages = configuration.Packages.Select(p => p with
            { Directory = Path.GetFullPath(p.Directory, parent) }).ToArray() });
    }

    public static ArchetypeLoadedContent Load(ArchetypeStartupConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.SchemaVersion != 1 || !Enum.IsDefined(configuration.Profile) || configuration.Roots is null ||
            configuration.Roots.Count == 0 || configuration.Roots.Any(r => r is null) || configuration.Packages is null)
            throw new InvalidDataException("Startup configuration needs a known schema/profile and explicit roots/packages.");
        var available = new Dictionary<(ArchetypePackId Id, string Version), ArchetypePackageReference>();
        foreach (var reference in configuration.Packages)
            if (reference is null || !ContentIdentifier.IsValid(reference.PackId.Value) || !ArchetypePackageValidation.IsVersion(reference.Version) ||
                !ArchetypePackageValidation.IsHash(reference.Sha256) || string.IsNullOrWhiteSpace(reference.Directory) ||
                !available.TryAdd((reference.PackId, reference.Version), reference))
                throw new InvalidDataException("Available package revisions require unique IDs/versions and explicit directories/hashes.");
        if (configuration.Roots.Select(r => r.PackId).Distinct().Count() != configuration.Roots.Count)
            throw new InvalidDataException("Startup roots must have distinct pack IDs.");
        var pending = new Queue<ArchetypePackRequirement>(configuration.Roots);
        var selected = new Dictionary<ArchetypePackId, (ArchetypePackageDocument Data, ArchetypeLoadedPackage Identity)>();
        long total = 0;
        while (pending.TryDequeue(out var requirement))
        {
            if (requirement is null || !ContentIdentifier.IsValid(requirement.PackId.Value) || !ArchetypePackageValidation.IsVersion(requirement.Version))
                throw new InvalidDataException("Dependencies require valid pack IDs and exact revisions.");
            if (selected.TryGetValue(requirement.PackId, out var previous))
            {
                if (previous.Identity.Version != requirement.Version) throw new InvalidDataException("Selection requires multiple versions of one pack.");
                continue;
            }
            if (!available.TryGetValue((requirement.PackId, requirement.Version), out var reference))
                throw new InvalidDataException("A pinned package for an exact required revision is missing.");
            var read = ArchetypePackageStore.Read(reference);
            total += read.Bytes;
            if (total > MaximumContentBytes) throw new InvalidDataException("Selected content exceeds its byte budget.");
            selected.Add(requirement.PackId, (read.Package, new(requirement.PackId, requirement.Version, reference.Sha256.ToLowerInvariant())));
            if (read.Package.Pack.Manifest.Dependencies is null) throw new InvalidDataException("Dependencies cannot be null.");
            foreach (var dependency in read.Package.Pack.Manifest.Dependencies)
            {
                if (dependency is null) throw new InvalidDataException("Dependency entries cannot be null.");
                pending.Enqueue(new(dependency.PackId, dependency.Version));
            }
        }
        var documents = selected.OrderBy(p => p.Key.Value, StringComparer.Ordinal).Select(p => p.Value.Data).ToArray();
        if (documents.Any(d => d.Catalog.Cards is null || d.Catalog.Cards.Any(c => c is null || c.Facts is null) || d.Pack.Definitions.Definitions is null))
            throw new InvalidDataException("Required card and definition collections cannot be null.");
        var context = new ArchetypeValidationContext(documents.SelectMany(d => d.Catalog.Cards).Select(c => c.Facts.CardId).ToArray(),
            documents.Select(d => new ArchetypeDependencyExports(d.Pack.Manifest.PackId, d.Pack.Manifest.Version,
                d.Pack.Definitions.Definitions.Where(a => a is not null).Select(a => a.Id).ToArray())).ToArray());
        foreach (var document in documents) ArchetypePackageValidation.Validate(document, context);
        var snapshot = ArchetypeContentSnapshot.Compile(configuration.Profile, documents.Select(d => d.Pack), documents.SelectMany(d => d.Catalog.Cards).Select(c => c.Facts));
        return new(snapshot, Array.AsReadOnly(selected.OrderBy(p => p.Key.Value, StringComparer.Ordinal).Select(p => p.Value.Identity).ToArray()), Array.AsReadOnly(documents));
    }
}
