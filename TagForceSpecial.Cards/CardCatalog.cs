using System.Collections.Frozen;

namespace TagForceSpecial.Cards;

/// <summary>Catalog identity, independent of a source file's packed ID or external database ID.</summary>
public sealed record CardDefinition(uint GameCardId, string Name, string Description,
    CardStatistics Statistics, CardNumericCodes? Codes = null, CardSourceRow? SourceRow = null,
    CardDataSource? Source = null);

/// <summary>Nonnegative printed stats; null represents an unresolved/variable value.</summary>
public sealed record CardStatistics(int? Attack, int? Defense, uint Level);

/// <summary>Optional imported numeric metadata. Gameplay classification uses explicit rule profiles.</summary>
public sealed record CardNumericCodes(uint Attribute, uint Type, uint RawKind, uint GetterKind,
    uint Icon, uint Field23To26, uint Field27To30);

/// <summary>Optional raw PSP provenance, retained without interpreting it as catalog identity.</summary>
public sealed record CardSourceRow(int InternalIndex, uint PropertyWord0, uint PropertyWord1,
    uint DescriptionBitOffset);

/// <summary>Origin profile and optional source-file hash. Authored cards need no invented container.</summary>
public sealed record CardDataSource(string? ContainerSha256, string DecoderProfile);

/// <summary>Immutable, format-independent catalog. Contains no bundled card data or effect behavior.</summary>
public sealed class CardCatalog
{
    private readonly FrozenDictionary<uint, CardDefinition> byId;
    public CardDataSource Source { get; }
    public IReadOnlyList<CardDefinition> Cards { get; }
    public int Count => Cards.Count;
    public CardDefinition this[uint gameCardId] => byId[gameCardId];

    public CardCatalog(CardDataSource source, IEnumerable<CardDefinition> cards)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(cards);
        Source = NormalizeSource(source);
        var snapshot = cards.ToArray();
        var ids = new HashSet<uint>();
        foreach (var card in snapshot)
        {
            if (card is null || card.GameCardId == 0 || !ids.Add(card.GameCardId))
                throw new ArgumentException("Cards require unique nonzero catalog IDs.", nameof(cards));
            if (string.IsNullOrWhiteSpace(card.Name) || card.Description is null ||
                card.Statistics is null)
                throw new ArgumentException("Card fields are required.", nameof(cards));
            var stats = card.Statistics;
            if (stats.Attack is < 0 || stats.Defense is < 0)
                throw new ArgumentException("Printed statistics must be nonnegative or variable.", nameof(cards));
        }
        var normalized = snapshot.Select(card => card with { Source = card.Source is { } origin ? NormalizeSource(origin) : Source })
            .OrderBy(card => card.GameCardId).ToArray();
        Cards = Array.AsReadOnly(normalized);
        byId = normalized.ToFrozenDictionary(c => c.GameCardId);
    }

    /// <summary>Combine immutable catalogs, preserving each card's origin and rejecting colliding IDs.</summary>
    public CardCatalog Merge(CardCatalog additions)
    {
        ArgumentNullException.ThrowIfNull(additions);
        return new(new(null, "merged-catalog-v1"), Cards.Concat(additions.Cards));
    }

    private static CardDataSource NormalizeSource(CardDataSource source)
    {
        if (string.IsNullOrWhiteSpace(source.DecoderProfile) ||
            source.ContainerSha256 is { } hash && (hash.Length != 64 || !hash.All(Uri.IsHexDigit)))
            throw new ArgumentException("An origin profile and, when supplied, a valid SHA-256 are required.", nameof(source));
        return source with { ContainerSha256 = source.ContainerSha256?.ToLowerInvariant() };
    }
}
