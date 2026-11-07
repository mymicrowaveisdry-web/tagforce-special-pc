using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Cards.CatalogStorage;

namespace TagForceSpecial.PC.Import;

public sealed record GameCacheManifest(int SchemaVersion, string BuildId, string SourceSha256,
    string ProfileFingerprint, string MappingVersion, string ConversionVersion, int CatalogSchema,
    CacheFileIdentity[] Files);
public sealed class ValidatedGameCache
{
    public string BuildId { get; }
    public string SourceSha256 { get; }
    public LoadedCardCatalog Catalog { get; }
    internal ValidatedGameCache(string buildId, string sourceSha256, LoadedCardCatalog catalog)
        => (BuildId, SourceSha256, Catalog) = (buildId, sourceSha256, catalog);
}
public sealed record GameCacheValidation(ValidatedGameCache? Cache, IReadOnlyList<SourceDiagnostic> Diagnostics)
{
    public bool IsValid => Cache is not null && Diagnostics.Count == 0;
}

/// <summary>Cache foundation that copies pinned raw inputs and the catalog only.</summary>
public static class GameCache
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16
    };

    public static GameCacheValidation Validate(string directory, GameBuildProfile? profile = null)
    {
        profile ??= GameBuildProfile.English21;
        List<SourceDiagnostic> issues = [];
        var actualFiles = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (!Directory.Exists(directory)) return Failure("CacheMissing", "cache", "The selected cache directory is absent.");
            Inspect(directory, "");
            if (issues.Count != 0) return Result();
            string manifestFile = Path.Combine(directory, "cache.json");
            if (!File.Exists(manifestFile)) return Failure("CacheIncomplete", "cache.json", "A completed cache manifest is required.");
            if (new FileInfo(manifestFile).Length > 128 * 1024) return Failure("CacheManifestInvalid", "cache.json", "Cache manifest exceeds its bounds.");
            using var jsonDocument = JsonDocument.Parse(File.ReadAllBytes(manifestFile), new JsonDocumentOptions { MaxDepth = 16 });
            RejectDuplicates(jsonDocument.RootElement);
            var manifest = jsonDocument.RootElement.Deserialize<GameCacheManifest>(Json);
            if (manifest is null || manifest.SchemaVersion != profile.CacheSchema || manifest.BuildId != profile.BuildId ||
                manifest.SourceSha256 != profile.ImageSha256 || manifest.ProfileFingerprint != profile.Fingerprint ||
                manifest.MappingVersion != profile.MappingVersion || manifest.ConversionVersion != profile.ConversionVersion ||
                manifest.CatalogSchema != CardCatalogPackage.SchemaVersion || manifest.Files is null || !manifest.Files.SequenceEqual(profile.CacheFiles))
                return Failure("CacheVersionMismatch", "cache.json", "Source, schema, mapping, conversion or artifact identities differ from the trusted profile.");
            var expectedPaths = profile.CacheFiles.Select(f => f.Path).Append("cache.json").ToHashSet(StringComparer.Ordinal);
            foreach (string path in actualFiles.Except(expectedPaths).Order(StringComparer.Ordinal))
                issues.Add(new("UnexpectedCacheFile", path, "File is not in the selected cache mapping."));
            foreach (var file in profile.CacheFiles)
            {
                string path = Local(directory, file.Path);
                if (!File.Exists(path)) issues.Add(new("MissingCacheFile", file.Path, "Required cache artifact is absent."));
                else if (new FileInfo(path).Length != file.Length) issues.Add(new("CacheFileLengthMismatch", file.Path, "Cache artifact length differs from the trusted profile."));
                else if (HashFile(path) != file.Sha256) issues.Add(new("CacheFileHashMismatch", file.Path, "Cache artifact SHA-256 differs from the trusted profile."));
            }
            if (issues.Count != 0) return Result();
            var catalog = CardCatalogPackage.Load(Path.Combine(directory, "catalog"));
            if (catalog.Catalog.Source.ContainerSha256 != profile.CatalogSourceSha256)
                return Failure("CacheCatalogSourceMismatch", "catalog", "Catalog belongs to a different original source.");
            // Pinned bytes are verified above; reviewed card metadata is code-owned and overlaid after validation.
            var opened = catalog with { Catalog = Effects.ReviewedCardMetadata.Overlay(catalog.Catalog) };
            return new(new(profile.BuildId, profile.ImageSha256, opened), Array.Empty<SourceDiagnostic>());
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return Failure("CacheUnreadable", "cache", "Cache structure or content cannot be fully validated."); }

        void Inspect(string path, string relative)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            { issues.Add(new("CacheLinkRejected", relative.Length == 0 ? "cache" : relative, "Cache entries must be ordinary local files/directories.")); return; }
            if (relative.Count(c => c == '/') > 8) throw new InvalidDataException("Cache directory depth exceeds the mapping.");
            foreach (string child in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal))
            {
                string key = relative.Length == 0 ? Path.GetFileName(child) : relative + "/" + Path.GetFileName(child);
                var attrs = File.GetAttributes(child);
                if ((attrs & FileAttributes.ReparsePoint) != 0) issues.Add(new("CacheLinkRejected", key, "Cache entry is a link."));
                else if ((attrs & FileAttributes.Directory) != 0) Inspect(child, key);
                else actualFiles.Add(key);
                if (actualFiles.Count > 1000) throw new InvalidDataException("Cache contains too many files.");
            }
        }
        GameCacheValidation Failure(string code, string path, string detail) { issues.Add(new(code, path, detail)); return Result(); }
        GameCacheValidation Result() => new(null, Array.AsReadOnly(issues.OrderBy(i => i.Path, StringComparer.Ordinal).ThenBy(i => i.Code, StringComparer.Ordinal).ToArray()));
    }

    public static GameCacheValidation Create(ValidatedGameSource source, string catalogPackage, string destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        string parent = Path.GetDirectoryName(target) ?? throw new ArgumentException("Cache requires a parent directory.");
        string temporary = Path.Combine(parent, ".import-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(target) || File.Exists(target)) return Fail("CacheAlreadyExists", "Existing caches are validated/reused explicitly; select a new directory to rebuild.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var catalog = CardCatalogPackage.Load(catalogPackage);
            if (catalog.Catalog.Source.ContainerSha256 != source.Profile.CatalogSourceSha256)
                return Fail("CacheCatalogSourceMismatch", "The supplied catalog does not originate from this supported source.");
            Directory.CreateDirectory(temporary);
            foreach (var file in source.Profile.SourceFiles.Where(f => f.CachePath is not null))
            {
                string output = Local(temporary, file.CachePath!);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyMappedFile(file.Path, stream, cancellationToken);
            }
            Directory.CreateDirectory(Path.Combine(temporary, "catalog"));
            foreach (string name in new[] { "manifest.json", "cards.json" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(Path.Combine(catalogPackage, name), Path.Combine(temporary, "catalog", name), overwrite: false);
            }
            var profile = source.Profile;
            var manifest = new GameCacheManifest(profile.CacheSchema, profile.BuildId, profile.ImageSha256,
                profile.Fingerprint, profile.MappingVersion, profile.ConversionVersion, CardCatalogPackage.SchemaVersion, profile.CacheFiles.ToArray());
            File.WriteAllBytes(Path.Combine(temporary, "cache.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
            var validated = Validate(temporary, profile);
            if (!validated.IsValid) return validated;
            cancellationToken.ThrowIfCancellationRequested();
            if (!source.IsUnchanged(cancellationToken)) return Fail("SourceChanged", "Original image changed during import; the cache was not published.");
            Directory.Move(temporary, target);
            return validated;
        }
        catch (OperationCanceledException) { return Fail("ImportCancelled", "Import was cancelled before publishing a completed cache."); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or ObjectDisposedException)
        { return Fail("ImportFailed", "Source/catalog could not be copied and validated; no completed cache was published."); }
        finally
        {
            // This is our own unique sibling staging directory, never the user's source or an existing cache.
            if (Directory.Exists(temporary))
            {
                string resolved = Path.GetFullPath(temporary);
                if (!string.Equals(Path.GetDirectoryName(resolved), parent, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                    !Path.GetFileName(resolved).StartsWith(".import-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Cache cleanup target leaves its staging parent.");
                Directory.Delete(resolved, recursive: true);
            }
        }
        GameCacheValidation Fail(string code, string detail) => new(null, Array.AsReadOnly(new[] { new SourceDiagnostic(code, "cache", detail) }));
    }

    private static string Local(string root, string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { if (!seen.Add(property.Name)) throw new InvalidDataException("Duplicate cache property."); RejectDuplicates(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
