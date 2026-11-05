using System.Security.Cryptography;
using System.Text;

namespace TagForceSpecial.Assets.GameSource;

public sealed record SourceFileIdentity(string Path, long Length, string Sha256, string? CachePath = null);
public sealed record CacheFileIdentity(string Path, long Length, string Sha256);
public sealed record SourceDiagnostic(string Code, string Path, string Detail);

/// <summary>Trusted application configuration, never populated from an untrusted cache manifest.</summary>
public sealed class GameBuildProfile
{
    public string BuildId { get; }
    public long ImageLength { get; }
    public string ImageSha256 { get; }
    public int CacheSchema { get; }
    public string MappingVersion { get; }
    public string ConversionVersion { get; }
    public string CatalogSourceSha256 { get; }
    public IReadOnlyList<SourceFileIdentity> SourceFiles { get; }
    public IReadOnlyList<CacheFileIdentity> CacheFiles { get; }
    public string Fingerprint { get; }

    public GameBuildProfile(string buildId, long imageLength, string imageSha256, int cacheSchema,
        string mappingVersion, string conversionVersion, string catalogSourceSha256,
        IEnumerable<SourceFileIdentity> sourceFiles, IEnumerable<CacheFileIdentity> convertedFiles)
    {
        if (new[] { buildId, mappingVersion, conversionVersion }.Any(string.IsNullOrWhiteSpace) ||
            imageLength <= 0 || cacheSchema <= 0 || !ValidHash(imageSha256) || !ValidHash(catalogSourceSha256))
            throw new ArgumentException("Explicit build, schema, mapping, conversion and SHA-256 identities are required.");
        BuildId = buildId; ImageLength = imageLength; ImageSha256 = imageSha256.ToLowerInvariant(); CacheSchema = cacheSchema;
        MappingVersion = mappingVersion; ConversionVersion = conversionVersion; CatalogSourceSha256 = catalogSourceSha256.ToLowerInvariant();
        var sources = sourceFiles.Select(f => f with { Sha256 = f.Sha256.ToLowerInvariant() }).OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        if (sources.Length == 0 || sources.Any(f => !SafePath(f.Path) || f.Length <= 0 || !ValidHash(f.Sha256) ||
                f.CachePath is not null && !SafePath(f.CachePath)) || sources.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Length)
            throw new ArgumentException("Source paths, lengths and hashes must be explicit and unique.");
        var outputs = sources.Where(f => f.CachePath is not null).Select(f => new CacheFileIdentity(f.CachePath!, f.Length, f.Sha256))
            .Concat(convertedFiles).Select(f => f with { Sha256 = f.Sha256.ToLowerInvariant() }).OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        if (outputs.Any(f => !SafePath(f.Path) || f.Path == "cache.json" || f.Length <= 0 || !ValidHash(f.Sha256)) ||
            outputs.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != outputs.Length ||
            !outputs.Any(f => f.Path == "catalog/cards.json") || !outputs.Any(f => f.Path == "catalog/manifest.json"))
            throw new ArgumentException("Cache mapping requires unique safe paths and the converted catalog files.");
        SourceFiles = Array.AsReadOnly(sources); CacheFiles = Array.AsReadOnly(outputs);
        Fingerprint = Hash(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
            { BuildId, ImageLength, ImageSha256, CacheSchema, MappingVersion, ConversionVersion, CatalogSourceSha256, SourceFiles, CacheFiles })));
    }

    internal static bool ValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    internal static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
    internal static bool SafePath(string? path) => !string.IsNullOrWhiteSpace(path) && !path.Contains('\\') &&
        !path.Contains(':') && !path.StartsWith('/') && path.Split('/').All(s => s.Length > 0 && s is not "." and not ".." && s.All(c => c is >= ' ' and <= '~'));

    public static GameBuildProfile English21 { get; } = new("tfsp-npjh00142-english-2.1-e99cff", 767053824,
        "e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707", 1,
        "tfsp-runtime-inputs-v1", "cards-json-v1-initial14-v1;cardh-stored-v1",
        "6cd21aa6ca4fb82289f329400c9645da0532bf5a5092a604bef4c3e1d82d9060",
        [
            new("PSP_GAME/PARAM.SFO", 560, "5b61d1c79cd5f67e9342d19d177b4dc012b0fe840ac21d1a62769845b1a56743"),
            new("PSP_GAME/SYSDIR/EBOOT.BIN", 2059817, "8087681f81f87a87471591752b3c0c9bc3cec48ffff15a115a1e2525f670c975"),
            new("PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp", 1217472, "6cd21aa6ca4fb82289f329400c9645da0532bf5a5092a604bef4c3e1d82d9060", "source/cardinfo.ehp"),
            new("PSP_GAME/USRDIR/card/cardh_j.cip", 29779968, "6ef297b38ff61e76f20849e1fedfe8df8f6a10494644aee4398f926d2550bd97", "assets/cardh_j.cip"),
            new("PSP_GAME/USRDIR/gmodule/rel_DuelEng.prx", 6851733, "ee0e50c12d78e28ebb9f91625e556d131a84a1729e06359ca5c045bf911085c0"),
            new("UMD_DATA.BIN", 48, "016ad9a4346d48d5b98186b1c7443da4f1714ee802659a845b7a792091c30913")
        ],
        [
            new("catalog/cards.json", 7710633, "62e330572f0cad77ae43ddc335554b1a708e9d48323bbe45b229f37115a99686"),
            new("catalog/manifest.json", 536, "fbeb88926b59ef1a3179019a9937dd8b9945424bbf1dae17f99312df9c6b07d8")
        ]);
}
