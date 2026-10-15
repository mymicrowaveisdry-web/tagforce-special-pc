using System.Buffers.Binary;
using System.Text;

namespace TagForceSpecial.Assets;

public sealed record CardInfoRow(int InternalIndex, uint CardId, string Name,
    uint RawPropertyWord0, uint RawPropertyWord1, uint RawDescriptionIndex);
public sealed record CardInfoIndex(uint MinimumCardId, uint MaximumCardId, int MissingIds,
    IReadOnlyList<CardInfoRow> Rows, IReadOnlyList<string> Limitations);

/// <summary>Observed TFSP table links and UTF-16 names; no stats or effect semantics inferred.</summary>
public sealed class CardInfoReader
{
    private static readonly UnicodeEncoding Utf16 = new(false, false, true);

    public CardInfoIndex Read(Stream source, uint minimumCardId, uint maximumCardId)
    {
        if (!source.CanRead || !source.CanSeek) throw new ArgumentException("Readable seekable stream required.");
        long original = source.Position;
        try
        {
            if (minimumCardId > maximumCardId || (long)maximumCardId - minimumCardId >= 100_000)
                throw new InvalidDataException("Unsupported mapping ID range.");
            var entries = new EhpReader().ReadIndex(source).Entries;
            byte[] Load(string name)
            {
                var entry = entries.SingleOrDefault(e => e.Name == name)
                    ?? throw new InvalidDataException($"Missing required table: {name}");
                if (entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("Table exceeds inspection limit.");
                var bytes = new byte[(int)entry.Length];
                source.Position = entry.Offset; source.ReadExactly(bytes); return bytes;
            }
            byte[] props = Load("CARD_Prop.bin"), index = Load("CARD_Indx_R.bin"),
                names = Load("CARD_Name_R.bin"), ids = Load("CARD_IntID.bin");
            if (props.Length < 16 || props.Length % 8 != 0 || names.Length % 2 != 0)
                throw new InvalidDataException("Invalid property/name table lengths.");
            int rows = props.Length / 8;
            if (rows > 100_000 || index.Length != (rows + 1L) * 8 ||
                ids.Length != ((long)maximumCardId - minimumCardId + 1) * 2)
                throw new InvalidDataException("Linked table lengths do not match the observed profile.");
            if (BinaryPrimitives.ReadUInt64LittleEndian(props) != 0)
                throw new InvalidDataException("Expected zero property sentinel row.");
            var linkedIds = new uint[rows];
            var seen = new bool[rows];
            int missing = 0;
            for (int i = 0; i < ids.Length / 2; i++)
            {
                ushort row = BinaryPrimitives.ReadUInt16LittleEndian(ids.AsSpan(i * 2));
                if (row == 0) { missing++; continue; }
                if (row >= rows || seen[row]) throw new InvalidDataException("Invalid/duplicate internal row mapping.");
                uint id = minimumCardId + (uint)i;
                uint word = BinaryPrimitives.ReadUInt32LittleEndian(props.AsSpan(row * 8));
                if ((word & 0x3fff) != id) throw new InvalidDataException("Property ID candidate disagrees with mapping.");
                seen[row] = true; linkedIds[row] = id;
            }
            var result = new List<CardInfoRow>(rows - 1);
            for (int i = 1; i < rows; i++)
            {
                if (!seen[i]) throw new InvalidDataException("Property row is not linked from the ID table.");
                uint nameOffset = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i * 8));
                uint description = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i * 8 + 4));
                if (nameOffset >= names.Length || nameOffset % 2 != 0)
                    throw new InvalidDataException("Invalid UTF-16 name offset.");
                int end = (int)nameOffset;
                int limit = (int)Math.Min(names.Length, nameOffset + 2048L);
                while (end + 1 < limit && (names[end] != 0 || names[end + 1] != 0)) end += 2;
                if (end + 1 >= limit) throw new InvalidDataException("Unterminated/oversized card name.");
                string name;
                try { name = Utf16.GetString(names, (int)nameOffset, end - (int)nameOffset); }
                catch (DecoderFallbackException e) { throw new InvalidDataException("Invalid UTF-16 card name.", e); }
                result.Add(new CardInfoRow(i, linkedIds[i], name,
                    BinaryPrimitives.ReadUInt32LittleEndian(props.AsSpan(i * 8)),
                    BinaryPrimitives.ReadUInt32LittleEndian(props.AsSpan(i * 8 + 4)), description));
            }
            return new CardInfoIndex(minimumCardId, maximumCardId, missing, result.AsReadOnly(),
                Array.AsReadOnly(new[] { "Property bits other than the cross-linked lower-14-bit ID are uninterpreted.",
                    "Description indexes are raw values; compressed descriptions are not decoded.",
                    "Name strings may retain game formatting; runtime display matching is not yet verified." }));
        }
        catch (EndOfStreamException e) { throw new InvalidDataException("Truncated card tables.", e); }
        finally { source.Position = original; }
    }
}
