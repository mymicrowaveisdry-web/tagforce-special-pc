using System.Text.Json;
using System.Text.Json.Serialization;

namespace TagForceSpecial.Import.Storage;

/// <summary>Bounded private documents and atomic replacement inside an ordinary local directory.</summary>
public sealed class PrivateStorage
{
    public string Root { get; }
    public static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public PrivateStorage(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (Path.GetDirectoryName(Root) is null) throw new ArgumentException("Choose a private application directory.");
        Check(Root); Directory.CreateDirectory(Root); Check(Root);
    }
    public string PathFor(string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Replace('\\', '/').Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException("Invalid private storage key.");
        string path = Path.GetFullPath(Path.Combine(Root, relative));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Private path leaves the application directory.");
        Check(path); return path;
    }
    public static void Check(string path)
    {
        for (string? p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked storage locations are not supported.");
    }
    public T Read<T>(string relative, int maximumBytes = 1024 * 1024) => Parse<T>(ReadBytes(relative, maximumBytes), Json);

    /// <summary>Reads a bounded file's exact bytes, for documents whose hash is checked before parsing.</summary>
    public byte[] ReadBytes(string relative, int maximumBytes)
    {
        string path = PathFor(relative);
        if (new FileInfo(path).Length > maximumBytes) throw new InvalidDataException("Private document exceeds its limit.");
        return File.ReadAllBytes(path);
    }

    public static T Parse<T>(byte[] bytes, JsonSerializerOptions options)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        Unique(document.RootElement);
        return document.RootElement.Deserialize<T>(options) ?? throw new InvalidDataException("Private document is empty.");
    }
    public void Write<T>(string relative, T value) => WriteBytes(relative, JsonSerializer.SerializeToUtf8Bytes(value, Json));

    /// <summary>Writes through a unique pending file, then replaces the destination in one move.</summary>
    public void WriteBytes(string relative, byte[] bytes)
    {
        string path = PathFor(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); Check(path);
        string temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            Check(path); File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Storage, format and argument failures a caller can report and retry without crashing.</summary>
    public static bool Recoverable(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or
        ArgumentException or JsonException or InvalidOperationException;

    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate private document field."); Unique(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Unique(item);
    }
}
