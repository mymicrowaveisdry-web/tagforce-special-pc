using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TagForceSpecial.Assets;

public sealed record CardPropertyFields(int? Attack, int? Defense, uint AttributeCode, uint Level,
    uint TypeCode, uint RawKindCode, uint GetterKindCode, uint IconCode, uint Field23To26, uint Field27To30);
public sealed record CardDetail(uint CardId, string Name, CardPropertyFields Properties, string Description);

/// <summary>Bit ranges from main-module getter disassembly; numeric enums remain uninterpreted.</summary>
public static class CardPropertyDecoder
{
    public static CardPropertyFields Decode(uint id, uint word0, uint word1)
    {
        uint attack = (word0 >> 14) & 511, defense = word0 >> 23;
        uint kind = (word1 >> 1) & 63;
        return new(attack == 511 ? null : (int)attack * 10, defense == 511 ? null : (int)defense * 10,
            (word1 >> 7) & 15, (word1 >> 11) & 15, (word1 >> 18) & 31, kind,
            id is 4998 or 4999 or 5000 ? 11 : kind, (word1 >> 15) & 7,
            (word1 >> 23) & 15, (word1 >> 27) & 15);
    }
}

public static class CardHuffmanDecoder
{
    public static string Decode(ReadOnlySpan<byte> tree, ReadOnlySpan<byte> bytes, uint bitOffset, int maximumCharacters = 4096)
    {
        if (tree.Length < 6 || tree.Length % 2 != 0 || maximumCharacters is < 1 or > 16384)
            throw new InvalidDataException("Invalid Huffman tree/output limit.");
        long bit = bitOffset;
        var output = new List<byte>();
        long budget = (long)maximumCharacters * 1024;
        for (int characters = 0; characters < maximumCharacters; characters++)
        {
            int node = 0;
            while (true)
            {
                if (--budget < 0 || bit >= bytes.Length * 8L || node < 0 || node + 6 > tree.Length)
                    throw new InvalidDataException("Huffman input/tree traversal exceeds bounds.");
                int selected = (bytes[(int)(bit / 8)] >> (7 - (int)(bit % 8))) & 1;
                ushort edge = BinaryPrimitives.ReadUInt16LittleEndian(tree[(node + selected * 2)..]);
                if (edge == 0) break;
                if (edge % 2 != 0) throw new InvalidDataException("Unaligned Huffman branch.");
                node = edge; bit++;
            }
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(tree[(node + 4)..]);
            if (value == 0)
            {
                try { return new UnicodeEncoding(false, false, true).GetString(output.ToArray()); }
                catch (DecoderFallbackException e) { throw new InvalidDataException("Invalid decoded UTF-16.", e); }
            }
            output.Add((byte)value); output.Add((byte)(value >> 8));
        }
        throw new InvalidDataException("Huffman output exceeds limit.");
    }
}

public static class CardDescriptionFormatter
{
    public static IReadOnlyList<string> ReadDictionary(byte[] bytes)
    {
        if (bytes.Length < 12) throw new InvalidDataException("Truncated dictionary.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        uint table = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        uint data = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        if (count > 10000 || table < 12 || table + count * 4L > data || data > bytes.Length)
            throw new InvalidDataException("Invalid dictionary bounds.");
        var result = new List<string>();
        for (int i = 0; i < count; i++)
        {
            uint relative = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)table + i * 4));
            long offset = data + (long)relative;
            result.Add(ReadString(bytes, offset));
        }
        return result.AsReadOnly();
    }

    private static string ReadString(byte[] bytes, long offset)
    {
        if (offset < 0 || offset >= bytes.Length || offset % 2 != 0)
            throw new InvalidDataException("Invalid dictionary string offset.");
        int end = (int)offset, limit = (int)Math.Min(bytes.Length, offset + 8192);
        while (end + 1 < limit && (bytes[end] != 0 || bytes[end + 1] != 0)) end += 2;
        if (end + 1 >= limit) throw new InvalidDataException("Unterminated dictionary string.");
        try { return new UnicodeEncoding(false, false, true).GetString(bytes, (int)offset, end - (int)offset); }
        catch (DecoderFallbackException e) { throw new InvalidDataException("Invalid dictionary UTF-16.", e); }
    }

    public static string Expand(string text, IReadOnlyList<string> dictionary, IReadOnlyDictionary<uint, string> names)
    {
        // The game does dictionary substitutions first, then card-name substitutions.
        string expanded = Regex.Replace(text, @"\$d([0-9]{3})", match =>
        {
            int index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (index >= dictionary.Count) throw new InvalidDataException("Dictionary reference out of bounds.");
            return dictionary[index];
        });
        expanded = Regex.Replace(expanded, @"\$m([0-9]+)", match =>
        {
            if (!uint.TryParse(match.Groups[1].Value, out uint id) || !names.TryGetValue(id, out string? name))
                throw new InvalidDataException("Unsupported card-name reference.");
            return name;
        });
        if (expanded.Length > 16384) throw new InvalidDataException("Expanded description exceeds limit.");
        return expanded;
    }
}

public sealed class CardDetailReader
{
    public IReadOnlyList<CardDetail> Read(Stream source, uint minimum, uint maximum)
    {
        long original = source.Position;
        try
        {
            var rows = new CardInfoReader().Read(source, minimum, maximum).Rows;
            var entries = new EhpReader().ReadIndex(source).Entries;
            byte[] Load(string name)
            {
                var entry = entries.SingleOrDefault(e => e.Name == name) ?? throw new InvalidDataException($"Missing {name}");
                if (entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("Description table exceeds limit.");
                source.Position = entry.Offset; var bytes = new byte[(int)entry.Length]; source.ReadExactly(bytes); return bytes;
            }
            byte[] tree = Load("CARD_Huff_R.bin"), descriptions = Load("CARD_Desc_R.bin");
            var dictionary = CardDescriptionFormatter.ReadDictionary(Load("DICT_R.bin"));
            var names = rows.ToDictionary(r => r.CardId, r => r.Name);
            return rows.Select(row => new CardDetail(row.CardId, row.Name,
                CardPropertyDecoder.Decode(row.CardId, row.RawPropertyWord0, row.RawPropertyWord1),
                CardDescriptionFormatter.Expand(CardHuffmanDecoder.Decode(tree, descriptions, row.RawDescriptionIndex), dictionary, names))).ToArray();
        }
        finally { source.Position = original; }
    }
}
