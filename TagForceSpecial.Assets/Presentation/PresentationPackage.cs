using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TagForceSpecial.Assets.GameSource;

namespace TagForceSpecial.Assets.Presentation;

public enum PresentationConversion { Stored, GimRgba, CpjRgba, CpjReferenceRgba }
public sealed record PresentationResourceRequest(string AssetKey, string SourcePath,
    string? MemberName = null, PresentationConversion Conversion = PresentationConversion.GimRgba,
    int? PaletteLevel = null, EhpReadMode ArchiveMode = EhpReadMode.Strict, uint? CardId = null);

public sealed record PresentationResource(string AssetKey, string SourcePath, string SourceSha256,
    string? MemberName, long Offset, long StoredLength, string StoredSha256, string InputSha256,
    string Conversion, int? PaletteLevel, int? Width, int? Height, string ContentSha256, long ContentLength,
    string EvidenceStatus, IReadOnlyList<string> Diagnostics,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CpjArtworkProvenance? Artwork = null);

/// <summary>
/// Content-addressed private resources, validated by hash on open and read. Integrity covers bytes only.
/// </summary>
public sealed class PresentationPackage
{
    public const int SchemaVersion = 2;
    public const string ConverterId = "tfsp-private-presentation-v2";
    public const int MaximumResources = 4096;
    public const int MaximumResourceBytes = 64 * 1024 * 1024;
    private const int MaximumManifestBytes = 4 * 1024 * 1024;
    private const string PipelineVersion = "private-presentation-v2;stored-v1;gim-rgba-v1;cpj-rgba-v1;cpj-native-rgba-v1";
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private sealed record Manifest(int Schema, string Scope, string BuildId, string SourceSha256,
        string ProfileFingerprint, string Pipeline, string ConverterId, string DecoderSha256, PresentationResource[] Resources);
    private sealed record Signoff(string Claim, string PackageId, string Scope, string SourceSha256,
        string ProfileFingerprint, string DecoderSha256, int Resources);
    private readonly string privateRoot;
    private readonly Manifest manifest;
    private readonly IReadOnlyDictionary<string, PresentationResource> resources;
    public string PackageId { get; }
    public string Scope => manifest.Scope;
    public string SourceProfileFingerprint => manifest.ProfileFingerprint;
    public string DirectoryPath => Path.Combine(privateRoot, "packages", PackageId);
    public IReadOnlyDictionary<string, PresentationResource> Resources => resources;

    private PresentationPackage(string root, string id, Manifest manifest)
    {
        privateRoot = root; PackageId = id; this.manifest = manifest;
        // Do not expose the deserializer's mutable arrays/lists.
        resources = new ReadOnlyDictionary<string, PresentationResource>(manifest.Resources.ToDictionary(
            r => r.AssetKey, r => r with { Diagnostics = Array.AsReadOnly(r.Diagnostics.ToArray()) }, StringComparer.Ordinal));
    }

