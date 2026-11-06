namespace TagForceSpecial.Assets.Presentation;

public sealed record PresentationInventoryItem(uint CardId, long Offset, long StoredBytes,
    string ArchiveFormat, string DecodeStatus);

/// <summary>Raw catalog inventory independent of the gameplay supported-card set.</summary>
public static class PresentationInventory
{
    public static IReadOnlyList<PresentationInventoryItem> ReadCardArchive(Stream source)
    {
        var index = new CardImageIndexReader().ReadIndex(source);
        return Array.AsReadOnly(index.Entries.Select(slot => new PresentationInventoryItem(slot.CardId,
            slot.Offset, slot.StoredSlotBytes, index.Signature, "unresolved")).ToArray());
    }
}
