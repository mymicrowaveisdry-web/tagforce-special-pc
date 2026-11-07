using TagForceSpecial.Effects;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

/// <summary>Deck-operation family and its lists against the local cardinfo.</summary>
public sealed class DeckOperationFamilyLocalReferenceTests
{
    [Fact]
    [Trait("Category", "LocalReference")]
    public void OriginalCatalogMatchesTheReviewedBatchAndLists()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        using var stream = File.OpenRead(Path.Combine(root,
            "data/extracted/e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707/files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp"));
        var catalog = ReviewedCardMetadata.ApplyReviewed(CardCatalogImporter.Read(stream, 3459, 11657));
        DeckOperationFamily.ValidateCatalog(catalog);
        // The lists are complete, not just valid: every icon-2 Spell, every reviewed Normal Monster at Level 5+.
        Assert.Equal(catalog.Cards.Where(c => c.Codes!.GetterKind == 13 && c.Codes.Icon == 2).Select(c => c.GameCardId),
            DeckOperationFamily.FieldSpellIds);
        Assert.Equal(TfspNormalMonsters.Ids.Where(id => catalog[id].Statistics.Level >= 5).Order(),
            DeckOperationFamily.HighLevelNormalMonsterIds.Order());
        Assert.Equal(catalog.Cards.Where(c => c.Name == "Polymerization").Select(c => c.GameCardId), DeckOperationFamily.PolymerizationIds);
        Assert.Equal(catalog.Cards.Where(c => c.Name is "Cat Girl" or "Stray Cat").Select(c => c.GameCardId).Order(),
            DeckOperationFamily.CatIds.Order());
        Assert.All(new[] { "Umi", "Yami", "Sogen", "Wasteland", "Mountain", "Forest" },
            name => Assert.Contains(catalog.Cards.Single(c => c.Name == name).GameCardId, DeckOperationFamily.FieldSpellIds));
    }
}
