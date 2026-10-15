using System.Collections.Frozen;

namespace TagForceSpecial.Cards;

/// <summary>Identity is local to this game's data, never an external card database ID.</summary>
public sealed record CardDefinition(uint GameCardId, string Name, string Description,
    CardStatistics Statistics, CardNumericCodes Codes, CardSourceRow SourceRow);

/// <summary>Null stats preserve the game's variable-stat sentinel. Spell/trap zeros remain raw data.</summary>
public sealed record CardStatistics(int? Attack, int? Defense, uint Level);

/// <summary>Observed getter codes. Display enums and the two high fields remain uninterpreted.</summary>
public sealed record CardNumericCodes(uint Attribute, uint Type, uint RawKind, uint GetterKind,
    uint Icon, uint Field23To26, uint Field27To30);

public sealed record CardSourceRow(int InternalIndex, uint PropertyWord0, uint PropertyWord1,
    uint DescriptionBitOffset);

/// <summary>Provenance of locally imported data; does not attest ownership or visual verification.</summary>
public sealed record CardDataSource(string ContainerSha256, string DecoderProfile);

/// <summary>Immutable catalog for native consumers. Contains no bundled card data or effect behavior.</summary>
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
        if (source.ContainerSha256 is not { Length: 64 } || !source.ContainerSha256.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(source.DecoderProfile))
            throw new ArgumentException("Source hash and decoder profile are required.", nameof(source));
        Source = source with { ContainerSha256 = source.ContainerSha256.ToLowerInvariant() };
        var snapshot = cards.ToArray();
        var ids = new HashSet<uint>();
        foreach (var card in snapshot)
        {
            if (card is null || card.GameCardId is 0 or > 0x3fff || !ids.Add(card.GameCardId))
                throw new ArgumentException("Cards require unique nonzero game-local IDs.", nameof(cards));
            if (string.IsNullOrWhiteSpace(card.Name) || card.Description is null ||
                card.Statistics is null || card.Codes is null || card.SourceRow is null)
                throw new ArgumentException("Card fields are required.", nameof(cards));
            if (card.SourceRow.InternalIndex < 1 || (card.SourceRow.PropertyWord0 & 0x3fff) != card.GameCardId)
                throw new ArgumentException("Source row does not match the card identity.", nameof(cards));
            var stats = card.Statistics;
            if (!ValidStat(stats.Attack) || !ValidStat(stats.Defense) || stats.Level > 15)
                throw new ArgumentException("Statistics exceed the observed profile.", nameof(cards));
            var codes = card.Codes;
            if (codes.Attribute > 15 || codes.Type > 31 || codes.RawKind > 63 || codes.GetterKind > 63 ||
                codes.Icon > 7 || codes.Field23To26 > 15 || codes.Field27To30 > 15)
                throw new ArgumentException("Numeric codes exceed the observed profile.", nameof(cards));
        }
        Cards = Array.AsReadOnly(snapshot.OrderBy(c => c.GameCardId).ToArray());
        byId = snapshot.ToFrozenDictionary(c => c.GameCardId);
    }

    private static bool ValidStat(int? value) => value is null || value is >= 0 and <= 5100 && value % 10 == 0;
}
