using TagForceSpecial.Cards;
using TagForceSpecial.Effects;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

/// <summary>Life-point family against the local cardinfo and the LP table in the PRX.</summary>
public sealed class LifePointFamilyLocalReferenceTests
{
    private const string Extracted = "data/extracted/e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707/files/PSP_GAME/USRDIR/";
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));

    [Fact]
    [Trait("Category", "LocalReference")]
    public void OriginalCatalogMatchesTheReviewedBatch()
    {
        using var stream = File.OpenRead(Path.Combine(Root, Extracted + "duelsys/cardinfo_jpn.ehp"));
        var catalog = ReviewedCardMetadata.ApplyReviewed(CardCatalogImporter.Read(stream, 3459, 11657));
        LifePointFamily.ValidateCatalog(catalog);
    }

    [Fact]
    [Trait("Category", "LocalReference")]
    public void SignedTableRecordsMatchTheTableBackedCards()
    {
        // 188 records of {u16 card ID, s16 signed amount} at file offset 0x47fc5c (historical RAM 0x09c98ec4).
        var bytes = File.ReadAllBytes(Path.Combine(Root, Extracted + "gmodule/rel_DuelEng.prx"));
        var table = Enumerable.Range(0, 188).ToDictionary(i => (uint)BitConverter.ToUInt16(bytes, 0x47fc5c + 4 * i),
            i => (int)BitConverter.ToInt16(bytes, 0x47fc5c + 4 * i + 2));
        Assert.Equal(188, table.Count);
        foreach (var (id, amount) in new Dictionary<uint, int> { [4345] = 500, [4348] = 1000, [4349] = -200, [4350] = -500, [4352] = -800, [4840] = 400 })
            Assert.Equal(amount, table[id]); // the preview values locate the table
        Assert.All(LifePointFamily.TableAmounts, entry => Assert.Equal(entry.Value, table[entry.Key]));
        // The other cards of the family are absent from the table.
        Assert.All(LifePointFamily.Cards.Where(c => !LifePointFamily.TableAmounts.ContainsKey(c.CardId)),
            c => Assert.DoesNotContain(c.CardId, table.Keys));
    }
}
