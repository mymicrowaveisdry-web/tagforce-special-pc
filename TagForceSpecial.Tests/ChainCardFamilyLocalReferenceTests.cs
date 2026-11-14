using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Effects;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

/// <summary>Chain card family against the local cardinfo: metadata, the Psychic list and the manifest.</summary>
public sealed class ChainCardFamilyLocalReferenceTests
{
    private static CardCatalog Original()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        using var stream = File.OpenRead(Path.Combine(root,
            "data/extracted/e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707/files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp"));
        return ReviewedCardMetadata.ApplyReviewed(CardCatalogImporter.Read(stream, 3459, 11657));
    }

    [Fact]
    [Trait("Category", "LocalReference")]
    public void OriginalCatalogMatchesTheReviewedBatchAndPsychicList()
    {
        var catalog = Original();
        ChainCardFamily.ValidateCatalog(catalog);
        // Every main-deck card with type code 21 at Level 1–3; the two type-21 Xyz (getter kind 23) are excluded.
        var derived = catalog.Cards.Where(c => c.Codes!.Type == 21 && c.Codes.GetterKind != 23 && c.Statistics.Level is >= 1 and <= 3)
            .Select(c => c.GameCardId);
        Assert.Equal(ChainCardFamily.LevelThreeOrLowerPsychicIds.Order(), derived.Order());
        Assert.Equal([10172u, 10466u], catalog.Cards.Where(c => c.Codes!.Type == 21 && c.Codes.GetterKind == 23 && c.Statistics.Level <= 3)
            .Select(c => c.GameCardId).Order());
        Assert.All(ChainCardFamily.SheepTokenIds, id => Assert.Contains("\"Scapegoat\"", catalog[id].Description));
        Assert.Contains("Psychic-Type", catalog[ChainCardFamily.EmergencyTeleport].Description);
    }

    [Fact]
    [Trait("Category", "LocalReference")]
    public void ManifestMarksTheFiveCardsSupportedInTheirBatch()
    {
        var catalog = Original();
        using var manifest = JsonDocument.Parse(JsonSerializer.Serialize(CardSupportInventory.Build(catalog, catalog.Count + 1),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var cards = manifest.RootElement.GetProperty("pspFidelity10").GetProperty("cards").EnumerateArray()
            .Where(c => ChainCardFamily.Cards.Any(p => p.CardId == c.GetProperty("gameCardId").GetUInt32())).ToArray();
        Assert.Equal(5, cards.Length);
        Assert.All(cards, c =>
        {
            Assert.Equal("supported", c.GetProperty("status").GetString());
            Assert.Equal(ChainCardFamily.BatchId, c.GetProperty("scope").GetString());
            Assert.Equal(ChainCardFamily.FamilyId, c.GetProperty("behaviorFamily").GetString());
            Assert.False(c.GetProperty("pspVerified").GetBoolean());
        });
        Assert.Contains(manifest.RootElement.GetProperty("familyBatches").EnumerateArray(),
            b => b.GetProperty("id").GetString() == ChainCardFamily.BatchId);
    }
}
