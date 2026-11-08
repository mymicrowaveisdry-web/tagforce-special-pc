using TagForceSpecial.Effects;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

/// <summary>Graveyard family against the local cardinfo.</summary>
public sealed class GraveyardFamilyLocalReferenceTests
{
    [Fact]
    [Trait("Category", "LocalReference")]
    public void OriginalCatalogMatchesTheFamily()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        using var stream = File.OpenRead(Path.Combine(root,
            "data/extracted/e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707/files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp"));
        GraveyardFamily.ValidateCatalog(ReviewedCardMetadata.ApplyReviewed(CardCatalogImporter.Read(stream, 3459, 11657)));
    }
}
