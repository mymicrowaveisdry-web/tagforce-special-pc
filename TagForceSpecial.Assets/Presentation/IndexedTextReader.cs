using System.Buffers.Binary;
using System.Text;

namespace TagForceSpecial.Assets.Presentation;

public sealed record IndexedTextRecord(int Index, uint SourceOffset, string? Text, bool IsEndMarker, string? Diagnostic);
public sealed record IndexedTextTable(IReadOnlyList<IndexedTextRecord> Records, string OffsetUnit, int TextBytes);

/// <summary>Read-only source string pointers; preserves duplicates, backward references, controls and row IDs.
/// Source suffix language labels are not translation verification. No script execution or localization.</summary>
public static class IndexedTextReader
{
    private const int MaximumBytes = 64 * 1024 * 1024;
    private const int MaximumRecords = 100_000;
    private const int MaximumStringUnits = 65536;

    public static IndexedTextTable ReadPair(ReadOnlySpan<byte> index, ReadOnlySpan<byte> text)
    {
        Require(index.Length > 0 && index.Length % 4 == 0 && index.Length / 4 <= MaximumRecords, "Invalid paired string index.");
        return Read(index, text, codeUnits: true);
    }

    public static IndexedTextTable ReadStringTable(ReadOnlySpan<byte> file)
    {
        Require(file.Length >= 12 && file.Length <= MaximumBytes, "Invalid string-table length.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(file), table = BinaryPrimitives.ReadUInt32LittleEndian(file[4..]),
            data = BinaryPrimitives.ReadUInt32LittleEndian(file[8..]);
        Require(count > 0 && count <= MaximumRecords && table >= 12 && table <= data &&
            data <= file.Length && count * 4L <= data - (long)table, "String-table directory exceeds declared member bounds.");
        return Read(file.Slice((int)table, (int)count * 4), file[(int)data..], codeUnits: false);
    }

    private static IndexedTextTable Read(ReadOnlySpan<byte> index, ReadOnlySpan<byte> text, bool codeUnits)
    {
        Require(text.Length <= MaximumBytes && text.Length % 2 == 0, "Text is not bounded UTF-16LE code units.");
        byte[] data = text.ToArray();
        var records = new List<IndexedTextRecord>();
        var cache = new Dictionary<uint, (string? Text, string? Diagnostic)>();
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
        for (int row = 0; row < index.Length / 4; row++)
        {
            uint sourceOffset = BinaryPrimitives.ReadUInt32LittleEndian(index[(row * 4)..]);
            long at = (long)sourceOffset * (codeUnits ? 2 : 1);
            if (at == data.Length) { records.Add(new(row, sourceOffset, null, true, null)); continue; }
            if (!cache.TryGetValue(sourceOffset, out var decoded))
            {
                decoded = (null, null);
                if (at < 0 || at > data.Length || at % 2 != 0) decoded = (null, "String pointer is outside/aligned incorrectly within declared text bounds.");
                else if (at > 0 && (data[(int)at - 2] != 0 || data[(int)at - 1] != 0))
                    decoded = (null, "Interior/suffix string pointer is unsupported; no boundary inferred.");
                else
                {
                    int stop = (int)at, units = 0;
                    while (stop < data.Length && units < MaximumStringUnits && (data[stop] != 0 || data[stop + 1] != 0))
                    { stop += 2; units++; }
                    if (stop == data.Length || units == MaximumStringUnits)
                        decoded = (null, "Unterminated/oversized string within declared text bounds.");
                    else
                    {
                        try { decoded = (encoding.GetString(data, (int)at, stop - (int)at), null); }
                        catch (DecoderFallbackException) { decoded = (null, "Invalid UTF-16LE string; replacement characters were not substituted."); }
                    }
                }
                cache.Add(sourceOffset, decoded);
            }
            records.Add(new(row, sourceOffset, decoded.Text, false, decoded.Diagnostic));
        }
        return new(records.AsReadOnly(), codeUnits ? "utf16-code-units" : "bytes", data.Length);
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
