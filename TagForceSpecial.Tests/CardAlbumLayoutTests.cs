using TagForceSpecial.Cards;
using TagForceSpecial.PC.App.Screens.Album;

namespace TagForceSpecial.Tests;

/// <summary>Card Album order and binder arithmetic measured on the same build; names here are synthetic stand-ins for each observed rule.</summary>
public class CardAlbumLayoutTests
{
    private static CardCatalog Catalog(params (uint Id, string Name, uint RawKind)[] cards) =>
        new(new CardDataSource(null, "test"), cards.Select(c => new CardDefinition(c.Id, c.Name, "", new CardStatistics(0, 0, 1),
            new CardNumericCodes(0, 0, c.RawKind, 0, 0, 0, 0))));

    private static string[] Names(CardCatalog catalog) => CardAlbumLayout.Order(catalog).Select(id => catalog[id].Name).ToArray();

    [Fact]
    public void OrderFollowsTheMeasuredCollationAndLeavesOutTokens()
    {
        var catalog = Catalog(
            (1, "Abyss-scale of Cetus", 1), (2, "Abyssal Designator", 1), (3, "Abyss Warrior", 1),
            (4, "3-Hump Lacooda", 1), (5, "30,000-Year White Turtle", 1), (6, "\"A\" Cell Incubator", 1), (7, "1st Movement Solo", 1),
            (8, "Cat's Ear Tribe", 1), (9, "Catoblepas and the Witch of Fate", 1),
            (10, "Number 101: Silent Honor ARK", 1), (11, "Number 10: Illumiknight", 1),
            (12, "Rai-Mei", 1), (13, "Raimei", 1), (14, "Eagle Eye", 1), (15, "E☆HERO Pit Boss", 1),
            (16, "Cú Chulainn the Awakened", 1), (17, "Cunning of the Six Samurai", 1), (18, "Sheep Token", 10));
        Assert.Equal(new[]
        {
            "\"A\" Cell Incubator", "1st Movement Solo", "30,000-Year White Turtle", "3-Hump Lacooda",
            "Abyss Warrior", "Abyssal Designator", "Abyss-scale of Cetus", "Catoblepas and the Witch of Fate", "Cat's Ear Tribe",
            "Cú Chulainn the Awakened", "Cunning of the Six Samurai", "E☆HERO Pit Boss", "Eagle Eye",
            "Number 10: Illumiknight", "Number 101: Silent Honor ARK", "Raimei", "Rai-Mei"
        }, Names(catalog));
    }

    [Fact]
    public void AlternateArtworksOfOneNameRunByDescendingCardId()
    {
        var catalog = Catalog((3800, "Blue-Eyes White Dragon", 1), (4007, "Blue-Eyes White Dragon", 1), (3862, "Blue-Eyes White Dragon", 1));
        Assert.Equal(new uint[] { 4007, 3862, 3800 }, CardAlbumLayout.Order(catalog));
    }

    [Fact]
    public void SheetsAndViewsMatchTheMeasuredBinder()
    {
        const int cards = 7159;
        Assert.Equal(398, CardAlbumLayout.Sheets(cards));
        Assert.Equal(399, CardAlbumLayout.Views(cards));
        // First view: no left page, right page = front of sheet 1.
        Assert.Null(CardAlbumLayout.Position(0, false, 0, cards));
        Assert.Equal(0, CardAlbumLayout.Position(0, true, 0, cards));
        // A middle view: back of sheet 104 on the left, front of sheet 105 on the right.
        Assert.Equal(1863, CardAlbumLayout.Position(104, false, 0, cards));
        Assert.Equal(1871, CardAlbumLayout.Position(104, false, 8, cards));
        Assert.Equal(1872, CardAlbumLayout.Position(104, true, 0, cards));
        // Last view: back of sheet 398 holds the final four cards; no right page.
        Assert.Equal(7158, CardAlbumLayout.Position(398, false, 3, cards));
        Assert.Null(CardAlbumLayout.Position(398, false, 4, cards));
        Assert.Null(CardAlbumLayout.Position(398, true, 0, cards));
    }

    [Fact]
    public void CounterShowsTheSheetUnderTheCursorAndLocateRoundTrips()
    {
        Assert.Equal(1, CardAlbumLayout.CounterSheet(0, true));
        Assert.Equal(104, CardAlbumLayout.CounterSheet(104, false));
        Assert.Equal(105, CardAlbumLayout.CounterSheet(104, true));
        foreach (int position in new[] { 0, 8, 9, 17, 1863, 1872, 7158 })
        {
            var (view, right, pocket) = CardAlbumLayout.Locate(position);
            Assert.Equal(position, CardAlbumLayout.Position(view, right, pocket, 7159));
        }
        Assert.Equal(1863, CardAlbumLayout.SlotPosition(105, -9, 7159));
        Assert.Null(CardAlbumLayout.SlotPosition(1, -1, 7159));
    }
}
