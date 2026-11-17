using System.Buffers.Binary;
using System.Text;

namespace TagForceSpecial.Import.Sources;

/// <summary>Bounded reader for a PSP <c>PARAM.SFO</c> (PSF) key/value table: UTF-8 strings and 32-bit integers only.</summary>
public static class ParamSfo
{
    public const int MaximumBytes = 64 * 1024;

    public static IReadOnlyDictionary<string, string> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length is < 20 or > MaximumBytes || !data[..4].SequenceEqual("\0PSF"u8)) throw new InvalidDataException("Not a PARAM.SFO table.");
        int keys = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[8..]), values = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
        if (count is < 0 or > 256 || 20 + count * 16 > data.Length || keys < 0 || keys > data.Length || values < 0 || values > data.Length)
            throw new InvalidDataException("PARAM.SFO table bounds are invalid.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            var row = data.Slice(20 + i * 16, 16);
            int keyOffset = keys + BinaryPrimitives.ReadUInt16LittleEndian(row);
            ushort format = BinaryPrimitives.ReadUInt16LittleEndian(row[2..]);
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[4..]), valueOffset = values + (int)BinaryPrimitives.ReadUInt32LittleEndian(row[12..]);
            if (keyOffset >= data.Length || valueOffset < 0 || length < 0 || valueOffset > data.Length - length)
                throw new InvalidDataException("PARAM.SFO entry exceeds the table.");
            int keyEnd = data[keyOffset..].IndexOf((byte)0);
            if (keyEnd <= 0) throw new InvalidDataException("PARAM.SFO key is unterminated.");
            string key = Encoding.ASCII.GetString(data.Slice(keyOffset, keyEnd));
            var value = data.Slice(valueOffset, length);
            result[key] = format switch
            {
                0x0204 or 0x0004 => Encoding.UTF8.GetString(value.IndexOf((byte)0) is var end and >= 0 ? value[..end] : value),
                0x0404 when length >= 4 => BinaryPrimitives.ReadUInt32LittleEndian(value).ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => Convert.ToHexStringLower(value)
            };
        }
        return result;
    }
}
