using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Album;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.Tests;

/// <summary>Card detail behaviour as measured and decoded on the same build (EBOOT 0x18e74-0x19058, 0x198a8, 0x17e98).</summary>
public class CardAlbumDetailTests
{
    private static readonly TimeSpan OneFrame = TimeSpan.FromSeconds(1 / MainMenuEntry.GameFramesPerSecond);

    private static CardAlbumDetail Detail(float description = 300, float genre = 48, float packs = 40) =>
        new(7, new(1, 0, 2), tab => tab switch { CardAlbumDetailTab.Description => description, CardAlbumDetailTab.Genre => genre, _ => packs });

    private static bool Press(CardAlbumDetail d, InputAction action) => d.Handle(new(action, action));
    private static bool Hold(CardAlbumDetail d, InputAction action) => d.Handle(new(InputAction.None, action));

    [Fact]
    public void LeftAndRightCycleTheThreeTabsAndResetTheScroll()
    {
        var d = Detail();
        for (int i = 0; i < 10; i++) Hold(d, InputAction.Down);
        Assert.Equal(20, d.Scroll);
        Press(d, InputAction.Right);
        Assert.Equal((CardAlbumDetailTab.Genre, 0f), (d.Tab, d.Scroll));
        Press(d, InputAction.Right);
        Assert.Equal(CardAlbumDetailTab.Packs, d.Tab);
        Press(d, InputAction.Right);
        Assert.Equal(CardAlbumDetailTab.Description, d.Tab);
        Press(d, InputAction.Left);
        Assert.Equal(CardAlbumDetailTab.Packs, d.Tab);
    }

    [Fact]
    public void HeldUpAndDownScrollTwoPixelsPerFrameWithinContentLessTheVisibleHeight()
    {
        var d = Detail(description: 300);
        Assert.Equal(108, d.MaxScroll);
        for (int i = 0; i < 100; i++) Hold(d, InputAction.Down);
        Assert.Equal(108, d.Scroll);
        Hold(d, InputAction.Up);
        Assert.Equal(106, d.Scroll);
        Assert.False(Press(d, InputAction.PreviousPage)); // L/R do nothing
        Press(d, InputAction.Right);
        Assert.Equal(0, d.MaxScroll); // two genre rows fit
        Assert.False(Hold(d, InputAction.Down));
    }

    [Fact]
    public void ThumbFollowsTheDecodedTrackFormula()
    {
        // Kepler's description (max scroll 168): one 4 px step moves the thumb 1 px, the next 3 px; length 178*192/360.
        var d = Detail(description: 360);
        Hold(d, InputAction.Down); Hold(d, InputAction.Down);
        Assert.Equal(1, d.Thumb(178).Offset);
        Hold(d, InputAction.Down); Hold(d, InputAction.Down);
        Assert.Equal(3, d.Thumb(178).Offset);
        Assert.Equal(94.9f, d.Thumb(178).Length, 1);
    }

    [Fact]
    public void ConfirmOpensTheDetailAndBackReturnsAfterOneBlankFrameWithTheCursorKept()
    {
        uint[] cards = Enumerable.Range(1, 40).Select(i => (uint)i).ToArray();
        var s = new CardAlbumSession(cards, CardAlbumCollection.ReferenceAllObtained(cards));
        s.UseDetail(id => new CardAlbumDetail(id, default, _ => 0));
        s.Handle(new(InputAction.Right, InputAction.Right)); s.Handle(default);
        Assert.Equal(CardAlbumOutcome.DetailRequested, s.Handle(new(InputAction.Confirm, InputAction.Confirm)));
        Assert.Equal(2u, s.Detail!.Card);
        s.Handle(new(InputAction.Down, InputAction.Down)); // the album cursor does not move under the detail
        s.Handle(new(InputAction.Back, InputAction.Back));
        Assert.True(s.Detail!.Closing);
        s.Update(OneFrame);
        Assert.NotNull(s.Detail); // the blank frame
        s.Update(OneFrame);
        Assert.Null(s.Detail);
        Assert.Equal((CardAlbumFocus.Pocket, true, 1), (s.Focus, s.RightPage, s.Pocket));
    }

    [Theory]
    [InlineData(3, 48, 0)]    // Destroy monster
    [InlineData(21, 48, 32)]  // Return card
    [InlineData(23, 80, 32)]  // Search Deck
    [InlineData(33, 112, 48)] // Counter
    [InlineData(49, 96, 164)] // Pendulum
    [InlineData(11, 0, 148)]
    [InlineData(39, 80, 147)]
    [InlineData(40, 0, 164)]
    [InlineData(47, 96, 148)]
    [InlineData(50, 64, 64)]  // Card with a different artwork
    public void GenreIconCellsFollowTheDecodedTable(int label, int u, int v) => Assert.Equal((u, v), CardDetailSource.IconCell(label));
}
