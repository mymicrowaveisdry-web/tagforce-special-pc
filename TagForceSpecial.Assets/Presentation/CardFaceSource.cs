namespace TagForceSpecial.Assets.Presentation;

/// <summary>card/cardm_j.cip (CPM): one 2,048-byte slot per card holding a raw 64x64 PSP DXT1 card face, the image the Card Album draws in
/// its pockets (the album captures upload these slot bytes unchanged). The index is read once; faces decode on demand.</summary>
public sealed class CardFaceSource
{
    public const string SourcePath = "PSP_GAME/USRDIR/card/cardm_j.cip";
    public const int Width = 64, Height = 64, SlotBytes = Width * Height / 2;
    private readonly byte[] archive;
    private readonly Dictionary<uint, long> slots;

    public CardFaceSource(byte[] archive)
    {
        this.archive = archive;
        using var stream = new MemoryStream(archive, writable: false);
        var index = new CardImageIndexReader().ReadIndex(stream);
        if (index.Signature != "CPM" || index.Entries.Any(e => e.StoredSlotBytes < SlotBytes))
            throw new InvalidDataException("Card face archive differs from the observed CPM layout.");
        slots = index.Entries.ToDictionary(e => e.CardId, e => e.Offset);
    }

    public bool Contains(uint cardId) => slots.ContainsKey(cardId);

    /// <summary>RGBA pixels of the card's face, or null when the archive has no slot for it.</summary>
    public byte[]? Decode(uint cardId)
    {
        if (!slots.TryGetValue(cardId, out long offset)) return null;
        var rgba = new byte[Width * Height * 4];
        PspDxt1.Decode(archive.AsSpan(checked((int)offset), SlotBytes), Width, Height, Width / 4 * 8, rgba);
        return rgba;
    }
}
