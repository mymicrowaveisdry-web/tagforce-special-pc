using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TagForceSpecial.Assets.GameSource;

namespace TagForceSpecial.Assets.Presentation;

public sealed record AssetManifestSource(string Archive, string? Member, long Offset,
    string SourceSha256, string StoredSha256, string InputSha256);
public sealed record AssetManifestOutput(string RelativePath, string ContentSha256,
    string? PixelSha256, int? Width, int? Height, IReadOnlyList<int>? SourceRectangle);
public sealed record AssetManifestEntry(int SchemaVersion, string BuildId, string PackageId,
    string AssetKey, string ResourceKey, AssetManifestSource Source, AssetManifestOutput Output,
    string Status, IReadOnlyList<string> EvidenceIds);
public sealed record ResolvedSemanticAsset(AssetManifestEntry Manifest, byte[] Content);

/// <summary>A pinned private v2 mapping sidecar over a validated content package.
/// Mapping integrity grants no runtime approval or PSP-reference verification.</summary>
public sealed class SemanticAssetRegistry
{
    public const int SchemaVersion = 2;
    public const int MaximumManifestBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true
    };
    private static readonly Regex SemanticKey = new("^[a-z][a-z0-9-]*(?:\\.[a-z0-9-]+)+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly PresentationPackage package;
    public IReadOnlyDictionary<string, AssetManifestEntry> Entries { get; }

    private SemanticAssetRegistry(PresentationPackage package, Dictionary<string, AssetManifestEntry> entries)
    {
        this.package = package;
        Entries = new ReadOnlyDictionary<string, AssetManifestEntry>(entries);
    }

    public static byte[] Encode(IEnumerable<AssetManifestEntry> entries) =>
        JsonSerializer.SerializeToUtf8Bytes(entries.OrderBy(e => e.AssetKey, StringComparer.Ordinal), Json);

    public static SemanticAssetRegistry Open(string privateRoot, string expectedPackageId,
        GameBuildProfile profile, string expectedScope, IEnumerable<string> requiredResourceKeys,
        ReadOnlySpan<byte> manifestJson, string expectedManifestSha256)
    {
        Require(manifestJson.Length is > 0 and <= MaximumManifestBytes &&
            Convert.ToHexStringLower(SHA256.HashData(manifestJson)) == expectedManifestSha256,
            "Semantic manifest identity/size mismatch.");
        // Reopen the complete package before any semantic resource can resolve.
        var package = PresentationPackage.Open(privateRoot, expectedPackageId, profile,
            expectedScope, requiredResourceKeys);
        AssetManifestEntry[] rows;
        try { rows = JsonSerializer.Deserialize<AssetManifestEntry[]>(manifestJson, Json)
            ?? throw new InvalidDataException("Missing semantic manifest."); }
        catch (JsonException error) { throw new InvalidDataException("Invalid semantic manifest JSON.", error); }
        Require(rows.Length is > 0 and <= PresentationPackage.MaximumResources, "Invalid semantic manifest count.");
        var entries = new Dictionary<string, AssetManifestEntry>(StringComparer.Ordinal);
        foreach (var entry in rows)
        {
            Require(entry is not null && entry.SchemaVersion == SchemaVersion &&
                entry.BuildId == profile.BuildId && entry.PackageId == expectedPackageId &&
                entry.AssetKey is { Length: > 0 and <= 200 } && SemanticKey.IsMatch(entry.AssetKey) &&
                entry.ResourceKey is not null && entry.Source is not null && entry.Output is not null &&
                entry.EvidenceIds is not null, "Invalid semantic asset identity.");
            Require(package.Resources.TryGetValue(entry!.ResourceKey, out var resource), "Missing package resource: " + entry.ResourceKey);
            var source = entry.Source;
            var output = entry.Output;
            Require(source.Archive == resource!.SourcePath && source.Member == resource.MemberName &&
                source.Offset == resource.Offset && source.SourceSha256 == resource.SourceSha256 &&
                source.StoredSha256 == resource.StoredSha256 && source.InputSha256 == resource.InputSha256,
                "Semantic source provenance differs from validated package.");
            Require(output.RelativePath == "blobs/" + resource.ContentSha256 + ".bin" &&
                output.ContentSha256 == resource.ContentSha256 && output.Width == resource.Width &&
                output.Height == resource.Height && output.PixelSha256 ==
                (resource.Conversion is "gim-rgba-v1" or "cpj-rgba-v1" or "cpj-native-rgba-v1" ? resource.ContentSha256 : null),
                "Semantic output differs from validated package.");
            Require(entry.Status is "stored" or "decoded" or "mapped-unverified" &&
                (entry.Status == "mapped-unverified" || entry.Status == resource.EvidenceStatus),
                "Unsupported semantic evidence status; approvals require separate reviewed evidence.");
            Require(entry.EvidenceIds.All(id => id is { Length: > 0 and <= 200 } && SemanticKey.IsMatch(id)) &&
                entry.EvidenceIds.Distinct(StringComparer.Ordinal).Count() == entry.EvidenceIds.Count &&
                (entry.Status != "mapped-unverified" || entry.EvidenceIds.Count > 0),
                "Mapped assets require explicit evidence identifiers.");
            if (output.SourceRectangle is { } rect)
                Require(resource.Width is not null && resource.Height is not null && rect.Count == 4 &&
                    rect[0] >= 0 && rect[1] >= 0 && rect[2] > 0 && rect[3] > 0 &&
                    (long)rect[0] + rect[2] <= resource.Width && (long)rect[1] + rect[3] <= resource.Height,
                    "Source rectangle exceeds decoded texture bounds.");
            // A semantic full-art name cannot relabel a thumbnail or a generic texture.
            Require(!entry.AssetKey.StartsWith("deck.card-art.full.", StringComparison.Ordinal) &&
                !entry.AssetKey.StartsWith("card.art.", StringComparison.Ordinal) ||
                resource.Artwork is not null && resource.Conversion is CpjArtworkDecoder.ConversionId or CpjArtworkDecoder.NativeConversionId &&
                (entry.AssetKey == "deck.card-art.full." + resource.Artwork.CardId.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                 entry.AssetKey == CardArtworkProvider.FullArtKey(resource.Artwork.CardId)),
                "Missing verified full-art converter; semantic thumbnail aliases are forbidden.");
            var copy = entry with
            {
                Output = output with { SourceRectangle = output.SourceRectangle is null ? null :
                    Array.AsReadOnly(output.SourceRectangle.ToArray()) },
                EvidenceIds = Array.AsReadOnly(entry.EvidenceIds.ToArray())
            };
            Require(entries.TryAdd(copy.AssetKey, copy), "Duplicate semantic asset key.");
        }
        return new(package, entries);
    }

    public ResolvedSemanticAsset Resolve(string assetKey)
    {
        if (!Entries.TryGetValue(assetKey, out var entry)) throw new KeyNotFoundException("Missing semantic asset: " + assetKey);
        // ReadResource checks the actual bytes again; paths in the sidecar never drive I/O.
        return new(entry, package.ReadResource(entry.ResourceKey));
    }

    public string? VerifiedReferenceScope(string assetKey)
    {
        var resolved = Resolve(assetKey);
        return PresentationReferenceCases.Match(resolved.Manifest,
            package.Resources[resolved.Manifest.ResourceKey], package.SourceProfileFingerprint);
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
