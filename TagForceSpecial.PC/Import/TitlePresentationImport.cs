using TagForceSpecial.Assets;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Import;

/// <summary>Onboarding normalizes the code-pinned title and title-flow resources plus the two source files holding
/// prompt text and embedded dialog textures; it does not import PSP saves.</summary>
public static class TitlePresentationImport
{
    public static IReadOnlyList<PresentationReferenceCase> Required { get; } = Array.AsReadOnly(
        TitleReferenceResources.Cases.Concat(TitleFlowReferenceResources.Cases).Concat(OptionsReferenceResources.Cases)
            .Concat(PresentationReferenceCases.Cases.Where(c => c.AssetKey == "title.source.press-start0002")).ToArray());
    private static readonly (string Key, string Path)[] StoredSources =
    [
        (TitleFlowReferenceResources.EbootKey, TitleFlowReferenceResources.EbootPath),
        (TitleFlowReferenceResources.TitleMenuKey, TitleFlowReferenceResources.TitleMenuPath)
    ];
    public const string Scope = "phase3-title-flow-v1";

    public static void Ensure(ValidatedGameSource source, PrivateStorage storage)
    {
        if (source.Profile.Fingerprint != GameBuildProfile.English21.Fingerprint || !source.IsUnchanged())
            throw new InvalidDataException("Original title import requires the unchanged registered English v2.1 source.");
        var store = new PresentationCacheStore(storage);
        try
        {
            var existing = store.OpenActive();
            if (existing is not null && Required.All(c => existing.Entries.TryGetValue(c.AssetKey, out var entry) &&
                entry.Output.SourceRectangle?.SequenceEqual(new[] { 0, 0, c.Width, c.Height }) == true &&
                existing.VerifiedReferenceScope(c.AssetKey) is not null) &&
                StoredSources.All(s => existing.Entries.ContainsKey(s.Key))) return;
        }
        catch (Exception e) when (SetupStore.Recoverable(e)) { /* Retain the old generation; rebuild using the live verified source. */ }
        _ = store.Publish(source, Scope, Required.Select(c => new PresentationResourceRequest(
            c.AssetKey, c.SourcePath, c.Member, PresentationConversion.GimRgba, 0, EhpReadMode.InspectStoredRanges))
            .Concat(StoredSources.Select(s => new PresentationResourceRequest(s.Key, s.Path, null, PresentationConversion.Stored))), package =>
            StoredSources.Select(s =>
            {
                var r = package.Resources[s.Key];
                return new AssetManifestEntry(2, source.Profile.BuildId, package.PackageId, s.Key, r.AssetKey,
                    new(r.SourcePath, r.MemberName, r.Offset, r.SourceSha256, r.StoredSha256, r.InputSha256),
                    new("blobs/" + r.ContentSha256 + ".bin", r.ContentSha256, null, null, null, null), "mapped-unverified",
                    Array.AsReadOnly(new[] { "phase3.flow.source-bytes.native-v1" }));
            }).Concat(Required.Select(c =>
            {
                var r = package.Resources[c.AssetKey];
                return new AssetManifestEntry(2, source.Profile.BuildId, package.PackageId, c.AssetKey, r.AssetKey,
                    new(r.SourcePath, r.MemberName, r.Offset, r.SourceSha256, r.StoredSha256, r.InputSha256),
                    new("blobs/" + r.ContentSha256 + ".bin", r.ContentSha256, r.ContentSha256, r.Width, r.Height,
                        Array.AsReadOnly(new[] { 0, 0, c.Width, c.Height })), "mapped-unverified", Array.AsReadOnly(new[] { c.EvidenceId }));
            })));
    }
}
