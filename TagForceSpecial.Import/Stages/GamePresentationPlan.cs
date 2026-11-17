using TagForceSpecial.Assets;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Import.Stages;

/// <summary>The presentation resources the PC game reads: pinned title, title-flow, Options and Deck Edit (deck/all_j.ehp) textures,
/// the two source files holding prompt text and dialog textures, and thumbnails for the confirmed card pool. PSP saves are not imported.</summary>
public static class GamePresentationPlan
{
    public const string Scope = "phase3-title-flow-v1";
    public const string SupplementEvidenceId = "phase62.deck-edit.supplement-unverified-v1";
    /// <summary>Decoded from the disc slot with the measured zero tail; not pixel-verified.</summary>
    public const string ThumbnailEvidenceId = "phase62.cardh.gpu-input-bytes-v1";
    private const string StoredEvidenceId = "phase3.flow.source-bytes.native-v1";

    public static IReadOnlyList<PresentationReferenceCase> Required { get; } = Array.AsReadOnly(
        TitleReferenceResources.Cases.Concat(TitleFlowReferenceResources.Cases).Concat(OptionsReferenceResources.Cases)
            .Concat(DeckEditReferenceResources.Cases)
            .Concat(PresentationReferenceCases.Cases.Where(c => c.AssetKey == "title.source.press-start0002")).ToArray());
    public static IReadOnlyList<(string Key, string Path)> StoredSources { get; } =
    [
        (TitleFlowReferenceResources.EbootKey, TitleFlowReferenceResources.EbootPath),
        (TitleFlowReferenceResources.TitleMenuKey, TitleFlowReferenceResources.TitleMenuPath),
        (DeckEditResources.ArchiveKey, DeckEditReferenceResources.Archive)
    ];
    public static IReadOnlyList<uint> ThumbnailCards => ConfirmedCardPool.CardIds;

    public static IEnumerable<PresentationResourceRequest> Requests() =>
        Required.Select(c => new PresentationResourceRequest(c.AssetKey, c.SourcePath, c.Member, PresentationConversion.GimRgba,
                c.Frame is null ? c.PaletteLevel : null, EhpReadMode.InspectStoredRanges, Frame: c.Frame))
            .Concat(StoredSources.Select(s => new PresentationResourceRequest(s.Key, s.Path, null, PresentationConversion.Stored)))
            .Concat(DeckEditResources.Supplements.Select(s => new PresentationResourceRequest(s.AssetKey, DeckEditReferenceResources.Archive, s.Member,
                PresentationConversion.GimRgba, s.PaletteLevel, EhpReadMode.InspectStoredRanges, Frame: s.Frame)))
            .Concat(ThumbnailCards.Select(id => new PresentationResourceRequest(CardArtworkProvider.ThumbnailKey(id), CardThumbnailDecoder.SourcePath,
                null, PresentationConversion.CardThumbnailRgba, CardId: id)));

    /// <summary>Semantic entries for a created package; every entry stays "mapped-unverified" with its evidence identifier.</summary>
    public static IEnumerable<AssetManifestEntry> Map(PresentationPackage package, string buildId)
    {
        AssetManifestEntry Entry(string key, PresentationResource r, int[]? rectangle, string evidence, bool pixels) =>
            new(2, buildId, package.PackageId, key, r.AssetKey,
                new(r.SourcePath, r.MemberName, r.Offset, r.SourceSha256, r.StoredSha256, r.InputSha256),
                new("blobs/" + r.ContentSha256 + ".bin", r.ContentSha256, pixels ? r.ContentSha256 : null,
                    pixels ? r.Width : null, pixels ? r.Height : null, rectangle is null ? null : Array.AsReadOnly(rectangle)),
                "mapped-unverified", Array.AsReadOnly(new[] { evidence }));

        return StoredSources.Select(s => Entry(s.Key, package.Resources[s.Key], null, StoredEvidenceId, false))
            .Concat(Required.Select(c => Entry(c.AssetKey, package.Resources[c.AssetKey], [0, 0, c.Width, c.Height], c.EvidenceId, true)))
            .Concat(DeckEditResources.Supplements.Select(s =>
            {
                var r = package.Resources[s.AssetKey];
                return Entry(s.AssetKey, r, [0, 0, r.Width!.Value, r.Height!.Value], SupplementEvidenceId, true);
            }))
            .Concat(ThumbnailCards.Select(id =>
            {
                string key = CardArtworkProvider.ThumbnailKey(id);
                return Entry(key, package.Resources[key], [0, 0, CardThumbnailDecoder.Width, CardThumbnailDecoder.Height], ThumbnailEvidenceId, true);
            }));
    }
}
