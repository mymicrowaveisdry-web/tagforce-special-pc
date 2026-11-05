using System.Collections.Frozen;

namespace TagForceSpecial.Core.Decks;

public enum DeckSection { Main, Extra, Side }
[Flags]
public enum DeckPlacements { None = 0, Main = 1, Extra = 2, Side = 4 }

/// <summary>Versioned card IDs and section order, independent of mutable duel instances.</summary>
public sealed class DeckDefinition
{
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; }
    public string DeckId { get; }
    public string Revision { get; }
    public string OwnerId { get; }
    public string CatalogId { get; }
    public string CatalogRevision { get; }
    public string SourceBuild { get; }
    public string PolicyId { get; }
    public string PolicyRevision { get; }
    public IReadOnlyList<uint> Main { get; }
    public IReadOnlyList<uint> Extra { get; }
    public IReadOnlyList<uint> Side { get; }

    public DeckDefinition(string deckId, string revision, string ownerId, string catalogId, string catalogRevision,
        string sourceBuild, string policyId, string policyRevision, IEnumerable<uint> main,
        IEnumerable<uint>? extra = null, IEnumerable<uint>? side = null, int schemaVersion = CurrentSchema)
    {
        if (new[] { deckId, revision, ownerId, catalogId, catalogRevision, sourceBuild, policyId, policyRevision }
            .Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 256)) throw new ArgumentException("Deck identity, owner, source and policy versions are required.");
        SchemaVersion = schemaVersion; DeckId = deckId; Revision = revision; OwnerId = ownerId; CatalogId = catalogId;
        CatalogRevision = catalogRevision; SourceBuild = sourceBuild; PolicyId = policyId; PolicyRevision = policyRevision;
        Main = Snapshot(main); Extra = Snapshot(extra ?? []); Side = Snapshot(side ?? []);
    }

    private static IReadOnlyList<uint> Snapshot(IEnumerable<uint> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var snapshot = ids.Take(1001).ToArray();
        if (snapshot.Length > 1000) throw new ArgumentException("Deck section exceeds the storage limit.");
        return Array.AsReadOnly(snapshot);
    }

    public IEnumerable<(DeckSection Section, int Index, uint CardId)> Entries() =>
        Main.Select((id, i) => (DeckSection.Main, i, id)).Concat(Extra.Select((id, i) => (DeckSection.Extra, i, id)))
            .Concat(Side.Select((id, i) => (DeckSection.Side, i, id)));
}

/// <summary>Local collection quantities; owner identity is a profile key, not account authentication.</summary>
public sealed class PlayerCollection
{
    public int SchemaVersion { get; }
    public string Revision { get; }
    public string OwnerId { get; }
    public string CatalogId { get; }
    public string CatalogRevision { get; }
    public string SourceBuild { get; }
    public IReadOnlyDictionary<uint, int> Counts { get; }
    public PlayerCollection(string ownerId, string catalogId, string catalogRevision, string sourceBuild, string revision,
        IReadOnlyDictionary<uint, int> counts, int schemaVersion = 1)
    {
        if (new[] { ownerId, catalogId, catalogRevision, sourceBuild, revision }.Any(string.IsNullOrWhiteSpace) || counts.Any(c => c.Key == 0 || c.Value < 0))
            throw new ArgumentException("Collection requires identity and nonnegative quantities of nonzero IDs.");
        SchemaVersion = schemaVersion; Revision = revision; OwnerId = ownerId; CatalogId = catalogId; CatalogRevision = catalogRevision; SourceBuild = sourceBuild;
        Counts = counts.ToFrozenDictionary();
    }
}
