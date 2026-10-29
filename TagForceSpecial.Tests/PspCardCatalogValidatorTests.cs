using TagForceSpecial.Cards;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

public sealed class PspCardCatalogValidatorTests
{
    private static CardDefinition Imported() => new(9, "Imported", "", new(null, 5100, 15),
        new(15, 31, 63, 63, 7, 15, 15), new(1, 9 | 511u << 14 | 510u << 23, 0, 0));

    [Fact]
    public void OriginalFormatAndVariableStatChecksStayAtImportBoundary()
    {
        PspCardCatalogValidator.Validate(Imported());
        Assert.Throws<InvalidDataException>(() => PspCardCatalogValidator.Validate(Imported() with { GameCardId = 40000 }));
        Assert.Throws<InvalidDataException>(() => PspCardCatalogValidator.Validate(Imported() with { GameCardId = 10 }));
        Assert.Throws<InvalidDataException>(() => PspCardCatalogValidator.Validate(Imported() with { SourceRow = null }));
        Assert.Throws<InvalidDataException>(() => PspCardCatalogValidator.Validate(Imported() with { SourceRow = new(0, 9, 0, 0) }));
        Assert.Throws<InvalidDataException>(() => PspCardCatalogValidator.Validate(Imported() with { Codes = null }));
    }

    [Theory]
    [InlineData(5110, 0, 4u)]
    [InlineData(1, 0, 4u)]
    [InlineData(0, 0, 16u)]
    public void CatalogValuesOutsidePspEncodingAreRejectedOnlyWhenImporting(int attack, int defense, uint level)
    {
        var card = Imported() with { Statistics = new(attack, defense, level) };
        var catalog = new CardCatalog(new(null, "authored-v1"), [card]);
        Assert.Equal(card.Statistics, catalog[9].Statistics);
        Assert.Throws<InvalidDataException>(() => PspCardCatalogValidator.Validate(card));
    }

    [Fact]
    public void PackedNumericWidthsAreImportedMetadataRatherThanGameplayConstraints()
    {
        var card = Imported() with { Codes = new(uint.MaxValue, 32, 64, 64, 8, 16, 16) };
        var catalog = new CardCatalog(new(null, "authored-v1"), [card]);
        Assert.Equal(card.Codes, catalog[9].Codes);
        Assert.Throws<InvalidDataException>(() => PspCardCatalogValidator.Validate(card));
    }
}