    public static PresentationPackage Create(ValidatedGameSource source, string privateRoot, string scope,
        IEnumerable<PresentationResourceRequest> requests, string? jpegDecoderExecutable = null, string? nativeReferenceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requests);
        string root = PrivateRoot(privateRoot);
        Require(Key(scope), "Scope must be an explicit normalized identifier.");
        var plan = requests.Take(MaximumResources + 1).ToArray();
        Require(plan.Length is > 0 and <= MaximumResources, "Explicit nonempty bounded resource scope required.");
        Require(plan.All(r => r is not null && Key(r.AssetKey) && GameBuildProfile.SafePath(r.SourcePath) &&
            (r.MemberName is null || Member(r.MemberName)) && Enum.IsDefined(r.Conversion) && Enum.IsDefined(r.ArchiveMode) &&
            r.PaletteLevel is null or >= 0 && (r.Conversion != PresentationConversion.Stored || r.PaletteLevel is null) &&
            (r.Conversion is PresentationConversion.CpjRgba or PresentationConversion.CpjReferenceRgba ? r.CardId is > 0 && r.PaletteLevel is null : r.CardId is null)),
            "Invalid resource request.");
        Require(plan.Select(r => r.AssetKey).Distinct(StringComparer.Ordinal).Count() == plan.Length, "Duplicate asset key.");
        Require(!plan.Any(r => CardArtworkProvider.IsUnsupportedArtKey(r.AssetKey) &&
            (r.Conversion is not (PresentationConversion.CpjRgba or PresentationConversion.CpjReferenceRgba) || r.AssetKey != CardArtworkProvider.FullArtKey(r.CardId!.Value))),
            "card.art keys require a verified full-art converter; thumbnail aliases are forbidden.");
        Array.Sort(plan, (a, b) => StringComparer.Ordinal.Compare(a.AssetKey, b.AssetKey));
        Require(source.IsUnchanged(), "Validated source changed before normalization.");
        string decoder = DecoderHash();
        Directory.CreateDirectory(root);
        string stage = Path.Combine(root, ".building-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(stage, "blobs"));
        try
        {
            var iso = new Iso9660Source(source.Image);
            var records = new List<PresentationResource>();
            foreach (var request in plan)
            {
                var selected = PresentationSourceReader.Read(source, iso, request);
                var decoded = PresentationResourceDecoder.Decode(selected, request, jpegDecoderExecutable, nativeReferenceRoot);
                byte[] content = decoded.Content;
                var diagnostics = selected.Diagnostics.Concat(decoded.Diagnostics).ToArray();
                string contentHash = Hash(content);
                string blob = Path.Combine(stage, "blobs", contentHash + ".bin");
                if (!File.Exists(blob)) WriteNew(blob, content);
                records.Add(new(request.AssetKey, request.SourcePath, selected.SourceSha256, request.MemberName, selected.Offset,
                    selected.Stored.Length, Hash(selected.Stored), decoded.InputSha256,
                    request.Conversion == PresentationConversion.CpjReferenceRgba ? CpjArtworkDecoder.NativeConversionId :
                        request.Conversion == PresentationConversion.CpjRgba ? CpjArtworkDecoder.ConversionId :
                        request.Conversion == PresentationConversion.GimRgba ? "gim-rgba-v1" : "stored-v1",
                    request.PaletteLevel, decoded.Width, decoded.Height, contentHash, content.Length,
                    request.Conversion != PresentationConversion.Stored ? "decoded" : "stored",
                    Array.AsReadOnly(diagnostics.ToArray()), decoded.Artwork));
            }
            Require(source.IsUnchanged(), "Validated source changed during normalization.");
            var manifest = new Manifest(SchemaVersion, scope, source.Profile.BuildId, source.Profile.ImageSha256,
                source.Profile.Fingerprint, PipelineVersion, ConverterId, decoder, records.ToArray());
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
            Require(bytes.Length <= MaximumManifestBytes, "Manifest exceeds limit.");
            WriteNew(Path.Combine(stage, "manifest.json"), bytes);
            string id = Hash(bytes), packages = Path.Combine(root, "packages");
            Directory.CreateDirectory(packages); CheckAncestors(packages);
            string destination = Path.Combine(packages, id);
            if (!Directory.Exists(destination)) Directory.Move(stage, destination);
            return Open(root, id, source.Profile, scope, plan.Select(r => r.AssetKey));
        }
        finally
        {
            // Only remove this newly created staging directory under the checked private root.
            if (Directory.Exists(stage))
            {
                Require(Path.GetDirectoryName(Path.GetFullPath(stage)) == root, "Unsafe staging cleanup target.");
                CheckAncestors(stage);
                Directory.Delete(stage, recursive: true);
            }
        }
    }

