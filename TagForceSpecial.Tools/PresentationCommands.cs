using System.Text.Json;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.Import;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.Tools;

public sealed record PlannedPresentationAsset(string AssetKey, PresentationResourceRequest Resource,
    int[]? SourceRectangle, string[] EvidenceIds);
public sealed record PresentationImportPlan(int SchemaVersion, string ConverterId, string BuildId,
    string Scope, PlannedPresentationAsset[] Assets, string? JpegDecoderExecutable = null, string? NativeReferenceRoot = null);

public static class PresentationCommands
{
    public static int ImportTitle(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2) { error.WriteLine("Usage: presentation-title-import <verified.iso> <private-data-root>"); return 2; }
        try
        {
            using var source = GameSourceValidator.Open(args[0]).Source;
            if (source is null) throw new InvalidDataException("Source image mismatch.");
            TitlePresentationImport.Ensure(source, new PrivateStorage(args[1]));
            output.WriteLine("Validated title source generation ready; original save/transfer support remains pending.");
            return 0;
        }
        catch (Exception exception) when (SetupStore.Recoverable(exception) || exception is KeyNotFoundException)
        { error.WriteLine("Title presentation import failed; retained generations were preserved."); return 1; }
    }
    public static int Check(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--signoff")
        { error.WriteLine("Usage: presentation-check <private-data-root> [--signoff]"); return 2; }
        try
        {
            var store = new PresentationCacheStore(new PrivateStorage(args[0]));
            var registry = store.OpenActive() ?? throw new InvalidDataException("No active presentation cache.");
            int verified = registry.Entries.Keys.Count(key => registry.VerifiedReferenceScope(key) is not null);
            if (args.Length == 2) _ = store.SignOffActiveIntegrity();
            output.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 2, entries = registry.Entries.Count,
                verifiedTextureCases = verified, referenceScope = PresentationReferenceCases.Scope,
                integritySignedOff = args.Length == 2, originalScreenLayoutVerified = false }));
            return 0;
        }
        catch (Exception exception) when (SetupStore.Recoverable(exception) || exception is KeyNotFoundException)
        { error.WriteLine("Presentation validation failed. Retained generations were not replaced."); return 1; }
    }

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 3)
        { error.WriteLine("Usage: presentation-import <verified.iso> <private-data-root> <private-plan.json>"); return 2; }
        try
        {
            var planPath = Path.GetFullPath(args[2]);
            // Plans can contain original resource names, so keep them outside Git too.
            for (string? at = Path.GetDirectoryName(planPath); at is not null; at = Path.GetDirectoryName(at))
                if (File.Exists(Path.Combine(at, ".git")) || Directory.Exists(Path.Combine(at, ".git")))
                    throw new InvalidDataException("Presentation plans must remain private outside Git.");
            PrivateStorage.Check(planPath);
            if (new FileInfo(planPath).Length > SemanticAssetRegistry.MaximumManifestBytes)
                throw new InvalidDataException("Presentation plan exceeds limit.");
            var plan = new PrivateStorage(Path.GetDirectoryName(planPath)!).Read<PresentationImportPlan>(
                Path.GetFileName(planPath), SemanticAssetRegistry.MaximumManifestBytes);
            if (plan.SchemaVersion != 2 || plan.ConverterId != PresentationPackage.ConverterId ||
                plan.BuildId != GameBuildProfile.English21.BuildId || plan.Assets is not { Length: > 0 and <= PresentationPackage.MaximumResources } ||
                plan.Assets.Any(a => a is null || a.Resource is null || a.EvidenceIds is null))
                throw new InvalidDataException("Invalid plan schema/converter/source profile.");
            var result = GameSourceValidator.Open(args[0]);
            using var source = result.Source;
            if (source is null) throw new InvalidDataException("The ISO does not match the registered exact source profile.");
            var store = new PresentationCacheStore(new PrivateStorage(args[1]));
            var registry = store.Publish(source, plan.Scope, plan.Assets.Select(a => a.Resource), package =>
                plan.Assets.Select(asset =>
                {
                    var resource = package.Resources[asset.Resource.AssetKey];
                    return new AssetManifestEntry(2, source.Profile.BuildId, package.PackageId, asset.AssetKey,
                        resource.AssetKey, new(resource.SourcePath, resource.MemberName, resource.Offset,
                            resource.SourceSha256, resource.StoredSha256, resource.InputSha256),
                        new("blobs/" + resource.ContentSha256 + ".bin", resource.ContentSha256,
                            resource.Conversion is "gim-rgba-v1" or "cpj-rgba-v1" or "cpj-native-rgba-v1" or CardThumbnailDecoder.ConversionId ? resource.ContentSha256 : null,
                            resource.Width, resource.Height, asset.SourceRectangle),
                        asset.EvidenceIds.Length > 0 ? "mapped-unverified" : resource.EvidenceStatus,
                        asset.EvidenceIds);
                }), plan.JpegDecoderExecutable, plan.NativeReferenceRoot);
            output.WriteLine($"Presentation generation published: {registry.Entries.Count} semantic entries; PSP verification pending.");
            return 0;
        }
        catch (Exception exception) when (SetupStore.Recoverable(exception) || exception is KeyNotFoundException)
        { error.WriteLine("Presentation import failed; previous generation retained. Check source, plan, version and storage."); return 1; }
    }
}
