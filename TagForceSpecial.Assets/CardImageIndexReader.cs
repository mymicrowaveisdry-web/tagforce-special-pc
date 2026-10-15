using System.Buffers.Binary;
using System.Text;

namespace TagForceSpecial.Assets;

public sealed record CardImageSlot(uint CardId, long Offset, long StoredSlotBytes);
public sealed record CardImageIndex(string Signature, uint MinimumCardId, uint MaximumCardId,
    long HeaderBytes, uint SlotSectors, IReadOnlyList<CardImageSlot> Entries, int MissingSlots);

/// <summary>Observed single-art CIP/CPJ/CPM directory. Slot length includes padding.</summary>
public sealed class CardImageIndexReader
{
    public CardImageIndex ReadIndex(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek) throw new ArgumentException("Readable seekable stream required.");
        long original = source.Position;
        try
        {
            Require(source.Length >= 48, "Truncated card image header.");
            source.Position = 0;
            Span<byte> h = stackalloc byte[48]; source.ReadExactly(h);
            string signature = Encoding.ASCII.GetString(h[..3]);
            Require(h[3] == 0x1a && signature is "CIP" or "CPJ" or "CPM", "Unsupported card image signature.");
            return ReadBody(source, signature, h);
        }
        catch (EndOfStreamException e) { throw new InvalidDataException("Truncated image index.", e); }
        finally { source.Position = original; }
    }

    private static CardImageIndex ReadBody(Stream source, string signature, ReadOnlySpan<byte> h)
    {
        uint total = BinaryPrimitives.ReadUInt32LittleEndian(h[4..]);
        uint header = BinaryPrimitives.ReadUInt32LittleEndian(h[16..]);
        uint minimum = BinaryPrimitives.ReadUInt32LittleEndian(h[20..]);
        uint maximum = BinaryPrimitives.ReadUInt32LittleEndian(h[24..]);
        uint table = BinaryPrimitives.ReadUInt32LittleEndian(h[28..]);
        uint sectors = BinaryPrimitives.ReadUInt32LittleEndian(h[32..]);
        Require(total == source.Length, "Declared image length differs from actual length.");
        Require(minimum <= maximum && (long)maximum - minimum < 100_000, "Invalid card ID range.");
        long count = (long)maximum - minimum + 1;
        Require(header >= 48 && header <= source.Length && table >= 48 && table <= header &&
            count * 8 + 8 <= header - (long)table, "Card offset table exceeds header bounds.");
        Require(sectors > 0 && sectors <= 32768, "Unsupported stored slot size.");
        long slotBytes = sectors * 2048L;
        long firstPayload = (header + 2047L) & ~2047L;
        Require(firstPayload <= source.Length, "Aligned payload start exceeds file.");
        source.Position = table;
        var result = new List<CardImageSlot>();
        int missing = 0;
        Span<byte> pair = stackalloc byte[8];
        var offsets = new HashSet<long>();
        for (long i = 0; i < count; i++)
        {
            source.ReadExactly(pair);
            uint sector = BinaryPrimitives.ReadUInt32LittleEndian(pair);
            uint second = BinaryPrimitives.ReadUInt32LittleEndian(pair[4..]);
            Require((sector & 0x80000000) == 0, "Alternate-art pointer tables are not yet supported.");
            Require(second == 0, "Nonzero second offset word is unsupported in this on-disc profile.");
            if (sector == 0) { missing++; continue; }
            long offset = sector * 2048L;
            Require(offset >= firstPayload && offset <= source.Length && slotBytes <= source.Length - offset,
                "Stored image slot exceeds payload bounds.");
            Require((offset - firstPayload) % slotBytes == 0, "Image slot does not match the observed fixed stride.");
            Require(offsets.Add(offset), "Aliased image slots are unsupported.");
            result.Add(new CardImageSlot(minimum + (uint)i, offset, slotBytes));
        }
        source.ReadExactly(pair);
        Require(BinaryPrimitives.ReadUInt64LittleEndian(pair) == ulong.MaxValue, "Missing image-table terminator.");
        return new CardImageIndex(signature, minimum, maximum, header, sectors, result.AsReadOnly(), missing);
    }

    private static void Require(bool value, string message)
    { if (!value) throw new InvalidDataException(message); }
}