    public static PresentationPackage Open(string privateRoot, string expectedPackageId, GameBuildProfile profile,
        string expectedScope, IEnumerable<string> requiredAssetKeys)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string root = PrivateRoot(privateRoot);
        Require(HashText(expectedPackageId) && Key(expectedScope), "Expected package identity/scope required.");
        var required = requiredAssetKeys.Take(MaximumResources + 1).Order(StringComparer.Ordinal).ToArray();
        Require(required.Length is > 0 and <= MaximumResources && required.All(Key) && required.Distinct().Count() == required.Length,
            "Explicit unique required assets are necessary for validation.");
        string directory = Path.Combine(root, "packages", expectedPackageId);
        CheckAncestors(directory);
        string path = Path.Combine(directory, "manifest.json"); CheckAncestors(path);
        byte[] bytes = ReadFile(path, MaximumManifestBytes);
        Require(Hash(bytes) == expectedPackageId, "Package manifest hash mismatch.");
        using (var document = JsonDocument.Parse(bytes))
            Require(document.RootElement.TryGetProperty("Schema", out var schema) && schema.TryGetInt32(out int version) && version == SchemaVersion,
                "Presentation schema upgrade requires rebuild from the verified ISO into a new cache generation; legacy packages are retained.");
        Manifest manifest;
        try { manifest = JsonSerializer.Deserialize<Manifest>(bytes, Json) ?? throw new InvalidDataException("Missing manifest."); }
        catch (JsonException error) { throw new InvalidDataException("Invalid package manifest.", error); }
        Require(bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(manifest, Json)), "Manifest is not canonical.");
        Require(manifest.Schema == SchemaVersion && manifest.Scope == expectedScope && manifest.BuildId == profile.BuildId &&
            manifest.SourceSha256 == profile.ImageSha256 && manifest.ProfileFingerprint == profile.Fingerprint &&
            manifest.Pipeline == PipelineVersion && manifest.ConverterId == ConverterId && manifest.DecoderSha256 == DecoderHash(), "Package source/profile/decoder version mismatch.");
        Require(manifest.Resources is { Length: > 0 and <= MaximumResources } && manifest.Resources.All(r => r is not null) &&
            manifest.Resources.Select(r => r.AssetKey).SequenceEqual(required), "Required package resources differ.");
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "manifest.json" };
        foreach (var r in manifest.Resources)
        {
            Require(!CardArtworkProvider.IsUnsupportedArtKey(r.AssetKey) ||
                r.Artwork is not null && r.AssetKey == CardArtworkProvider.FullArtKey(r.Artwork.CardId),
                "Unverified full-art resources and legacy thumbnail aliases are forbidden.");
            Require(GameBuildProfile.SafePath(r.SourcePath) && HashText(r.SourceSha256) && HashText(r.StoredSha256) &&
                HashText(r.InputSha256) && HashText(r.ContentSha256) && (r.MemberName is null || Member(r.MemberName)) &&
                r.Offset >= 0 && r.StoredLength is >= 0 and <= MaximumResourceBytes &&
                r.ContentLength is >= 0 and <= MaximumResourceBytes && r.Diagnostics is not null &&
                r.Diagnostics.All(d => d is not null) && r.PaletteLevel is null or >= 0, "Invalid resource provenance.");
            Require(r.Conversion == "stored-v1" && r.EvidenceStatus == "stored" && r.Width is null && r.Height is null && r.PaletteLevel is null ||
                r.Conversion is "gim-rgba-v1" or "cpj-rgba-v1" or "cpj-native-rgba-v1" && r.EvidenceStatus == "decoded" && r.Width is > 0 and <= GimTextureDecoder.MaximumDimension &&
                r.Height is > 0 and <= GimTextureDecoder.MaximumDimension && r.ContentLength == (long)r.Width * r.Height * 4,
                "Invalid normalized format/evidence status.");
            Require(r.Conversion is CpjArtworkDecoder.ConversionId or CpjArtworkDecoder.NativeConversionId ? r.Artwork is { CardId: > 0 } &&
                r.Artwork.HeaderSourcePath == CpjArtworkSourceReader.HeaderSource &&
                r.Artwork.HeaderSourceSha256 == CpjArtworkSourceReader.BootSha256 &&
                r.Artwork.HeaderOffset == CpjArtworkSourceReader.HeaderOffset && r.Artwork.HeaderLength == 160 &&
                r.Artwork.HeaderSha256 == CpjArtworkSourceReader.HeaderSha256 &&
                HashText(r.Artwork.JpegSha256) && HashText(r.Artwork.PixelDecoderSha256) &&
                r.Artwork.JpegSha256 == r.InputSha256 && r.Width == 256 && r.Height == 256 &&
                r.PaletteLevel is null && manifest.ProfileFingerprint == GameBuildProfile.English21.Fingerprint : r.Artwork is null,
                "Invalid full-art provenance.");
            Require(r.Conversion == CpjArtworkDecoder.NativeConversionId ? CpjArtworkReferenceProfiles.Matches(r) : r.Artwork?.ReferenceEvidenceId is null,
                "Native full-art reference differs from the registered exact source/output case.");
            string relative = "blobs/" + r.ContentSha256 + ".bin";
            allowed.Add(relative);
            CheckBlob(Path.Combine(directory, "blobs", r.ContentSha256 + ".bin"), r);
        }
        Require(Directory.EnumerateDirectories(directory).Select(Path.GetFileName).SequenceEqual(["blobs"]), "Unexpected package directory.");
        CheckAncestors(Path.Combine(directory, "blobs"));
        Require(!Directory.EnumerateDirectories(Path.Combine(directory, "blobs")).Any(), "Nested blob directory is invalid.");
        var files = Directory.EnumerateFiles(directory).Select(Path.GetFileName)
            .Concat(Directory.EnumerateFiles(Path.Combine(directory, "blobs")).Select(p => "blobs/" + Path.GetFileName(p)));
        Require(allowed.SetEquals(files!), "Unexpected or missing package files.");
        return new(root, expectedPackageId, manifest);
    }

    public byte[] ReadResource(string assetKey)
    {
        if (!resources.TryGetValue(assetKey, out var entry)) throw new KeyNotFoundException(assetKey);
        string path = Path.Combine(DirectoryPath, "blobs", entry.ContentSha256 + ".bin");
        CheckAncestors(path);
        byte[] data = ReadFile(path, MaximumResourceBytes);
        Require(data.LongLength == entry.ContentLength && Hash(data) == entry.ContentSha256, "Resource changed after package validation.");
        return data; // Caller owns a copy, never an internal mutable buffer.
    }

    /// <summary>Deterministic local integrity receipt; not a digital signature.</summary>
    public string SignOffIntegrity(GameBuildProfile profile)
    {
        _ = Open(privateRoot, PackageId, profile, Scope, resources.Keys); // Recheck every required file.
        var signoff = new Signoff("integrity-only;psp-fidelity-unverified;pixel-parity-unverified", PackageId, Scope,
            manifest.SourceSha256, manifest.ProfileFingerprint, manifest.DecoderSha256, resources.Count);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(signoff, Json);
        string directory = Path.Combine(privateRoot, "signoffs"); CheckAncestors(directory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, PackageId + ".integrity.json"); CheckAncestors(path);
        if (File.Exists(path)) Require(ReadFile(path, MaximumManifestBytes).AsSpan().SequenceEqual(bytes), "Existing integrity receipt differs; not overwritten.");
        else WriteNew(path, bytes);
        return path;
    }

    private static void CheckBlob(string path, PresentationResource entry)
    {
        CheckAncestors(path);
        using var stream = File.OpenRead(path);
        Require(stream.Length == entry.ContentLength && Convert.ToHexStringLower(SHA256.HashData(stream)) == entry.ContentSha256,
            "Missing, truncated or corrupted package blob.");
    }
    private static string PrivateRoot(string path)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        CheckAncestors(root);
        return root;
    }
    private static void CheckAncestors(string path)
    {
        string? at = Path.GetFullPath(path);
        while (at is not null)
        {
            if (File.Exists(at) || Directory.Exists(at))
                Require((File.GetAttributes(at) & FileAttributes.ReparsePoint) == 0, "Linked package paths are unsupported.");
            Require(!File.Exists(Path.Combine(at, ".git")) && !Directory.Exists(Path.Combine(at, ".git")),
                "Original presentation resources must be outside Git worktrees.");
            at = Path.GetDirectoryName(at);
        }
    }
    private static byte[] ReadRange(Stream stream, long at, int size)
    {
        Require(at >= 0 && size >= 0 && size <= MaximumResourceBytes && at <= stream.Length && size <= stream.Length - at,
            "Source range exceeds bounds.");
        stream.Position = at; byte[] bytes = new byte[size]; stream.ReadExactly(bytes); return bytes;
    }
    private static byte[] ReadFile(string path, int maximum)
    {
        using var stream = File.OpenRead(path);
        Require(stream.Length <= maximum, "Package file exceeds limit.");
        return ReadRange(stream, 0, checked((int)stream.Length));
    }
    private static string DecoderHash()
    {
        string path = typeof(GimTextureDecoder).Assembly.Location;
        Require(!string.IsNullOrEmpty(path), "Decoder assembly identity unavailable in this deployment.");
        using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static void WriteNew(string path, byte[] bytes)
    { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write); stream.Write(bytes); stream.Flush(flushToDisk: true); }
    private static bool HashText(string? hash) => GameBuildProfile.ValidHash(hash) && hash == hash!.ToLowerInvariant();
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static bool Key(string? value) => GameBuildProfile.SafePath(value) && value!.Length <= 160 &&
        value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '/' or '-' or '_' or '.');
    private static bool Member(string value) => GameBuildProfile.SafePath(value) && !value.Contains('/');
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
