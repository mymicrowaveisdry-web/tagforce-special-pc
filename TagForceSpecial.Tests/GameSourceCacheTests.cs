using System.Text.Json;
using System.Text.Json.Nodes;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Cards;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.PC.Decks;
using TagForceSpecial.PC.Import;

namespace TagForceSpecial.Tests;

public sealed class GameSourceCacheTests
{
    [Fact]
    public void ExactSourceAndRequiredFilesValidateReadOnlyAndCacheReusesWithoutIso()
    {
        using var f = new FoundationFixture(); var profile = f.Profile();
        byte[] before = File.ReadAllBytes(f.Iso);
        var result = GameSourceValidator.Open(f.Iso, profile);
        Assert.True(result.IsValid, JsonSerializer.Serialize(result.Diagnostics));
        using (var source = result.Source!)
        {
            if (OperatingSystem.IsWindows()) Assert.Throws<IOException>(() => File.Open(f.Iso, FileMode.Open, FileAccess.Write).Dispose());
            var cache = GameCache.Create(source, f.CatalogPath, f.CachePath);
            Assert.True(cache.IsValid, JsonSerializer.Serialize(cache.Diagnostics));
            Assert.True(source.IsUnchanged());
        }
        Assert.Equal(before, File.ReadAllBytes(f.Iso));
        File.Delete(f.Iso);
        var reused = GameCache.Validate(f.CachePath, profile);
        Assert.True(reused.IsValid);
        Assert.Equal(profile.ImageSha256, reused.Cache!.SourceSha256);
        Assert.Equal("Synthetic monster", reused.Cache.Catalog.Catalog[1].Name);
        Assert.Equal(f.Payload, File.ReadAllBytes(Path.Combine(f.CachePath, "source/input.bin")));
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

    [Theory]
    [InlineData("mapping")]
    [InlineData("conversion")]
    [InlineData("schema")]
    public void CacheVersionsAreIndependentAndMustMatchTrustedProfile(string changed)
    {
        using var f = new FoundationFixture();
        using var source = GameSourceValidator.Open(f.Iso, f.Profile()).Source!;
        Assert.True(GameCache.Create(source, f.CatalogPath, f.CachePath).IsValid);
        var other = f.Profile(changed == "mapping" ? "mapping-v2" : "mapping-v1",
            changed == "conversion" ? "conversion-v2" : "conversion-v1", changed == "schema" ? 2 : 1);
        var validation = GameCache.Validate(f.CachePath, other);
        Assert.False(validation.IsValid); Assert.Null(validation.Cache);
        Assert.Contains(validation.Diagnostics, d => d.Code == "CacheVersionMismatch");
    }

    [Theory]
    [InlineData("same-length-corruption", "CacheFileHashMismatch")]
    [InlineData("missing-file", "MissingCacheFile")]
    [InlineData("truncated", "CacheFileLengthMismatch")]
    [InlineData("altered-manifest", "CacheVersionMismatch")]
    [InlineData("self-rehashed-output", "CacheVersionMismatch")]
    [InlineData("unexpected-file", "UnexpectedCacheFile")]
    [InlineData("interrupted", "CacheIncomplete")]
    [InlineData("duplicate-property", "CacheUnreadable")]
    public void AlteredAndIncompleteCachesNeverReturnUsableData(string failure, string code)
    {
        using var f = new FoundationFixture(); var profile = f.Profile();
        using var source = GameSourceValidator.Open(f.Iso, profile).Source!;
        Assert.True(GameCache.Create(source, f.CatalogPath, f.CachePath).IsValid);
        string artifact = Path.Combine(f.CachePath, "source/input.bin"), manifest = Path.Combine(f.CachePath, "cache.json");
        if (failure == "same-length-corruption") { byte[] b = File.ReadAllBytes(artifact); b[0] ^= 1; File.WriteAllBytes(artifact, b); }
        if (failure == "missing-file") File.Delete(artifact);
        if (failure == "truncated") File.WriteAllText(artifact, "x");
        if (failure == "altered-manifest")
        { var json = JsonNode.Parse(File.ReadAllText(manifest))!; json["files"]![0]!["sha256"] = new string('0', 64); File.WriteAllText(manifest, json.ToJsonString()); }
        if (failure == "self-rehashed-output")
        {
            byte[] bytes = File.ReadAllBytes(artifact); bytes[0] ^= 1; File.WriteAllBytes(artifact, bytes);
            var json = JsonNode.Parse(File.ReadAllText(manifest))!;
            var file = json["files"]!.AsArray().Single(n => n!["path"]!.GetValue<string>() == "source/input.bin")!;
            file["sha256"] = FoundationFixture.Hash(bytes); File.WriteAllText(manifest, json.ToJsonString());
        }
        if (failure == "unexpected-file") File.WriteAllText(Path.Combine(f.CachePath, "unexpected.json"), "{}");
        if (failure == "interrupted") File.Delete(manifest);
        if (failure == "duplicate-property") File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"));
        var result = GameCache.Validate(f.CachePath, profile);
        Assert.False(result.IsValid); Assert.Null(result.Cache);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
        Assert.True(source.IsUnchanged());
    }

    [Fact]
    public void CancellationWrongCatalogAndExistingCacheDoNotPublishOrOverwrite()
    {
        using var f = new FoundationFixture(); using var source = GameSourceValidator.Open(f.Iso, f.Profile()).Source!;
        var cancelled = GameCache.Create(source, f.CatalogPath, f.CachePath, new CancellationToken(canceled: true));
        Assert.Equal("ImportCancelled", Assert.Single(cancelled.Diagnostics).Code);
        Assert.False(Directory.Exists(f.CachePath));
        Assert.Empty(Directory.EnumerateDirectories(f.Root, ".import-*"));
        string other = Path.Combine(f.Root, "other-catalog");
        CardCatalogPackage.Write(other, new(new(null, "unrelated"), [new(1, "Other", "", new(0, 0, 1))]), new("other", "1", "en", "other"));
        Assert.Equal("CacheCatalogSourceMismatch", Assert.Single(GameCache.Create(source, other, f.CachePath).Diagnostics).Code);
        Assert.False(Directory.Exists(f.CachePath));
        string altered = Path.Combine(f.Root, "altered-conversion");
        CardCatalogPackage.Write(altered, new(f.Catalog.Source,
            f.Catalog.Cards.Select(c => c with { Description = "Changed conversion with unchanged source identity" })), new("synthetic", "1", "en", "synthetic-source"));
        Assert.False(GameCache.Create(source, altered, f.CachePath).IsValid);
        Assert.False(Directory.Exists(f.CachePath));
        Assert.Empty(Directory.EnumerateDirectories(f.Root, ".import-*"));
        Assert.True(GameCache.Create(source, f.CatalogPath, f.CachePath).IsValid);
        byte[] before = File.ReadAllBytes(Path.Combine(f.CachePath, "cache.json"));
        Assert.Equal("CacheAlreadyExists", Assert.Single(GameCache.Create(source, f.CatalogPath, f.CachePath).Diagnostics).Code);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(f.CachePath, "cache.json")));
    }

    [Fact]
    public void CacheCreationIsDeterministicAndBothSelectedDecksAreGated()
    {
        using var f = new FoundationFixture(); var profile = f.Profile();
        using var source = GameSourceValidator.Open(f.Iso, profile).Source!;
        var cache = GameCache.Create(source, f.CatalogPath, f.CachePath).Cache!;
        string repeat = Path.Combine(f.Root, "repeat-cache");
        Assert.True(GameCache.Create(source, f.CatalogPath, repeat).IsValid);
        Assert.Equal(File.ReadAllBytes(Path.Combine(f.CachePath, "cache.json")), File.ReadAllBytes(Path.Combine(repeat, "cache.json")));
        var policy = new DeckLegalityPolicy("test", "1", profile.ImageSha256, "synthetic", "1", f.Catalog.Source.ContainerSha256,
            "Synthetic policy", [new(1, DeckPlacements.Main, "one", 3)], minimumMain: 1);
        DeckDefinition Deck(string revision = "1") => new("test", "1", "owner", "synthetic", revision, profile.ImageSha256, "test", "1", [1]);
        var collection = new PlayerCollection("owner", "synthetic", "1", profile.ImageSha256, "1", new Dictionary<uint, int> { [1] = 1 });
        Assert.True(SelectedDecks.Validate(cache, policy, Deck(), collection, Deck(), collection).IsValid);
        var invalid = SelectedDecks.Validate(cache, policy, Deck(), collection, Deck("2"), collection);
        Assert.False(invalid.IsValid); Assert.Null(invalid.Selection);
        Assert.Contains(invalid.Second.Diagnostics, d => d.Code == "SelectedCacheMismatch");
    }
}
