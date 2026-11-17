using System.Security.Cryptography;

namespace TagForceSpecial.Assets.GameSource;

public sealed class ValidatedGameSource : IDisposable
{
    private readonly Stream? image;
    internal IGameFiles Files { get; }
    internal IReadOnlyDictionary<string, GameFileEntry> Entries { get; }
    public GameBuildProfile Profile { get; }
    /// <summary>"iso" for an image validated by its whole SHA-256; "directory" for an extracted game validated file by file.</summary>
    public string Kind => image is null ? "directory" : "iso";

    internal ValidatedGameSource(Stream? image, IGameFiles files, GameBuildProfile profile, IReadOnlyDictionary<string, GameFileEntry> entries)
        => (this.image, Files, Profile, Entries) = (image, files, profile, entries);

    public void CopyMappedFile(string sourcePath, Stream destination, CancellationToken cancellationToken = default)
    {
        if (!Profile.SourceFiles.Any(f => f.Path == sourcePath && f.CachePath is not null) || !Entries.TryGetValue(sourcePath, out var entry))
            throw new ArgumentException("Only an explicitly mapped, validated source file may be copied.");
        using var input = Files.OpenRead(entry);
        long remaining = entry.Length;
        var buffer = new byte[128 * 1024];
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = input.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
            if (read == 0) throw new EndOfStreamException();
            destination.Write(buffer, 0, read); remaining -= read;
        }
    }

    /// <summary>ISO: the whole image still has the profile hash. Directory: every validated pinned file still has its hash.</summary>
    public bool IsUnchanged(CancellationToken cancellationToken = default)
    {
        if (image is not null)
            return image.Length == Profile.ImageLength && GameSourceValidator.Hash(image, 0, image.Length, cancellationToken) == Profile.ImageSha256;
        return Entries.All(pair =>
        {
            var pin = Profile.SourceFiles.Single(f => f.Path == pair.Key);
            try { using var input = Files.OpenRead(pair.Value); return GameSourceValidator.Hash(input, 0, input.Length, cancellationToken) == pin.Sha256; }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
        });
    }

    public void Dispose() => image?.Dispose();
}

public sealed record GameSourceValidation(ValidatedGameSource? Source, IReadOnlyList<SourceDiagnostic> Diagnostics)
{
    public bool IsValid => Source is not null && Diagnostics.Count == 0;
}

public static class GameSourceValidator
{
    public static GameSourceValidation Open(string isoPath, GameBuildProfile? profile = null,
        CancellationToken cancellationToken = default, IProgress<long>? progress = null)
    {
        profile ??= GameBuildProfile.English21;
        FileStream? image = null;
        List<SourceDiagnostic> issues = [];
        try
        {
            image = new(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (image.Length != profile.ImageLength) issues.Add(new("SourceLengthMismatch", "image", $"Expected {profile.ImageLength} bytes; found {image.Length}."));
            else if (Hash(image, 0, image.Length, cancellationToken, progress) != profile.ImageSha256) issues.Add(new("SourceHashMismatch", "image", "Image SHA-256 is not the selected supported build."));
            if (issues.Count != 0) return Finish(); // Reject a wrong build before reading/using its files.
            var iso = new Iso9660Source(image);
            var entries = new Dictionary<string, GameFileEntry>(StringComparer.Ordinal);
            foreach (var file in profile.SourceFiles)
            {
                var entry = iso.Find(file.Path);
                if (entry is null || entry.Directory) issues.Add(new("MissingSourceFile", file.Path, "Required source file is absent."));
                else if (entry.Length != file.Length) issues.Add(new("SourceFileLengthMismatch", file.Path, "Required file length differs."));
                else if (Hash(image, entry.Offset, entry.Length, cancellationToken) != file.Sha256) issues.Add(new("SourceFileHashMismatch", file.Path, "Required file SHA-256 differs."));
                else entries.Add(file.Path, entry);
            }
            if (issues.Count != 0) return Finish();
            var receipt = new ValidatedGameSource(image, iso, profile, entries); image = null;
            return new(receipt, Array.Empty<SourceDiagnostic>());
        }
        catch (InvalidDataException) { issues.Add(new("InvalidSourceStructure", "image", "The supported image's ISO structure is invalid or unsupported.")); return Finish(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { issues.Add(new("SourceUnavailable", "image", "The ISO cannot be opened completely for read-only validation.")); return Finish(); }
        finally { image?.Dispose(); }

        GameSourceValidation Finish() => Sorted(issues);
    }

    /// <summary>Validates an extracted game directory (the folder holding <c>PSP_GAME</c>) against the profile's pinned files.
    /// Pinned files outside <c>PSP_GAME/</c>, such as <c>UMD_DATA.BIN</c>, are optional because extractions often omit them.</summary>
    public static GameSourceValidation OpenDirectory(string directory, GameBuildProfile? profile = null,
        CancellationToken cancellationToken = default, IProgress<long>? progress = null)
    {
        profile ??= GameBuildProfile.English21;
        List<SourceDiagnostic> issues = [];
        try
        {
            var files = new DirectoryGameFiles(directory);
            var entries = new Dictionary<string, GameFileEntry>(StringComparer.Ordinal);
            long hashed = 0;
            foreach (var file in profile.SourceFiles)
            {
                var entry = files.Find(file.Path);
                bool optional = !file.Path.StartsWith("PSP_GAME/", StringComparison.Ordinal);
                if (entry is null && optional) continue;
                if (entry is null || entry.Directory) issues.Add(new("MissingSourceFile", file.Path, "Required source file is absent."));
                else if (entry.Length != file.Length) issues.Add(new("SourceFileLengthMismatch", file.Path, "Required file length differs."));
                else
                {
                    using var input = files.OpenRead(entry);
                    if (Hash(input, 0, entry.Length, cancellationToken) != file.Sha256) issues.Add(new("SourceFileHashMismatch", file.Path, "Required file SHA-256 differs."));
                    else entries.Add(file.Path, entry);
                    progress?.Report(hashed += entry.Length);
                }
            }
            if (issues.Count != 0) return Sorted(issues);
            return new(new ValidatedGameSource(null, files, profile, entries), Array.Empty<SourceDiagnostic>());
        }
        catch (InvalidDataException) { issues.Add(new("InvalidSourceStructure", "directory", "The directory contains linked or invalid entries.")); return Sorted(issues); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { issues.Add(new("SourceUnavailable", "directory", "The directory cannot be read completely for read-only validation.")); return Sorted(issues); }
    }

    private static GameSourceValidation Sorted(List<SourceDiagnostic> issues) =>
        new(null, Array.AsReadOnly(issues.OrderBy(i => i.Path, StringComparer.Ordinal).ThenBy(i => i.Code, StringComparer.Ordinal).ToArray()));

    internal static string Hash(Stream stream, long offset, long length, CancellationToken cancellationToken = default, IProgress<long>? progress = null)
    {
        stream.Position = offset;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long consumed = 0;
        while (length != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, 0, (int)Math.Min(length, buffer.Length));
            if (read == 0) throw new EndOfStreamException();
            hash.AppendData(buffer, 0, read); length -= read;
            consumed += read; progress?.Report(consumed);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
