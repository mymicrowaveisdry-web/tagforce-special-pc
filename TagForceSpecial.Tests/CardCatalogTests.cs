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
        var codes = Assert.IsType<CardNumericCodes>(catalog[9].Codes);
        Assert.Equal(3u, codes.RawKind);
        Assert.Equal(11u, codes.GetterKind);
        Assert.Throws<KeyNotFoundException>(() => catalog[8]);
    }

    [Fact]
    public void CatalogRejectsDuplicateAndZeroIds()
    {
        Assert.Throws<ArgumentException>(() => new CardCatalog(Source, [Card(9), Card(9)]));
        Assert.Throws<ArgumentException>(() => new CardCatalog(Source, [Card(0)]));
    }

    [Fact]
    public void CatalogRejectsInvalidStatsAndProvenance()
    {
        Assert.Throws<ArgumentException>(() => new CardCatalog(Source, [Card(9) with { Statistics = new(-1, 0, 4) }]));
        Assert.Throws<ArgumentException>(() => new CardCatalog(Source, [Card(9) with { Statistics = new(0, -1, 4) }]));
        Assert.Throws<ArgumentException>(() => new CardCatalog(new("bad", "synthetic"), []));
        Assert.Throws<ArgumentException>(() => new CardCatalog(new(null, " "), []));
    }

    [Fact]
    public void AuthoredCardsNeedNoPackedRowsCodesOrFictitiousSourceHash()
    {
        var card = new CardDefinition(uint.MaxValue, "Additional card", "Authored text", new(9999, 5111, 16));
        var catalog = new CardCatalog(new(null, "additional-cards-v1"), [card]);
        var stored = catalog[uint.MaxValue];
        Assert.Equal(card.Statistics, stored.Statistics);
        Assert.Null(stored.Codes);
        Assert.Null(stored.SourceRow);
        Assert.Null(catalog.Source.ContainerSha256);
        Assert.Equal(catalog.Source, stored.Source);
        Assert.Null(card.Source);
    }

    [Fact]
    public void MergePreservesEachOriginAndRawImportProvenance()
    {
        var imported = new CardCatalog(Source, [Card(9)]);
        var extra = new CardCatalog(new(null, "authored-v1"),
            [new(uint.MaxValue, "Additional", "", new(12345, 1, 20))]);
        var merged = imported.Merge(extra);
        Assert.Equal(new uint[] { 9, uint.MaxValue }, merged.Cards.Select(c => c.GameCardId));
        Assert.Equal(imported[9], merged[9]);
        Assert.Equal(extra[uint.MaxValue], merged[uint.MaxValue]);
        Assert.Equal(Source, merged[9].Source);
        Assert.Equal(imported[9].SourceRow, merged[9].SourceRow);
        Assert.Equal(extra.Source, merged[uint.MaxValue].Source);
        Assert.Null(merged.Source.ContainerSha256);
        Assert.Single(imported.Cards);
        Assert.Single(extra.Cards);
        Assert.Throws<ArgumentException>(() => imported.Merge(new(Source, [Card(9)])));
    }

    [Fact]
    public void OriginsAreNormalizedPerCardAndRejectMalformedExplicitHashes()
    {
        var source = new CardDataSource(new string('A', 64), "import-v1");
        var card = new CardDefinition(40000, "Authored", "", new(null, int.MaxValue, uint.MaxValue),
            Source: source);
        var catalog = new CardCatalog(new(null, "collection-v1"), [card]);
        Assert.Equal(new string('a', 64), catalog[40000].Source!.ContainerSha256);
        Assert.Equal(new string('A', 64), card.Source!.ContainerSha256);
        Assert.Throws<ArgumentException>(() => new CardCatalog(Source,
            [card with { Source = new("bad", "authored-v1") }]));
    }
}
