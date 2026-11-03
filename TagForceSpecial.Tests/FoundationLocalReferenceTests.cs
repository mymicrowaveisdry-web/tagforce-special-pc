using System.Text.Json;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.Decks;
using TagForceSpecial.PC.Import;

namespace TagForceSpecial.Tests;

public sealed class FoundationLocalReferenceTests
{
    [Fact]
    [Trait("Category", "LocalReference")]
    public void UserIsoAndPinnedCacheAdmitOnlyKnownLegalSelectedDecks()
    {
        string iso = Environment.GetEnvironmentVariable("TAGFORCE_ISO") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PPSSPP/PSP/GAME/Yu-Gi-Oh! ARC-V Tag Force Special (English v2.1).iso");
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        string package = Path.Combine(root, "TagForceSpecial.Cards/Data/Local/tfsp/6cd21aa6ca4fb82289f329400c9645da0532bf5a5092a604bef4c3e1d82d9060/v1");
        var validated = GameSourceValidator.Open(iso);
        Assert.True(validated.IsValid, JsonSerializer.Serialize(validated.Diagnostics));
        using var source = validated.Source!;
        Assert.Equal(InitialDeckPolicy.SourceBuild, source.Profile.ImageSha256);
        string cachePath = Path.Combine(Path.GetTempPath(), "tagforce-original-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = GameCache.Create(source, package, cachePath);
            Assert.True(cache.IsValid, JsonSerializer.Serialize(cache.Diagnostics));
            Assert.True(source.IsUnchanged());
            Assert.Equal(7254, cache.Cache!.Catalog.Catalog.Count);
            var policy = InitialDeckPolicy.Create();
            var first = InitialDeckPolicy.Example(); var second = InitialDeckPolicy.Example("local-opponent");
            var firstCollection = InitialDeckPolicy.ExampleCollection(); var secondCollection = InitialDeckPolicy.ExampleCollection("local-opponent");
            Assert.True(SelectedDecks.Validate(cache.Cache, policy, first, firstCollection, second, secondCollection).IsValid);
            var illegal = new DeckDefinition("over-copy", "1", first.OwnerId, first.CatalogId, first.CatalogRevision,
                first.SourceBuild, first.PolicyId, first.PolicyRevision, first.Main, side: [first.Main[0]]);
            var invalid = SelectedDecks.Validate(cache.Cache, policy, first, firstCollection, illegal, firstCollection);
            Assert.False(invalid.IsValid); Assert.Equal(DeckLegality.Illegal, invalid.Second.Legality);
            var unknown = new DeckDefinition("unknown-regulation", "1", first.OwnerId, first.CatalogId, first.CatalogRevision,
                first.SourceBuild, first.PolicyId, "unreviewed", first.Main);
            Assert.Equal(DeckLegality.Indeterminate, DeckValidator.Validate(unknown, cache.Cache.Catalog.Catalog, policy, firstCollection).Legality);
            Assert.True(GameCache.Validate(cachePath).IsValid);
        }
        finally { if (Directory.Exists(cachePath)) Directory.Delete(cachePath, recursive: true); }
    }
}
