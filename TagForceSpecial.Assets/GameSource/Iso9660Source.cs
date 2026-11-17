using System.Buffers.Binary;
using System.Text;

namespace TagForceSpecial.Assets.GameSource;

/// <summary>Read-only ISO9660 lookup without Joliet, CSO or multi-extent support.</summary>
internal sealed class Iso9660Source : IGameFiles
{
    private const int Block = 2048;
    private readonly Stream stream;
    private readonly GameFileEntry root;

    internal Iso9660Source(Stream stream)
    {
        this.stream = stream;
        for (int sector = 16; sector < 48; sector++)
        {
            var descriptor = Read(sector * (long)Block, Block);
            if (!descriptor.AsSpan(1, 5).SequenceEqual("CD001"u8) || descriptor[6] != 1)
                throw new InvalidDataException("Invalid ISO9660 volume descriptor.");
            if (descriptor[0] == 255) break;
            if (descriptor[0] != 1) continue;
            if (BinaryPrimitives.ReadUInt16LittleEndian(descriptor.AsSpan(128)) != Block ||
                BinaryPrimitives.ReadUInt16BigEndian(descriptor.AsSpan(130)) != Block)
                throw new InvalidDataException("Only 2048-byte ISO sectors are supported.");
            root = Parse(descriptor.AsSpan(156), "");
            if (!root.Directory) throw new InvalidDataException("ISO root is not a directory.");
            return;
        }
        throw new InvalidDataException("Missing ISO9660 primary volume descriptor.");
    }

    public GameFileEntry? Find(string path)
    {
        var current = root;
        foreach (string part in path.Split('/'))
        {
            if (!current.Directory) return null;
            if (!Children(current).TryGetValue(part, out var next)) return null;
            current = next;
        }
        return current;
    }

    public Stream OpenRead(GameFileEntry entry)
    {
        if (entry.Directory || entry.Offset < 0 || entry.Offset > stream.Length || entry.Length > stream.Length - entry.Offset)
            throw new InvalidDataException("ISO file extent exceeds bounds.");
        return new StreamSlice(stream, entry.Offset, entry.Length);
    }

    public IEnumerable<GameFileEntry> Enumerate(int maximumEntries)
    {
        int count = 0;
        var pending = new Stack<GameFileEntry>(); pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (var child in Children(pending.Pop()).Values.OrderBy(e => e.Path, StringComparer.Ordinal))
            {
                if (++count > maximumEntries) throw new InvalidDataException("ISO has too many entries.");
                if (child.Directory) pending.Push(child);
                yield return child;
            }
        }
    }

    private Dictionary<string, GameFileEntry> Children(GameFileEntry directory)
    {
        var data = Read(directory.Offset, checked((int)directory.Length));
        var entries = new Dictionary<string, GameFileEntry>(StringComparer.OrdinalIgnoreCase);
        for (int offset = 0; offset < data.Length;)
        {
            int length = data[offset];
            if (length == 0) { offset = (offset / Block + 1) * Block; continue; }
            if (length < 34 || offset + length > data.Length || offset % Block + length > Block)
                throw new InvalidDataException("Malformed ISO directory record.");
            var row = data.AsSpan(offset, length);
            int nameLength = row[32];
            if (33 + nameLength > row.Length || nameLength == 0) throw new InvalidDataException("Malformed ISO filename.");
            if (!(nameLength == 1 && row[33] is 0 or 1))
            {
                string name = Encoding.ASCII.GetString(row.Slice(33, nameLength));
                if (name.EndsWith(";1", StringComparison.Ordinal)) name = name[..^2];
                if (name.Contains('/') || name is "." or ".." ||
                    !entries.TryAdd(name, Parse(row, directory.Path.Length == 0 ? name : directory.Path + "/" + name)))
                    throw new InvalidDataException("Duplicate or invalid ISO directory name.");
            }
            offset += length;
        }
        return entries;
    }

    private GameFileEntry Parse(ReadOnlySpan<byte> row, string path)
    {
        if (row.Length < 34 || row[0] < 34 || (row[25] & 0x80) != 0)
            throw new InvalidDataException("Unsupported ISO directory record.");
        uint sector = BinaryPrimitives.ReadUInt32LittleEndian(row[2..]), length = BinaryPrimitives.ReadUInt32LittleEndian(row[10..]);
        if (sector != BinaryPrimitives.ReadUInt32BigEndian(row[6..]) || length != BinaryPrimitives.ReadUInt32BigEndian(row[14..]))
            throw new InvalidDataException("ISO endian fields disagree.");
        long start = sector * (long)Block;
        if (start > stream.Length || length > stream.Length - start || (row[25] & 2) != 0 && length > 16 * 1024 * 1024)
            throw new InvalidDataException("ISO file extent exceeds bounds.");
        return new(path, start, length, (row[25] & 2) != 0);
    }

    private byte[] Read(long offset, int length)
    {
        if (length < 0 || offset < 0 || offset > stream.Length || length > stream.Length - offset)
            throw new InvalidDataException("Truncated ISO extent.");
        stream.Position = offset;
        var data = new byte[length]; stream.ReadExactly(data); return data;
    }
}
