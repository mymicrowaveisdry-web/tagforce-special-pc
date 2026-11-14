using TagForceSpecial.Effects;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

/// <summary>Monster trigger family against the local cardinfo.</summary>
public sealed class MonsterTriggerFamilyLocalReferenceTests
{
    [Fact]
    [Trait("Category", "LocalReference")]
    public void OriginalCatalogMatchesTheFamily()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        using var stream = File.OpenRead(Path.Combine(root,
            "data/extracted/e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707/files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp"));
        MonsterTriggerFamily.ValidateCatalog(ReviewedCardMetadata.ApplyReviewed(CardCatalogImporter.Read(stream, 3459, 11657)));
    }

    [Fact]
    [Trait("Category", "LocalReference")]
    public void TheDeckSummonBlockListMatchesTheCatalogText()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        using var stream = File.OpenRead(Path.Combine(root,
            "data/extracted/e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707/files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp"));
        var rule = new System.Text.RegularExpressions.Regex(
            @"(^|[.\n]\s*)(This card c|C)annot be Special Summoned(?! from the Graveyard)|and cannot be Special Summoned by other ways|(^|[.\n]\s*)Must (first )?be (Special|Ritual|Fusion|Synchro|Xyz) Summoned|(^|[.\n]\s*)Cannot be Normal Summoned/Set\.");
        var expected = CardCatalogImporter.Read(stream, 3459, 11657).Cards
            .Where(c => c.Statistics.Level > 0 && c.Codes is { GetterKind: not (13 or 14) } &&
                (c.Codes.GetterKind == 5 || !c.Description.Contains("Must be Special Summoned by a card effect") && rule.IsMatch(c.Description)))
            .Select(c => c.GameCardId).Order();
        Assert.Equal(expected, TfspSummonRestrictions.DeckSummonBlockedIds);
    }
}