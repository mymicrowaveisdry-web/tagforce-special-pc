using System.Text.RegularExpressions;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.Import;

namespace TagForceSpecial.Tests;

/// <summary>Deck Edit textures from deck/all_j.ehp are pinned like the Options texture and imported with the title assets.</summary>
public class DeckEditAssetTests
{
    [Fact]
    public void PinsAreWellFormedAndCoverOneArchive()
    {
        var cases = DeckEditReferenceResources.Cases;
        // 33 single-image members, eight cis_all.gim frames and five extra palette levels (frame.gim 1/3/4/10, panel.gim 1) seen in the dumps.
        Assert.Equal(47, cases.Count);
        Assert.Equal(cases.Count, cases.Select(c => c.AssetKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(cases.Count, cases.Select(c => c.EvidenceId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(cases.Count, cases.Select(c => (c.Member, c.Frame, c.PaletteLevel)).Distinct().Count());
        Assert.Equal(new[] { 1, 3, 4, 10 }, cases.Where(c => c.Member == "frame.gim").Select(c => c.PaletteLevel).Order());
        Assert.All(cases.Where(c => c.Frame is not null), c => Assert.Equal(0, c.PaletteLevel));
        Assert.Equal(new int?[] { 0, 1, 4, 5, 6, 7, 8, 9 }, cases.Where(c => c.Member == "cis_all.gim").Select(c => c.Frame).Order());
        Assert.All(cases.Where(c => c.Member != "cis_all.gim"), c => Assert.Null(c.Frame));
        Assert.All(cases, c =>
        {
            Assert.Equal("deck-editor", c.Screen);
            Assert.Matches(new Regex("^deck\\.all\\.[a-z0-9-]+$"), c.AssetKey);
            Assert.StartsWith("phase62.deck-edit.", c.EvidenceId, StringComparison.Ordinal);
            Assert.Equal(DeckEditReferenceResources.Archive, c.SourcePath);
            Assert.Equal(DeckEditReferenceResources.ArchiveSha256, c.SourceSha256);
            Assert.EndsWith(".gim", c.Member, StringComparison.Ordinal);
            Assert.True(c.Width is > 0 and <= 512 && c.Height is > 0 and <= 512);
            Assert.False(c.NativeCard);
        });
    }

    [Fact]
    public void ImportAndVerificationIncludeEveryPin()
    {
        var required = TitlePresentationImport.Required.Select(c => c.AssetKey).ToHashSet(StringComparer.Ordinal);
        var verifiable = PresentationReferenceCases.Cases.Select(c => c.AssetKey).ToHashSet(StringComparer.Ordinal);
        Assert.All(DeckEditReferenceResources.Cases, c =>
        {
            Assert.Contains(c.AssetKey, required);
            Assert.Contains(c.AssetKey, verifiable);
        });
        // The PC's supported pool gets thumbnails; their keys are the ones CardArtworkProvider reads.
        Assert.Equal(TagForceSpecial.Effects.PlayablePreview.Cards.Select(c => c.CardId).Order(), TitlePresentationImport.ThumbnailCards);
    }
}
