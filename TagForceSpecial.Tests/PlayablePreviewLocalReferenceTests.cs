using System.Text.Json;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.Effects;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

public sealed class PlayablePreviewLocalReferenceTests
{
    [Fact]
    [Trait("Category", "LocalReference")]
    public void FreshOriginalCatalogBindsPreviewAndExportsHonestFullManifest()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var source = Path.Combine(root, "data/extracted/e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707/files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp");
        using var stream = File.OpenRead(source);
        var catalog = CardCatalogImporter.Read(stream, 3459, 11657);
        Assert.Equal(PlayablePreview.CardInfoSha256, catalog.Source.ContainerSha256);
        Assert.Equal(7254, catalog.Count);
        PlayablePreview.ValidateCatalog(catalog);
        using var manifest = JsonDocument.Parse(JsonSerializer.Serialize(CardSupportInventory.Build(ReviewedCardMetadata.ApplyReviewed(catalog), 7255),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var full = manifest.RootElement.GetProperty("pspFidelity10");
        var entries = full.GetProperty("cards").EnumerateArray().ToArray();
        // Denominator: 7,254 IDs minus 95 effect-generated Tokens; unobtainable cards stay included.
        Assert.Equal(95, full.GetProperty("excludedTokens").GetArrayLength());
        Assert.Equal(7159, entries.Length);
        // 665 Normal Monsters (including the 8 preview monsters), 6 preview Spells, 5 chain cards, 13 life-point cards
        // 10 Deck-operation cards, 15 removal cards and 8 Graveyard cards.
        Assert.Equal(731, entries.Count(c => c.GetProperty("status").GetString() == "supported"));
        Assert.Equal(0, entries.Count(c => c.GetProperty("status").GetString() == "partial"));
        Assert.Equal(6428, entries.Count(c => c.GetProperty("status").GetString() == "unsupported"));
        Assert.All(entries, c => Assert.False(c.GetProperty("pspVerified").GetBoolean()));
        var scenario = PreviewDuelScenario.Run(catalog);
        Assert.Equal(TagForceSpecial.Core.DuelEndReason.LifePointsDepleted, scenario.Outcome.Reason);

        string packagePath = Path.Combine(Path.GetTempPath(), "tagforce-original-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            CardCatalogPackage.Write(packagePath, catalog, new("tfsp-original", "1", "en", "local-original-import"));
            var loaded = CardCatalogPackage.Load(packagePath).Catalog;
            // Compare all fields, including source rows, descriptions, codes and immutable effect-reference snapshots.
            Assert.Equal(JsonSerializer.Serialize(catalog), JsonSerializer.Serialize(loaded));
            // Packages carry the frozen conversion baseline; the reviewed overlay is applied when opened.
            Assert.Equal(7240, loaded.Cards.Count(c => c.Effects!.State == Cards.CardEffectState.Unknown));
            Assert.Equal(8, loaded.Cards.Count(c => c.Effects!.State == Cards.CardEffectState.None));
            Assert.Equal(665, ReviewedCardMetadata.Overlay(loaded).Cards.Count(c => c.Effects!.State == Cards.CardEffectState.None));
            Assert.Equal(6, loaded.Cards.Count(c => c.Effects!.State == Cards.CardEffectState.Referenced));
            Assert.Equal(JsonSerializer.Serialize(scenario), JsonSerializer.Serialize(PreviewDuelScenario.Run(loaded)));
        }
        finally
        {
            if (Directory.Exists(packagePath)) Directory.Delete(packagePath, recursive: true);
        }
    }
}
