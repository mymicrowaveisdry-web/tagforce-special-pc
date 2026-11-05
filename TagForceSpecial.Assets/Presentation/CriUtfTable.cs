using System.Buffers.Binary;
using System.Text;

namespace TagForceSpecial.Assets.Presentation;

// Independently authored bounded @UTF reader. Offsets in Blob refer to the original ACB buffer.
internal sealed record CriUtfValue(int Type, ulong Number = 0, string? Text = null, int Offset = 0, int Length = 0);
internal sealed record CriUtfTable(string Name, IReadOnlyList<IReadOnlyDictionary<string, CriUtfValue>> Rows)
{
    internal static CriUtfTable Read(byte[] bytes, int at, int limit)
    {
        void Check(long start, long count, long end) => Adx2AudioReader.Require(start >= at && count >= 0 && start <= end && count <= end - start,
            "UTF range exceeds table/section bounds.");
        Check(at, 32, limit);
        Adx2AudioReader.Require(bytes.AsSpan(at, 4).SequenceEqual("@UTF"u8), "Missing UTF table signature.");
        int U16(int p) { Check(p, 2, limit); return BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(p)); }
        int U32(int p)
        {
            Check(p, 4, limit); uint n = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(p));
            Adx2AudioReader.Require(n <= int.MaxValue - 8, "UTF integer exceeds supported range."); return (int)n;
        }
        int size = U32(at + 4) + 8; Check(at, size, limit); int end = at + size;
        int rowsAt = checked(at + 8 + U32(at + 8)), stringsAt = checked(at + 8 + U32(at + 12)), dataAt = checked(at + 8 + U32(at + 16));
        int columns = U16(at + 24), width = U16(at + 26), count = U32(at + 28);
        Adx2AudioReader.Require(at + 32 <= rowsAt && rowsAt <= stringsAt && stringsAt <= dataAt && dataAt <= end &&
            columns <= 256 && count <= 100_000 && (long)columns * count <= 1_000_000, "Unsupported UTF layout/count.");
        Check(rowsAt, (long)width * count, stringsAt);
        string Text(int relative)
        {
            long p = (long)stringsAt + relative; Check(p, 1, dataAt);
            int stop = Array.IndexOf(bytes, (byte)0, (int)p, dataAt - (int)p);
            Adx2AudioReader.Require(stop >= p && stop - p <= 65536, "Unterminated or oversized UTF string.");
            try { return new UTF8Encoding(false, true).GetString(bytes, (int)p, stop - (int)p); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid UTF-8 string.", error); }
        }
        int ValueSize(int type) => type switch { 0 or 1 => 1, 2 or 3 => 2, 4 or 5 or 8 or 10 => 4, 6 or 7 or 9 or 11 => 8,
            _ => throw new InvalidDataException("Unsupported UTF field type.") };
        CriUtfValue Value(int type, int p, int boundary)
        {
            int length = ValueSize(type); Check(p, length, boundary);
            if (type == 10) return new(type, Text: Text(U32(p)));
            if (type == 11)
            {
                int offset = U32(p), lengthData = U32(p + 4);
                Check((long)dataAt + offset, lengthData, end);
                return new(type, Offset: dataAt + offset, Length: lengthData);
            }
            ulong number = 0; for (int i = 0; i < length; i++) number = (number << 8) | bytes[p + i];
            return new(type, number);
        }
        int cursor = at + 32, rowBytes = 0;
        var schema = new List<(string Name, int Type, int RowOffset, CriUtfValue? Constant)>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < columns; i++)
        {
            Check(cursor, 5, rowsAt);
            int flags = bytes[cursor], type = flags & 15, storage = flags & 240;
            string name = Text(U32(cursor + 1)); cursor += 5;
            Adx2AudioReader.Require(names.Add(name), "Duplicate UTF column name.");
            int length = ValueSize(type), rowOffset = rowBytes; CriUtfValue? constant;
            if (storage == 0x30) { constant = Value(type, cursor, rowsAt); cursor += length; }
            else if (storage == 0x50) { constant = null; rowBytes += length; }
            else if (storage == 0x10) constant = new(type, Text: type == 10 ? "" : null, Offset: type == 11 ? dataAt : 0);
            else throw new InvalidDataException("Unsupported UTF storage flags.");
            schema.Add((name, type, rowOffset, constant));
        }
        Adx2AudioReader.Require(rowBytes <= width, "UTF schema exceeds row width.");
        var rows = new List<IReadOnlyDictionary<string, CriUtfValue>>(count);
        for (int row = 0; row < count; row++)
        {
            int rowAt = rowsAt + row * width;
            var values = new Dictionary<string, CriUtfValue>(StringComparer.Ordinal);
            foreach (var field in schema)
                values.Add(field.Name, field.Constant ?? Value(field.Type, rowAt + field.RowOffset, rowAt + width));
            rows.Add(new System.Collections.ObjectModel.ReadOnlyDictionary<string, CriUtfValue>(values));
        }
        return new(Text(U32(at + 20)), rows.AsReadOnly());
    }
}
