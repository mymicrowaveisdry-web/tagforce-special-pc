using System.Security.Cryptography;
using TagForceSpecial.Assets;
using TagForceSpecial.Cards;

namespace TagForceSpecial.Tools;

/// <summary>Local PSP import adapter. Cards has no dependency on binary readers.</summary>
public static class CardCatalogImporter
{
    public static CardCatalog Read(Stream source, uint minimum, uint maximum)
    {
        if (!source.CanRead || !source.CanSeek) throw new ArgumentException("Readable seekable source required.");
        long original = source.Position;
        try
        {
            source.Position = 0;
            string before = Convert.ToHexStringLower(SHA256.HashData(source));
            source.Position = 0;
            var rows = new CardInfoReader().Read(source, minimum, maximum).Rows.ToDictionary(r => r.CardId);
            var details = new CardDetailReader().Read(source, minimum, maximum);
            var cards = details.Select(detail =>
            {
                var p = detail.Properties;
                var row = rows[detail.CardId];
                var card = new CardDefinition(detail.CardId, detail.Name, detail.Description,
                    new(p.Attack, p.Defense, p.Level),
                    new(p.AttributeCode, p.TypeCode, p.RawKindCode, p.GetterKindCode,
                        p.IconCode, p.Field23To26, p.Field27To30),
                    new(row.InternalIndex, row.RawPropertyWord0, row.RawPropertyWord1, row.RawDescriptionIndex));
                PspCardCatalogValidator.Validate(card);
                return card;
            }).ToArray();
            source.Position = 0;
            if (Convert.ToHexStringLower(SHA256.HashData(source)) != before)
                throw new InvalidDataException("Source changed during import.");
            return new(new(before, "tfsp-main-8087681f-stage4c-v1"), cards);
        }
        finally { source.Position = original; }
    }
}
