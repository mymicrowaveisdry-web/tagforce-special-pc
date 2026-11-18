using System.Text.Json;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.PC.App.Screens.Album;

namespace TagForceSpecial.Tests;

/// <summary>Compares the PC album order with the order the original keeps in memory (captured from the reference save). Private inputs only.</summary>
public class CardAlbumLocalReferenceTests
{
    [Fact]
    [Trait("Category", "LocalReference")]
    public void AlbumOrderEqualsTheCapturedOriginalOrder()
    {
        string root = Environment.GetEnvironmentVariable("TFSP_PRIVATE_ROOT")
            ?? throw new InvalidOperationException("Set TFSP_PRIVATE_ROOT to the private evidence root. Never copy fixtures into Git/CI.");
        string batch = Path.Combine(root, "phase7-database-20261117");
        // Repeated imports leave one stage per decoder build; the newest is the active one.
        string catalogDir = Directory.GetDirectories(Path.Combine(batch, "pc-data", "import", "stages", "catalog"))
            .OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        var catalog = CardCatalogPackage.Load(Path.Combine(catalogDir, "catalog")).Catalog;
        var captured = JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(batch, "album-order-unlocked.json")))!;
        var order = CardAlbumLayout.Order(catalog);
        Assert.Equal(7159, captured.Length);
        Assert.Equal(captured, order);
    }
}
