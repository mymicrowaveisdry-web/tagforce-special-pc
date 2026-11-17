using System.Security.Cryptography;
using System.Text;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Import.Stages;

/// <summary>Decodes the game's presentation plan into a content-addressed package plus its semantic mapping.</summary>
public static class PresentationStage
{
    public const string Id = "presentation";
    public const string Version = "presentation-stage-v1";
    private const string MappingFile = "mapping.json", BindingFile = "binding.json";

    public static (StageResult Result, PresentationBinding? Binding) Run(StageCache cache, ValidatedGameSource source, string decoderSha256,
        bool reuse, CancellationToken cancellation)
    {
        var requests = GamePresentationPlan.Requests().ToArray();
        string planHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            requests.Select(r => $"{r.AssetKey}|{r.SourcePath}|{r.MemberName}|{r.Conversion}|{r.PaletteLevel}|{r.Frame}|{r.CardId}").Order(StringComparer.Ordinal)))));
        string key = StageCache.Key(Id, Version, PresentationPackage.ConverterId, GamePresentationPlan.Scope, decoderSha256,
            source.Profile.Fingerprint, planHash);
        if (reuse && cache.TryReuse(Id, key, dir => TryOpen(dir, source.Profile) is not null))
        {
            var reused = Open(cache.Directory(Id, key), source.Profile);
            return (Result(StageStatus.Reused, "Presentation package reused from a verified earlier import.", cache, key, decoderSha256, reused), reused.Binding);
        }
        cancellation.ThrowIfCancellationRequested();
        string directory = cache.Publish(Id, key, staging =>
        {
            var package = PresentationPackage.Create(source, staging, GamePresentationPlan.Scope, requests);
            byte[] mapping = SemanticAssetRegistry.Encode(GamePresentationPlan.Map(package, source.Profile.BuildId));
            var binding = new PresentationBinding(package.PackageId, GamePresentationPlan.Scope,
                package.Resources.Keys.Order(StringComparer.Ordinal).ToArray(), Convert.ToHexStringLower(SHA256.HashData(mapping)));
            File.WriteAllBytes(Path.Combine(staging, MappingFile), mapping);
            File.WriteAllBytes(Path.Combine(staging, BindingFile), ManifestJson.Serialize(binding));
            // Opening re-reads every blob before the output can be published.
            _ = SemanticAssetRegistry.Open(staging, binding.PackageId, source.Profile, binding.Scope, binding.ResourceKeys, mapping, binding.MappingSha256);
        }, cancellation);
        var opened = Open(directory, source.Profile);
        return (Result(StageStatus.Succeeded, $"Presentation package decoded: {opened.Registry.Entries.Count} resources.", cache, key, decoderSha256, opened),
            opened.Binding);
    }

    public sealed record Opened(SemanticAssetRegistry Registry, PresentationBinding Binding);

    /// <summary>Opens and fully re-validates a stage directory; the binding file is checked against the package it names.</summary>
    public static Opened Open(string directory, GameBuildProfile profile)
    {
        var binding = ManifestJson.Parse<PresentationBinding>(ReadBounded(Path.Combine(directory, BindingFile), 1024 * 1024));
        byte[] mapping = ReadBounded(Path.Combine(directory, MappingFile), SemanticAssetRegistry.MaximumManifestBytes);
        return new(SemanticAssetRegistry.Open(directory, binding.PackageId, profile, binding.Scope, binding.ResourceKeys, mapping, binding.MappingSha256), binding);
    }

    public static Opened? TryOpen(string directory, GameBuildProfile profile)
    {
        try { return Open(directory, profile); }
        catch (Exception e) when (PrivateStorage.Recoverable(e) || e is KeyNotFoundException) { return null; }
    }

    private static StageResult Result(StageStatus status, string message, StageCache cache, string key, string decoder, Opened opened)
    {
        var verified = GamePresentationPlan.Required.Count(c => opened.Registry.VerifiedReferenceScope(c.AssetKey) is not null);
        var checks = new List<ValidationCheck>
        {
            new("presentation.package-integrity", CheckCategory.Asset, CheckSeverity.Info, CheckStatus.Passed, "presentation",
                "Every package blob was re-read and matched its recorded SHA-256 and decoded size.",
                $"{opened.Registry.Entries.Count} semantic entries, package {opened.Binding.PackageId[..12]}"),
            new("presentation.reference-pixels", CheckCategory.Asset, CheckSeverity.Info,
                verified == GamePresentationPlan.Required.Count ? CheckStatus.Passed : CheckStatus.Warning, "presentation",
                "Pinned textures whose decoded pixels equal a recorded original-runtime reference.",
                $"{verified} of {GamePresentationPlan.Required.Count} pinned textures matched; thumbnails and stored files have no pixel reference.",
                verified == GamePresentationPlan.Required.Count ? null : "Check the decoder version against the pinned reference cases."),
            new("presentation.layout", CheckCategory.Asset, CheckSeverity.Info, CheckStatus.NotSupported, "presentation",
                "Original screen layout is not verified by the importer; only texture bytes are.")
        };
        var output = new StageOutput(Id, cache.RelativeDirectory(Id, key), key, PresentationPackage.ConverterId + ";assets-" + decoder[..12],
            StageCache.Inventory(cache.Directory(Id, key)));
        return new(status, message, output, checks, opened.Registry.Entries.Count, opened.Registry.Entries.Count);
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        PrivateStorage.Check(path);
        if (new FileInfo(path).Length > maximum) throw new InvalidDataException("Presentation stage file exceeds its limit.");
        return File.ReadAllBytes(path);
    }
}
