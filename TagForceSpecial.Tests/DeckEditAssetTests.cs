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
        // Single-image members, all ten cis_all.gim frames and the palette levels seen in the dumps, including the preview frame of every
        // card frame the original's Card Frame filter offers (frame.gim 0-5/10/11, frame_p.gim 0/1, frame_xyz.gim).
        Assert.Equal(56, cases.Count);
        Assert.Equal(cases.Count, cases.Select(c => c.AssetKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(cases.Count, cases.Select(c => c.EvidenceId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(cases.Count, cases.Select(c => (c.Member, c.Frame, c.PaletteLevel)).Distinct().Count());
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 10, 11 }, cases.Where(c => c.Member == "frame.gim").Select(c => c.PaletteLevel).Order());
        Assert.Equal(new[] { 0, 1 }, cases.Where(c => c.Member == "frame_p.gim").Select(c => c.PaletteLevel).Order());
        Assert.Single(cases, c => c.Member == "frame_xyz.gim");
        Assert.All(cases.Where(c => c.Frame is not null), c => Assert.Equal(0, c.PaletteLevel));
        Assert.Equal(Enumerable.Range(0, 10).Select(f => (int?)f), cases.Where(c => c.Member == "cis_all.gim").Select(c => c.Frame).Order());
        Assert.All(cases.Where(c => c.Member != "cis_all.gim"), c => Assert.Null(c.Frame));
        Assert.All(cases, c =>
        {
            Assert.Equal("deck-editor", c.Screen);
            Assert.Matches(new Regex("^deck\\.all\\.[a-z0-9-]+(\\.[fp][0-9]{2})?$"), c.AssetKey);
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
        // The PC's confirmed pool gets thumbnails; their keys are the ones CardArtworkProvider reads.
        Assert.Equal(TagForceSpecial.Effects.ConfirmedCardPool.CardIds, TitlePresentationImport.ThumbnailCards);
    }
}
