namespace TagForceSpecial.Effects.Archetypes;

public sealed record ArchetypeCatalogCard(uint CatalogCardId, ArchetypeCardFacts Facts,
    ArchetypeReviewStatus Review, ArchetypeEvidence Evidence);
public sealed record ArchetypeCardCatalogDocument(int SchemaVersion, IReadOnlyList<ArchetypeCatalogCard> Cards);
public sealed record ArchetypeMappingSource(string System, string Version, ArchetypeReviewStatus Review, ArchetypeEvidence Evidence);
public sealed record ArchetypeMappingSourcesDocument(int SchemaVersion, IReadOnlyList<ArchetypeMappingSource> Sources);
public sealed record ArchetypeSourceCardMapping(SourceCardId ExternalId, SourceCardId CardId,
    ArchetypeReviewStatus Review, ArchetypeEvidence Evidence);
public sealed record ArchetypeCardMappingsDocument(int SchemaVersion, IReadOnlyList<ArchetypeSourceCardMapping> Mappings);

public sealed record ArchetypePackageDocument(ArchetypePackDocument Pack, ArchetypeCardCatalogDocument Catalog,
    ArchetypeCardMappingsDocument CardMappings, ArchetypeMappingSourcesDocument MappingSources,
    NativeArchetypeTableDocument? NativeTable = null);
public sealed record ArchetypePackageInventory(int SchemaVersion, ArchetypePackId PackId, string Version,
    IReadOnlyDictionary<string, string> DocumentHashes);
public sealed record ArchetypePackRequirement(ArchetypePackId PackId, string Version);
public sealed record ArchetypePackageReference(ArchetypePackId PackId, string Version, string Directory, string Sha256);
public sealed record ArchetypeStartupConfiguration(int SchemaVersion, ArchetypeContentProfile Profile,
    IReadOnlyList<ArchetypePackRequirement> Roots, IReadOnlyList<ArchetypePackageReference> Packages);
public sealed record ArchetypeLoadedPackage(ArchetypePackId PackId, string Version, string Sha256);
