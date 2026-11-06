using TagForceSpecial.Assets.GameSource;

namespace TagForceSpecial.Assets.Presentation;

public sealed record PresentationReferenceCase(string Screen, string AssetKey, string EvidenceId,
    string SourcePath, string? Member, long Offset, long Length, string SourceSha256,
    string InputSha256, string PixelSha256, int Width, int Height, bool NativeCard = false, int? Frame = null, int PaletteLevel = 0);

/// <summary>Reviewed text-only pins derived from matching original-game scenarios.
/// Scope is complete texture pixels, not screen placement, transitions or gameplay.</summary>
public static class PresentationReferenceCases
{
    private const string Archive = "PSP_GAME/USRDIR/title_menu/title_menu_j.ehp";
    private const string ArchiveHash = "65b75fc41086e181aaee5ebcea3cc332098123230739f443884f4ce76d7c0e47";
    public const string Scope = "native-texture-pixels-only;layout-and-timing-unverified";
    public static IReadOnlyList<PresentationReferenceCase> Cases { get; } = Array.AsReadOnly(new[]
    {
        new PresentationReferenceCase("title", "title.source.press-start0002", "phase2.title.press-start0002.native-v1",
            Archive, "press_start0002.gim", 3963344, 3280, ArchiveHash,
            "94b97c1e614223771eca0688348bd2016dc70eace869466c2fb958308e029318",
            "a35179e5e89342a6171d9b4773d7329d0fc21c0f04b14185fe860120ecc12d8a", 256, 16),
        new("main-menu", "menu.main.source.panel01", "phase2.menu.panel01.native-v1",
            Archive, "m_menu_panel01.gim", 1336400, 17616, ArchiveHash,
            "44de61250cdd5721a1cb084cad1e2e1560fffee997786bc8bfd90769b2058fc5",
            "7a1d7227fa3e23a37efc9a50e6bb196275bebb51e8ac292e3bf68f05ef573643", 128, 128),
        new("main-menu", "menu.main.source.title01", "phase2.menu.title01.native-v1",
            Archive, "m_menu_title01.gim", 1354016, 17616, ArchiveHash,
            "613c3a8a492f79ac7ce5f80d6e0cea44365930215b51deb0da09e50b9d75ef9e",
            "401ec4a298f86cdba9d7a50ac7691d96b88bb1a1ece97a1a045afbdc59b99d61", 256, 128),
        new("main-menu", "menu.main.source.bar00001", "phase2.menu.bar00001.native-v1",
            Archive, "m_menu_bar00001.gim", 1332672, 784, ArchiveHash,
            "9a47b93fbfb3c7999f08c90e0c5243969ec21119c4a7ca9f7787e94b6446323d",
            "ffcb902f6624d7a5445dc341bd3e824601c1cd3c9e01f0bb9fb37b45dec5c7e5", 32, 32),
        new("main-menu", "menu.main.source.bar00003", "phase2.menu.bar00003.native-v1",
            Archive, "m_menu_bar00003.gim", 1333456, 784, ArchiveHash,
            "4efdc9477d309f964a8adcd3b3d6ea76b0f1aabc64274de16d91604963c4e217",
            "b3e770331b933943f13ff5703584ae2ee42f56de2bf5681f178735d48452f0b7", 32, 32),
        new("deck-editor", "deck.card-art.full.9863", CpjArtworkReferenceProfiles.EvidenceId,
            "PSP_GAME/USRDIR/card/cardj256_j.cip", null, 204126208, 28672,
            CpjArtworkReferenceProfiles.ArchiveSha256, CpjArtworkReferenceProfiles.JpegSha256,
            CpjArtworkReferenceProfiles.RgbaSha256, 256, 256, true)
    }.Concat(TitleReferenceResources.Cases).Concat(TitleFlowReferenceResources.Cases).Concat(OptionsReferenceResources.Cases)
        .Concat(DeckEditReferenceResources.Cases).ToArray());

    internal static string? Match(AssetManifestEntry entry, PresentationResource resource, string profileFingerprint)
    {
        if (profileFingerprint != GameBuildProfile.English21.Fingerprint) return null;
        var reference = Cases.SingleOrDefault(c => c.AssetKey == entry.AssetKey && entry.EvidenceIds.Contains(c.EvidenceId));
        if (reference is null || resource.SourcePath != reference.SourcePath || resource.MemberName != reference.Member ||
            resource.Offset != reference.Offset || resource.StoredLength != reference.Length ||
            resource.SourceSha256 != reference.SourceSha256 || resource.InputSha256 != reference.InputSha256 ||
            resource.ContentSha256 != reference.PixelSha256 || resource.Width != reference.Width || resource.Height != reference.Height)
            return null;
        if (reference.NativeCard)
            return resource.Conversion == CpjArtworkDecoder.NativeConversionId && CpjArtworkReferenceProfiles.Matches(resource) ? Scope : null;
        // A multi-frame pin names its frame; single-frame pins name their palette level (0 unless the member has several).
        bool selection = reference.Frame is { } frame ? resource.Frame == frame && resource.PaletteLevel is null
            : resource.Frame is null && resource.PaletteLevel == reference.PaletteLevel;
        return resource.Conversion == "gim-rgba-v1" && resource.StoredSha256 == reference.InputSha256 &&
            selection && resource.Artwork is null ? Scope : null;
    }
}
