using System.Security.Cryptography;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Import;

public sealed record PresentationCacheBinding(int SchemaVersion, string ConverterId, string BuildId,
    string ProfileFingerprint, string Generation, string PackageId, string Scope,
    string[] ResourceKeys, string MappingSha256);

/// <summary>Presentation generations alongside the existing gameplay cache; never activates gameplay cards.</summary>
public sealed class PresentationCacheStore(PrivateStorage storage, IEnumerable<GameBuildProfile>? trustedProfiles = null)
{
    // Known sources are trusted code profiles, never reconstructed from a cache manifest.
    private readonly IReadOnlyDictionary<string, GameBuildProfile> profiles =
        (trustedProfiles ?? [GameBuildProfile.English21]).ToDictionary(p => p.Fingerprint, StringComparer.Ordinal);

    public SemanticAssetRegistry Publish(ValidatedGameSource source, string scope,
        IEnumerable<PresentationResourceRequest> requests,
        Func<PresentationPackage, IEnumerable<AssetManifestEntry>> map, string? jpegDecoderExecutable = null, string? nativeReferenceRoot = null)
    {
        if (!profiles.TryGetValue(source.Profile.Fingerprint, out var trusted) ||
            source.Profile.BuildId != trusted.BuildId)
            throw new InvalidDataException("Unknown presentation source profile; register an independent known-source profile first.");
        using var lease = new FileStream(storage.PathFor("presentation-import.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        string generation = Guid.NewGuid().ToString("N");
        string prefix = "presentation/generations/" + generation;
        string root = storage.PathFor(prefix + "/package-cache");
        var package = PresentationPackage.Create(source, root, scope, requests, jpegDecoderExecutable, nativeReferenceRoot);
        byte[] mapping = SemanticAssetRegistry.Encode(map(package));
        string digest = Convert.ToHexStringLower(SHA256.HashData(mapping));
        string[] keys = package.Resources.Keys.Order(StringComparer.Ordinal).ToArray();
        var registry = SemanticAssetRegistry.Open(root, package.PackageId, trusted, scope, keys, mapping, digest);
        string path = storage.PathFor(prefix + "/mapping.json");
        // This generation is new and unreferenced. Never overwrite a published mapping.
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { output.Write(mapping); output.Flush(true); }
        if (!source.IsUnchanged()) throw new InvalidDataException("Presentation source changed before activation.");
        var binding = new PresentationCacheBinding(2, PresentationPackage.ConverterId, trusted.BuildId,
            trusted.Fingerprint, generation, package.PackageId, scope, keys, digest);
        // Only the fully validated generation becomes active. Old generations remain intact.
        storage.Write("presentation/active.json", binding);
        return registry;
    }

    public SemanticAssetRegistry? OpenActive()
    {
        if (!File.Exists(storage.PathFor("presentation/active.json"))) return null;
        var binding = storage.Read<PresentationCacheBinding>("presentation/active.json");
        if (binding.SchemaVersion != 2 || binding.ConverterId != PresentationPackage.ConverterId ||
            !Guid.TryParseExact(binding.Generation, "N", out _) || binding.ProfileFingerprint is null ||
            !profiles.TryGetValue(binding.ProfileFingerprint, out var profile) || binding.BuildId != profile.BuildId ||
            binding.ResourceKeys is null)
            throw new InvalidDataException("Presentation cache version/profile mismatch. Rebuild from the verified ISO into a new generation.");
        string prefix = "presentation/generations/" + binding.Generation;
        string mapping = storage.PathFor(prefix + "/mapping.json");
        using var stream = File.OpenRead(mapping);
        if (stream.Length > SemanticAssetRegistry.MaximumManifestBytes) throw new InvalidDataException("Mapping exceeds limit.");
        byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        return SemanticAssetRegistry.Open(storage.PathFor(prefix + "/package-cache"), binding.PackageId,
            profile, binding.Scope, binding.ResourceKeys, bytes, binding.MappingSha256);
    }

    public string SignOffActiveIntegrity()
    {
        using var lease = new FileStream(storage.PathFor("presentation-import.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        _ = OpenActive() ?? throw new InvalidDataException("No presentation generation is active.");
        var binding = storage.Read<PresentationCacheBinding>("presentation/active.json");
        var profile = profiles[binding.ProfileFingerprint];
        var package = PresentationPackage.Open(storage.PathFor("presentation/generations/" + binding.Generation + "/package-cache"),
            binding.PackageId, profile, binding.Scope, binding.ResourceKeys);
        return package.SignOffIntegrity(profile);
    }
}
