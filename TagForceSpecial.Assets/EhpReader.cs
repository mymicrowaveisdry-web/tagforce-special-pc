using System.Buffers.Binary;
using System.Text;

namespace TagForceSpecial.Assets;

public sealed record EhpEntry(int Index, string Name, long InfoOffset, long Offset, long Length);
public sealed record EhpIndex(uint Version, long DeclaredLength, uint Marker,
    IReadOnlyList<EhpEntry> Entries, IReadOnlyList<string> Diagnostics);

/// <summary>Read-only EHP v3 index reader for ASCII names. Ranges describe stored bytes.</summary>
public sealed class EhpReader
{
    public const int MaximumEntries = 100_000;
    public const int MaximumNameBytes = 1024;

    public EhpIndex ReadIndex(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("A readable, seekable stream is required.", nameof(source));
        long originalPosition = source.Position;
        try
        {
            long length = source.Length;
            Require(length >= 16, "Truncated EHP header.");
            Require(length <= int.MaxValue, "Archives above the supported signed 32-bit range are not supported.");
            source.Position = 0;
            Span<byte> header = stackalloc byte[16];
            source.ReadExactly(header);
            Require(header[..4].SequenceEqual("EHP\x03"u8), "Unsupported EHP magic/version.");
            long declared = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            uint marker = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
            Require(declared == length, $"Declared length {declared} differs from actual length {length}.");
            Require(count <= MaximumEntries, "Entry count exceeds the inspection limit.");
            long tableEnd = 16L + count * 8L;
            Range(16, count * 8L, length, "Entry table");
            var pointers = new (long Info, long Data)[(int)count];
            Span<byte> pair = stackalloc byte[8];
            long firstData = length;
            for (int i = 0; i < pointers.Length; i++)
            {
                source.ReadExactly(pair);
                long info = BinaryPrimitives.ReadUInt32LittleEndian(pair);
                long data = BinaryPrimitives.ReadUInt32LittleEndian(pair[4..]);
                Require(info >= tableEnd && info < length, $"Entry {i}: invalid info pointer.");
                Require(data >= tableEnd && data <= length, $"Entry {i}: invalid payload pointer.");
                pointers[i] = (info, data);
                firstData = Math.Min(firstData, data);
            }
            var entries = new List<EhpEntry>(pointers.Length);
            var metadataRanges = new List<(long Start, long End)>(pointers.Length);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var diagnostics = new List<string>();
            if (marker != 0x20544f4e)
                diagnostics.Add($"Secondary marker 0x{marker:x8} differs from NOT; preserved as uninterpreted metadata.");
            Span<byte> sizeBuffer = stackalloc byte[4];
            for (int i = 0; i < pointers.Length; i++)
            {
                var (info, data) = pointers[i];
                Require(info < firstData, $"Entry {i}: metadata intersects payload area.");
                source.Position = info;
                var nameBytes = new List<byte>();
                bool terminated = false;
                for (int j = 0; j <= MaximumNameBytes && source.Position < firstData; j++)
                {
                    int value = source.ReadByte();
                    if (value < 0) throw new InvalidDataException("Truncated name.");
                    if (value == 0) { terminated = true; break; }
                    Require(j < MaximumNameBytes, $"Entry {i}: name exceeds inspection limit.");
                    Require(value >= 32 && value <= 126, $"Entry {i}: unsupported non-ASCII/control byte in name.");
                    nameBytes.Add((byte)value);
                }
                Require(terminated, $"Entry {i}: unterminated name.");
                string name = Encoding.ASCII.GetString(nameBytes.ToArray());
                Require(name.Length > 0 && name is not "." and not ".." &&
                    name.IndexOfAny(['/', '\\', ':']) < 0, $"Entry {i}: invalid or path-bearing name.");
                Require(names.Add(name), $"Entry {i}: duplicate name.");
                Range(source.Position, 4, firstData, $"Entry {i} size metadata");
                source.ReadExactly(sizeBuffer);
                long storedLength = BinaryPrimitives.ReadUInt32LittleEndian(sizeBuffer);
                Range(data, storedLength, length, $"Entry {i} payload");
                metadataRanges.Add((info, source.Position));
                entries.Add(new EhpEntry(i, name, info, data, storedLength));
                if (data % 16 != 0)
                    diagnostics.Add($"Entry {i} payload offset is not 16-byte aligned; no alignment assumption applied.");
            }
            RejectOverlaps(metadataRanges, "Metadata records overlap.");
            RejectOverlaps(entries.Where(e => e.Length > 0).Select(e => (e.Offset, e.Offset + e.Length)),
                "Payload ranges overlap; shared/aliased entries are unsupported.");
            return new EhpIndex(3, declared, marker, entries.AsReadOnly(), diagnostics.AsReadOnly());
        }
        catch (EndOfStreamException error)
        {
            throw new InvalidDataException("Archive was truncated while reading.", error);
        }
        finally { source.Position = originalPosition; }
    }

    private static void RejectOverlaps(IEnumerable<(long Start, long End)> ranges, string message)
    {
        long end = -1;
        foreach (var range in ranges.OrderBy(r => r.Start))
        {
            Require(range.Start >= end, message);
            end = range.End;
        }
    }

    private static void Range(long offset, long size, long boundary, string label) =>
        Require(offset >= 0 && size >= 0 && offset <= boundary && size <= boundary - offset,
            $"{label} exceeds file/section bounds.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
