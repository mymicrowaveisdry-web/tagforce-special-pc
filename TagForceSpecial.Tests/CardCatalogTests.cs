using TagForceSpecial.Cards;

namespace TagForceSpecial.Tests;

public sealed class CardCatalogTests
{
    private static CardDataSource Source => new(new string('a', 64), "synthetic-v1");
    private static CardDefinition Card(uint id) => new(id, "Synthetic card", "$unknown preserved",
        new(null, 0, 4), new(1, 2, 3, 11, 0, 4, 5), new(1, id, 0, 7));

    [Fact]
    public void CatalogSnapshotsInputAndUsesGameIdsWithoutMergingNames()
    {
        var input = new List<CardDefinition> { Card(12), Card(9) };
        var catalog = new CardCatalog(Source, input);
        input.Clear();
        Assert.Equal(2, catalog.Count);
        Assert.Equal(new uint[] { 9, 12 }, catalog.Cards.Select(c => c.GameCardId));
        Assert.Null(catalog[9].Statistics.Attack);
        Assert.Equal(0, catalog[9].Statistics.Defense);
        Assert.Equal("$unknown preserved", catalog[9].Description);
        Assert.Equal(3u, catalog[9].Codes.RawKind);
        Assert.Equal(11u, catalog[9].Codes.GetterKind);
        Assert.Throws<KeyNotFoundException>(() => catalog[8]);
    }

    [Fact]
    public void CatalogRejectsDuplicateIdsAndMismatchedSourceIdentity()
    {
        Assert.Throws<ArgumentException>(() => new CardCatalog(Source, [Card(9), Card(9)]));
        Assert.Throws<ArgumentException>(() => new CardCatalog(Source, [Card(9) with { GameCardId = 10 }]));
    }

    [Fact]
    public void CatalogRejectsInvalidStatsAndProvenance()
    {
        Assert.Throws<ArgumentException>(() => new CardCatalog(Source, [Card(9) with { Statistics = new(5110, 0, 4) }]));
        Assert.Throws<ArgumentException>(() => new CardCatalog(Source, [Card(9) with { Statistics = new(1, 0, 4) }]));
        Assert.Throws<ArgumentException>(() => new CardCatalog(new("bad", "synthetic"), []));
    }
}
