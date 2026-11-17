namespace TagForceSpecial.Assets.GameSource;

/// <summary>Read-only browsing of any ISO 9660 image or extracted directory, supported build or not. It never validates identity;
/// use <see cref="GameSourceValidator"/> for that. Reads are serialized because ISO files share one image stream.</summary>
public sealed class GameFileSystem : IDisposable
{
    public const int MaximumEntries = 200_000;
    private readonly Stream? image;
    private readonly IGameFiles files;
    private readonly object gate = new();
    public string Kind { get; }
    public string Root { get; }

    private GameFileSystem(string kind, string root, Stream? image, IGameFiles files) => (Kind, Root, this.image, this.files) = (kind, root, image, files);

    /// <summary>Opens a directory as an extracted game, anything else as an ISO image.</summary>
    public static GameFileSystem Open(string path) => Directory.Exists(path) ? OpenDirectory(path) : OpenIso(path);

    public static GameFileSystem OpenIso(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
        try { return new("iso", Path.GetFullPath(path), stream, new Iso9660Source(stream)); }
        catch { stream.Dispose(); throw; }
    }

    public static GameFileSystem OpenDirectory(string path) => new("directory", Path.GetFullPath(path), null, new DirectoryGameFiles(path));

    public long ContainerLength => image?.Length ?? 0;

    public GameFileEntry? Find(string path) { lock (gate) return files.Find(path); }

    public IReadOnlyList<GameFileEntry> List(int maximumEntries = MaximumEntries)
    {
        lock (gate) return files.Enumerate(maximumEntries).ToArray();
    }

    /// <summary>Reads up to <paramref name="count"/> bytes from <paramref name="offset"/> inside the file.</summary>
    public byte[] Read(GameFileEntry entry, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset > entry.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        int length = (int)Math.Min(count, entry.Length - offset);
        lock (gate)
        {
            using var input = files.OpenRead(entry);
            input.Position = offset;
            var data = new byte[length]; input.ReadExactly(data); return data;
        }
    }

    /// <summary>Reads a whole file when it is no larger than <paramref name="maximum"/> bytes.</summary>
    public byte[] ReadAll(GameFileEntry entry, int maximum)
    {
        if (entry.Length > maximum) throw new InvalidDataException("File exceeds the read limit.");
        return Read(entry, 0, (int)entry.Length);
    }

    /// <summary>Runs <paramref name="read"/> over the file's stream while holding the read lock.</summary>
    public T WithStream<T>(GameFileEntry entry, Func<Stream, T> read)
    {
        lock (gate) { using var input = files.OpenRead(entry); return read(input); }
    }

    public void Dispose() => image?.Dispose();
}
