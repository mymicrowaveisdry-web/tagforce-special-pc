using System.Text.Json;
using System.Text.Json.Serialization;

namespace TagForceSpecial.Import.Manifest;

/// <summary>Overall result of one import session. Only <see cref="Succeeded"/> and <see cref="SucceededWithWarnings"/> replace the active import.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImportStatus>))]
public enum ImportStatus { Succeeded, SucceededWithWarnings, Partial, Failed, Cancelled }

/// <summary>Result of one pipeline stage; <see cref="Reused"/> means a cached output re-validated and was used unchanged.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StageStatus>))]
public enum StageStatus { Pending, Running, Succeeded, Reused, Warning, Failed, Skipped, Cancelled, NotSupported }

/// <summary>What the importer established about a source. The three flags are independent findings, never one boolean.</summary>
/// <param name="Recognized">PSP structure and a Tag Force Special game identifier were found.</param>
/// <param name="StructurallyValid">The container and required files could be read within their bounds.</param>
/// <param name="Supported">The exact build matches a trusted importer profile.</param>
public sealed record SourceIdentity(string Kind, string Path, string DisplayName, long Length, string? Sha256,
    string? GameId, string? Title, string? DiscVersion, bool Recognized, bool StructurallyValid, bool Supported,
    string? BuildId, string? ProfileFingerprint, string[] Notes);

/// <summary>Which stages run. Recipes use <see cref="RecipeLibrary"/> or the data root's previous recipe import.</summary>
public sealed record ImportOptions(bool Catalog = true, bool Presentation = true, bool Recipes = true,
    string? RecipeLibrary = null, bool Inventory = true, bool ReuseCache = true);

public sealed record StageRecord(string Id, string Name, StageStatus Status, DateTimeOffset? StartedUtc, DateTimeOffset? FinishedUtc,
    long? Processed, long? Total, int Warnings, int Errors, string? Message, string? CacheKey, string? OutputDirectory);

/// <param name="Path">Relative to the stage directory, '/'-separated.</param>
public sealed record ArtifactRecord(string Path, long Length, string Sha256);

/// <param name="Directory">Relative to the data root, '/'-separated.</param>
/// <param name="ToolVersion">Converter or decoder identity that produced the output; a change invalidates the cache key.</param>
public sealed record StageOutput(string Stage, string Directory, string CacheKey, string ToolVersion, ArtifactRecord[] Artifacts);

public sealed record PresentationBinding(string PackageId, string Scope, string[] ResourceKeys, string MappingSha256);

public sealed record ImportOutputs(StageOutput? Catalog, StageOutput? Presentation, PresentationBinding? PresentationBinding,
    StageOutput? Recipes, StageOutput? Inventory);

public sealed record ImportManifest(int SchemaVersion, string SessionId, string ImporterVersion, string DecoderSha256,
    DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc, ImportStatus Status, SourceIdentity Source, ImportOptions Options,
    StageRecord[] Stages, ImportOutputs Outputs, int Warnings, int Errors)
{
    public const int CurrentSchema = 1;
}

/// <summary>Pointer to the session the game loads; the manifest hash pins its exact bytes.</summary>
public sealed record ActiveImport(int SchemaVersion, string SessionId, string ManifestSha256);

public static class ManifestJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
    public static T Parse<T>(byte[] bytes) => Storage.PrivateStorage.Parse<T>(bytes, Options);
}
