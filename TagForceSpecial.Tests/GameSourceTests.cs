using System.Text.Json;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.PC.Decks;

namespace TagForceSpecial.Tests;

public sealed class GameSourceTests
{
    [Fact]
    public void ExactSourceAndRequiredFilesValidateReadOnly()
    {
        using var f = new FoundationFixture(); var profile = f.Profile();
        byte[] before = File.ReadAllBytes(f.Iso);
        var result = GameSourceValidator.Open(f.Iso, profile);
        Assert.True(result.IsValid, JsonSerializer.Serialize(result.Diagnostics));
        using (var source = result.Source!)
        {
            if (OperatingSystem.IsWindows()) Assert.Throws<IOException>(() => File.Open(f.Iso, FileMode.Open, FileAccess.Write).Dispose());
            Assert.True(source.IsUnchanged());
        }
        Assert.Equal(before, File.ReadAllBytes(f.Iso));
    }

    [Theory]
    [InlineData("altered", "SourceHashMismatch")]
    [InlineData("truncated", "SourceLengthMismatch")]
    [InlineData("missing", "SourceUnavailable")]
    [InlineData("different-build", "SourceHashMismatch")]
    public void WrongOrIncompleteImagesAreRejectedBeforeUse(string failure, string code)
    {
        using var f = new FoundationFixture(); var profile = f.Profile();
        if (failure is "altered" or "different-build") f.EditImage(bytes => bytes[^1] ^= 1);
        if (failure == "truncated") { using var file = File.OpenWrite(f.Iso); file.SetLength(100); }
        if (failure == "missing") File.Delete(f.Iso);
        var result = GameSourceValidator.Open(f.Iso, profile);
        Assert.False(result.IsValid); Assert.Null(result.Source);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
        Assert.Equal(JsonSerializer.Serialize(result.Diagnostics), JsonSerializer.Serialize(GameSourceValidator.Open(f.Iso, profile).Diagnostics));
    }

    [Theory]
    [InlineData("missing-file", "MissingSourceFile")]
    [InlineData("changed-file", "SourceFileHashMismatch")]
    [InlineData("wrong-file-length", "SourceFileLengthMismatch")]
    [InlineData("bad-endian", "InvalidSourceStructure")]
    [InlineData("duplicate-name", "InvalidSourceStructure")]
    [InlineData("invalid-descriptor", "InvalidSourceStructure")]
    public void RequiredFilesAndIsoMappingAreCheckedEvenWithMatchingImageHash(string failure, string code)
    {
        using var f = new FoundationFixture();
        if (failure == "missing-file") f.EditImage(b => Array.Clear(b, 22 * 2048, 2048));
        if (failure == "changed-file") f.EditImage(b => b[23 * 2048] ^= 1);
        if (failure == "bad-endian") f.EditImage(b => b[22 * 2048 + 6] ^= 1);
        if (failure == "invalid-descriptor") f.EditImage(b => b[16 * 2048 + 1] = 0);
        if (failure == "duplicate-name") f.EditImage(b =>
            FoundationFixture.Record("src.bin;1"u8.ToArray(), 23, f.Payload.Length, false).CopyTo(b, 22 * 2048 + b[22 * 2048]));
        var profile = f.Profile(requiredLength: failure == "wrong-file-length" ? f.Payload.Length + 1 : null);
        var result = GameSourceValidator.Open(f.Iso, profile);
        Assert.False(result.IsValid); Assert.Null(result.Source);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void PinnedCatalogDirectoriesOpenAndChangedBytesAreRejected()
    {
        using var f = new FoundationFixture(); var profile = f.Profile();
        var game = InstalledGame.FromCatalogDirectory(f.Root, profile);
        Assert.Equal(profile.ImageSha256, game.SourceSha256);
        Assert.Equal("Synthetic monster", game.Catalog.Catalog[1].Name);
        string cards = Path.Combine(f.CatalogPath, "cards.json");
        byte[] bytes = File.ReadAllBytes(cards); bytes[^2] ^= 1; File.WriteAllBytes(cards, bytes);
        Assert.Throws<InvalidDataException>(() => InstalledGame.FromCatalogDirectory(f.Root, profile));
    }

    [Fact]
    public void BothSelectedDecksAreGatedByTheImportedCatalogRevision()
    {
        using var f = new FoundationFixture(); var profile = f.Profile();
        var game = InstalledGame.FromCatalogDirectory(f.Root, profile);
        var policy = new DeckLegalityPolicy("test", "1", profile.ImageSha256, "synthetic", "1", f.Catalog.Source.ContainerSha256,
            "Synthetic policy", [new(1, DeckPlacements.Main, "one", 3)], minimumMain: 1);
        DeckDefinition Deck(string revision = "1") => new("test", "1", "owner", "synthetic", revision, profile.ImageSha256, "test", "1", [1]);
        var collection = new PlayerCollection("owner", "synthetic", "1", profile.ImageSha256, "1", new Dictionary<uint, int> { [1] = 1 });
        Assert.True(SelectedDecks.Validate(game, policy, Deck(), collection, Deck(), collection).IsValid);
        var invalid = SelectedDecks.Validate(game, policy, Deck(), collection, Deck("2"), collection);
        Assert.False(invalid.IsValid); Assert.Null(invalid.Selection);
        Assert.Contains(invalid.Second.Diagnostics, d => d.Code == "SelectedCacheMismatch");
    }
}
