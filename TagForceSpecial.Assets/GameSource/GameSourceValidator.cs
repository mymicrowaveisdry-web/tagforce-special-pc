using System.Security.Cryptography;

namespace TagForceSpecial.Assets.GameSource;

public sealed class ValidatedGameSource : IDisposable
{
    internal Stream Image { get; }
    internal IReadOnlyDictionary<string, (long Offset, long Length)> Entries { get; }
    public GameBuildProfile Profile { get; }
    internal ValidatedGameSource(Stream image, GameBuildProfile profile, IReadOnlyDictionary<string, (long, long)> entries)
        => (Image, Profile, Entries) = (image, profile, entries);
    public void CopyMappedFile(string sourcePath, Stream destination, CancellationToken cancellationToken = default)
    {
        if (!Profile.SourceFiles.Any(f => f.Path == sourcePath && f.CachePath is not null) || !Entries.TryGetValue(sourcePath, out var entry))
            throw new ArgumentException("Only an explicitly mapped, validated source file may be copied.");
        Image.Position = entry.Offset;
        long remaining = entry.Length;
        var buffer = new byte[128 * 1024];
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = Image.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
            if (read == 0) throw new EndOfStreamException();
            destination.Write(buffer, 0, read); remaining -= read;
        }
    }
    public bool IsUnchanged(CancellationToken cancellationToken = default) => Image.Length == Profile.ImageLength &&
        GameSourceValidator.Hash(Image, 0, Image.Length, cancellationToken) == Profile.ImageSha256;
    public void Dispose() => Image.Dispose();
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
            var entries = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
            foreach (var file in profile.SourceFiles)
            {
                var entry = iso.Find(file.Path);
                if (entry is null || entry.Directory) issues.Add(new("MissingSourceFile", file.Path, "Required source file is absent."));
                else if (entry.Length != file.Length) issues.Add(new("SourceFileLengthMismatch", file.Path, "Required file length differs."));
                else if (Hash(image, entry.Offset, entry.Length, cancellationToken) != file.Sha256) issues.Add(new("SourceFileHashMismatch", file.Path, "Required file SHA-256 differs."));
                else entries.Add(file.Path, (entry.Offset, entry.Length));
            }
            if (issues.Count != 0) return Finish();
            var receipt = new ValidatedGameSource(image, profile, entries); image = null;
            return new(receipt, Array.Empty<SourceDiagnostic>());
        }
        catch (InvalidDataException) { issues.Add(new("InvalidSourceStructure", "image", "The supported image's ISO structure is invalid or unsupported.")); return Finish(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { issues.Add(new("SourceUnavailable", "image", "The ISO cannot be opened completely for read-only validation.")); return Finish(); }
        finally { image?.Dispose(); }

        GameSourceValidation Finish() => new(null, Array.AsReadOnly(issues.OrderBy(i => i.Path, StringComparer.Ordinal).ThenBy(i => i.Code, StringComparer.Ordinal).ToArray()));
    }

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
