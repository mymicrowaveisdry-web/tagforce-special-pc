namespace TagForceSpecial.Assets.GameSource;

/// <param name="Path">'/'-separated path from the source root, for example <c>PSP_GAME/SYSDIR/EBOOT.BIN</c>.</param>
/// <param name="Offset">Byte offset inside the ISO image; 0 for files of an extracted directory.</param>
public sealed record GameFileEntry(string Path, long Offset, long Length, bool Directory);

/// <summary>Read-only file lookup over an original source: an ISO 9660 image or an extracted PSP directory.</summary>
internal interface IGameFiles
{
    GameFileEntry? Find(string path);
    /// <summary>A read-only stream over exactly the file's bytes. The caller disposes it.</summary>
    Stream OpenRead(GameFileEntry entry);
    IEnumerable<GameFileEntry> Enumerate(int maximumEntries);
}

/// <summary>Bounded read-only view of part of a seekable stream; the underlying stream stays open.</summary>
internal sealed class StreamSlice(Stream source, long start, long length) : Stream
{
    private long position;
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        if (position >= length) return 0;
        source.Position = checked(start + position);
        int count = source.Read(buffer[..(int)Math.Min(buffer.Length, length - position)]);
        position += count; return count;
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        long next = checked((origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? position : length) + offset);
        if (next < 0 || next > length) throw new IOException("Seek exceeds source file bounds.");
        return position = next;
    }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Files of an extracted PSP game directory. Paths never leave the root and linked entries are rejected.</summary>
internal sealed class DirectoryGameFiles : IGameFiles
{
    private readonly string root;
    public DirectoryGameFiles(string root)
    {
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!System.IO.Directory.Exists(this.root)) throw new DirectoryNotFoundException("Source directory is missing.");
        RejectLinks(this.root);
    }

    public GameFileEntry? Find(string path)
    {
        if (!Safe(path)) return null;
        string full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        if (File.Exists(full)) { RejectLinks(full); return new(path, 0, new FileInfo(full).Length, false); }
        if (System.IO.Directory.Exists(full)) { RejectLinks(full); return new(path, 0, 0, true); }
        return null;
    }

    public Stream OpenRead(GameFileEntry entry)
    {
        if (entry.Directory || Find(entry.Path) is not { Directory: false } current || current.Length != entry.Length)
            throw new InvalidDataException("Source file is missing or changed.");
        return new FileStream(Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar)), FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public IEnumerable<GameFileEntry> Enumerate(int maximumEntries)
    {
        int count = 0;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string child in System.IO.Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (++count > maximumEntries) throw new InvalidDataException("Source directory has too many entries.");
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked source entries are not supported.");
                string relative = Path.GetRelativePath(root, child).Replace('\\', '/');
                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                if (isDirectory) pending.Push(child);
                yield return new(relative, 0, isDirectory ? 0 : new FileInfo(child).Length, isDirectory);
            }
        }
    }

    private void RejectLinks(string path)
    {
        for (string? p = path; p is not null && p.Length >= root.Length; p = Path.GetDirectoryName(p))
            if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked source entries are not supported.");
    }

    private static bool Safe(string path) => !string.IsNullOrWhiteSpace(path) && !path.Contains('\\') && !path.Contains(':') &&
        !path.StartsWith('/') && path.Split('/').All(s => s.Length > 0 && s is not "." and not "..");
}
