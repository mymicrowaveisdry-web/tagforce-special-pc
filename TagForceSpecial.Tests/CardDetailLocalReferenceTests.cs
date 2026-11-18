using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Cards.CatalogStorage;

namespace TagForceSpecial.Tests;

/// <summary>Card detail data read from the private original files against what the original detail screen showed for the captured cards
/// (Spell Speed digit, CARD GENRE rows, Pack List rows and badges). Private inputs only.</summary>
public class CardDetailLocalReferenceTests
{
    private static (CardDetailSource Source, Cards.CardCatalog Catalog) Load()
    {
        string root = Environment.GetEnvironmentVariable("TFSP_PRIVATE_ROOT")
            ?? throw new InvalidOperationException("Set TFSP_PRIVATE_ROOT to the private evidence root. Never copy fixtures into Git/CI.");
        string raw = Path.Combine(root, "extracted", "dual-strategy-20261104", "managed", "raw", "PSP_GAME");
        string batch = Path.Combine(root, "phase7-database-20261117");
        string catalogDir = Directory.GetDirectories(Path.Combine(batch, "pc-data", "import", "stages", "catalog"))
            .OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        return (new CardDetailSource(File.ReadAllBytes(Path.Combine(raw, "SYSDIR", "EBOOT.BIN")),
                File.ReadAllBytes(Path.Combine(raw, "USRDIR", "duelsys", "cardinfo_jpn.ehp"))),
            CardCatalogPackage.Load(Path.Combine(catalogDir, "catalog")).Catalog);
    }

    [Theory]
    [Trait("Category", "LocalReference")]
    [InlineData(7128u, 1)]   // Continuous Spell
    [InlineData(11391u, 1)]  // Normal Spell
    [InlineData(6032u, 1)]   // Effect monster
    [InlineData(11442u, 1)]  // Pendulum effect monster
    [InlineData(10846u, 1)]  // Xyz
    [InlineData(6761u, 2)]   // Normal Trap
    [InlineData(6906u, 2)]   // Quick-Play Spell
    [InlineData(9513u, 2)]   // Synchro with a quick effect
    [InlineData(4455u, null)] // Normal monster: no panel digit
    public void SpellSpeedMatchesTheCapturedPanel(uint id, int? speed)
    {
        var (source, catalog) = Load();
        var codes = catalog[id].Codes!;
        Assert.Equal(speed, source.SpellSpeed(id, codes.Type, codes.Icon, codes.GetterKind));
    }

    [Fact]
    [Trait("Category", "LocalReference")]
    public void GenreRowsAndPackRowsMatchTheCapturedTabs()
    {
        var (source, catalog) = Load();
        int[] Genres(uint id) => source.GenresOf(id, catalog[id].SourceRow!.InternalIndex).ToArray();
        Assert.Equal([33], Genres(7128));
        Assert.Equal([3, 21, 23, 49], Genres(11442));
        Assert.Equal([1, 12, 18, 35, 36, 38], Genres(9513));
        Assert.Empty(Genres(4455));
        Assert.Equal([new(37, 0), new(131, 0)], source.PacksOf(7128));
        Assert.Equal([new(34, 1), new(87, 0), new(131, 0)], source.PacksOf(6761)); // R badge
        Assert.Equal([new(28, 2), new(131, 0)], source.PacksOf(6266));               // SR badge
        Assert.Equal([new(110, 3), new(131, 0), new(175, 0)], source.PacksOf(7187)); // UR badge
        Assert.Equal(27, source.PacksOf(4909).Count);
        Assert.All(source.PackNames.Skip(1), name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal(CardDetailSource.LabelCount, source.Labels.Count);
    }
}
