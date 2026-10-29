using TagForceSpecial.Cards;

namespace TagForceSpecial.Tools;

/// <summary>PSP property-format checks at the import boundary, independent of catalog/gameplay rules.</summary>
public static class PspCardCatalogValidator
{
    public static void Validate(CardDefinition card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.GameCardId is 0 or > 0x3fff || card.SourceRow is not { InternalIndex: > 0 } row ||
            (row.PropertyWord0 & 0x3fff) != card.GameCardId)
            throw new InvalidDataException("PSP source row does not match the decoded card identity.");
        if (card.Statistics is not { } stats || !ValidStat(stats.Attack) || !ValidStat(stats.Defense) || stats.Level > 15)
            throw new InvalidDataException("Decoded statistics exceed the PSP property format.");
        if (card.Codes is not { } codes || codes.Attribute > 15 || codes.Type > 31 ||
            codes.RawKind > 63 || codes.GetterKind > 63 || codes.Icon > 7 ||
            codes.Field23To26 > 15 || codes.Field27To30 > 15)
            throw new InvalidDataException("Decoded numeric codes exceed the PSP property format.");
    }

    private static bool ValidStat(int? value) => value is null || value is >= 0 and <= 5100 && value % 10 == 0;
}
