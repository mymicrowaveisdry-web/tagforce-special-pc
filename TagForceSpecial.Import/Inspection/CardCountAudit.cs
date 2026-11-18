using TagForceSpecial.Assets;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Import.Inspection;

public sealed record CardCountReport(int RawPropertyRows, int SentinelRows, int LinkedCards, int NormalizedCards,
    uint MinimumId, uint MaximumId, string Source, string Explanation);

/// <summary>Reconciles raw property slots with mapped cards using the existing original-format readers.</summary>
public static class CardCountAudit
{
    public static CardCountReport Run(PrivateStorage storage, ImportManifest manifest)
    {
        using var files = GameFileSystem.Open(manifest.Source.Path);
        const string cardPath = "PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp";
        const string imagePath = "PSP_GAME/USRDIR/card/cardh_j.cip";
        var card = files.Find(cardPath) ?? throw new FileNotFoundException(cardPath);
        var image = files.Find(imagePath) ?? throw new FileNotFoundException(imagePath);
        var images = files.WithStream(image, s => new CardImageIndexReader().ReadIndex(s));
        var result = files.WithStream(card, stream =>
        {
            var index = new EhpReader().ReadIndex(stream);
            var property = index.Entries.Single(e => e.Name == "CARD_Prop.bin");
            stream.Position = property.Offset;
            Span<byte> sentinel = stackalloc byte[8]; stream.ReadExactly(sentinel);
            if (property.Length % 8 != 0 || !sentinel.SequenceEqual(new byte[8])) throw new InvalidDataException("Property rows or sentinel differ from the known encoding.");
            var cards = new CardInfoReader().Read(stream, images.MinimumCardId, images.MaximumCardId);
            return (Rows: checked((int)(property.Length / 8)), Cards: cards.Rows.Count);
        });
        int normalized = ImportInspection.Cards(storage, manifest).Count;
        if (result.Rows - 1 != result.Cards || result.Cards != normalized) throw new InvalidDataException("Raw, linked and normalized counts have an unexplained difference.");
        return new(result.Rows, 1, result.Cards, normalized, images.MinimumCardId, images.MaximumCardId, cardPath + "/CARD_Prop.bin",
            "Raw CARD_Prop.bin includes all-zero internal row 0, the missing-ID sentinel. CardInfoReader validates it and emits rows 1 onward; the normalized catalog preserves every linked card. A raw-slot count includes one non-card slot. This explains 7,255 versus 7,254 for this source, but does not establish how an external toolkit derives its count.");
    }
}
