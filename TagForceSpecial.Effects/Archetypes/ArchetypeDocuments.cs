namespace TagForceSpecial.Effects.Archetypes;

public enum ArchetypeReviewStatus { Candidate, Reviewed }
public enum ArchetypeContentProfile { TfsClassic, Expanded }
public enum ArchetypePackKind { Native, Expansion, Synthetic }
public enum ArchetypeIdentityTreatment { PermanentName, PermanentArchetype }
public enum ArchetypeValidationMode { ReviewedContent, Diagnostic }

public sealed record ArchetypeEvidence(string Reference, string Sha256, string Reason);
public sealed record ArchetypePackDependency(ArchetypePackId PackId, string Version);
public sealed record ArchetypeExternalIdentifier(string System, string Value, ArchetypeEvidence Evidence);

public sealed record ArchetypePackManifest(int SchemaVersion, ArchetypePackId PackId, string Version,
    ArchetypePackKind Kind, IReadOnlyList<ArchetypeContentProfile> Profiles,
    IReadOnlyList<ArchetypePackDependency> Dependencies, ArchetypeEvidence Evidence);

public sealed record ArchetypeDefinition(ArchetypeId Id, string Name, IReadOnlyList<string> Aliases,
    IReadOnlyList<ArchetypeExternalIdentifier> ExternalIds, ArchetypeReviewStatus Review, ArchetypeEvidence Evidence);

/// <summary>Declares that every member of Child also belongs to Parent; it does not perform matching.</summary>
public sealed record ArchetypeSubsetRelationship(ArchetypeId Child, ArchetypeId Parent,
    ArchetypeReviewStatus Review, ArchetypeEvidence Evidence);

public sealed record ArchetypeDefinitionsDocument(int SchemaVersion, IReadOnlyList<ArchetypeDefinition> Definitions,
    IReadOnlyList<ArchetypeSubsetRelationship> Relationships);

public sealed record ArchetypeCardMembership(SourceCardId CardId, IReadOnlyList<ArchetypeId> Archetypes,
    ArchetypeReviewStatus Review, ArchetypeEvidence Evidence);
public sealed record ArchetypeMembershipsDocument(int SchemaVersion, IReadOnlyList<ArchetypeCardMembership> Memberships);

/// <summary>Authoring data for permanent treatment; duel-state resolution is a separate contract.</summary>
public sealed record ArchetypeIdentityRule(string Id, SourceCardId CardId, ArchetypeIdentityTreatment Treatment,
    ArchetypeReviewStatus Review, ArchetypeEvidence Evidence, string? Name = null, ArchetypeId? Archetype = null);
public sealed record ArchetypeIdentityRulesDocument(int SchemaVersion, IReadOnlyList<ArchetypeIdentityRule> Rules);

public sealed record NativeArchetypeMapping(int NativeGroupId, ArchetypeId? Archetype,
    ArchetypeReviewStatus Review, ArchetypeEvidence Evidence);
public sealed record NativeArchetypeMappingsDocument(int SchemaVersion, IReadOnlyList<NativeArchetypeMapping> Mappings);

/// <summary>Separate authoring documents, not a loaded runtime content snapshot.</summary>
public sealed record ArchetypePackDocument(ArchetypePackManifest Manifest, ArchetypeDefinitionsDocument Definitions,
    ArchetypeMembershipsDocument Memberships, ArchetypeIdentityRulesDocument IdentityRules,
    NativeArchetypeMappingsDocument NativeMappings);

/// <summary>Previously validated dependency exports supplied explicitly by the caller, without loading packs.</summary>
public sealed record ArchetypeDependencyExports(ArchetypePackId PackId, string Version, IReadOnlyList<ArchetypeId> Archetypes);
public sealed record ArchetypeValidationContext(IReadOnlyList<SourceCardId> Cards,
    IReadOnlyList<ArchetypeDependencyExports> Dependencies);

public sealed record ArchetypeValidationIssue(string Path, string Code, string Message);
