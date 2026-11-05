namespace TagForceSpecial.Cards.CatalogStorage;

/// <summary>Package identity is independent of card IDs and effect implementation versions.</summary>
public sealed record CardCatalogPackageInfo(string CatalogId, string Revision, string Locale, string SourceBuild);

public sealed record CardCatalogPackageManifest(int SchemaVersion, CardCatalogPackageInfo Identity,
    CardDataSource Source, int CardCount, string CardsFile, string CardsSha256);

// Mutable arrays exist only at the serialization boundary. They are never published as catalog state.
internal sealed record CardCatalogDocument
{
    public required int SchemaVersion { get; init; }
    public required CardDataSource Source { get; init; }
    public required CardDefinitionDocument[] Cards { get; init; }
}

internal sealed record CardDefinitionDocument
{
    public required uint GameCardId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required CardStatisticsDocument Statistics { get; init; }
    public CardNumericCodesDocument? Codes { get; init; }
    public CardSourceRowDocument? SourceRow { get; init; }
    public required CardDataSource Source { get; init; }
    public required CardKind Kind { get; init; }
    public required CardEffectState EffectState { get; init; }
    public required string[] EffectReferences { get; init; }
}

internal sealed record CardStatisticsDocument
{
    public required int? Attack { get; init; }
    public required int? Defense { get; init; }
    public required uint Level { get; init; }
}

internal sealed record CardNumericCodesDocument
{
    public required uint Attribute { get; init; }
    public required uint Type { get; init; }
    public required uint RawKind { get; init; }
    public required uint GetterKind { get; init; }
    public required uint Icon { get; init; }
    public required uint Field23To26 { get; init; }
    public required uint Field27To30 { get; init; }
    public CardNumericCodes ToModel() => new(Attribute, Type, RawKind, GetterKind, Icon, Field23To26, Field27To30);
}

internal sealed record CardSourceRowDocument
{
    public required int InternalIndex { get; init; }
    public required uint PropertyWord0 { get; init; }
    public required uint PropertyWord1 { get; init; }
    public required uint DescriptionBitOffset { get; init; }
    public CardSourceRow ToModel() => new(InternalIndex, PropertyWord0, PropertyWord1, DescriptionBitOffset);
}
