using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Album;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.Tests;

/// <summary>Card Album input as observed on the same build, on a synthetic 40-card album (3 sheets, 4 views). Confirm = Circle, Back = Cross.</summary>
public class CardAlbumSessionTests
{
    private static readonly TimeSpan OneFrame = TimeSpan.FromSeconds(1 / MainMenuEntry.GameFramesPerSecond);
    private static readonly uint[] Cards = Enumerable.Range(1, 40).Select(i => (uint)i).ToArray();

    private static CardAlbumSession Session(Func<uint, bool>? owned = null)
    {
        var collection = owned is null ? CardAlbumCollection.ReferenceAllObtained(Cards) : CardAlbumCollection.ReferenceAllObtained(Cards.Where(owned));
        return new CardAlbumSession(Cards, collection);
    }

    private static CardAlbumOutcome Tap(CardAlbumSession s, InputAction action)
    {
        var outcome = s.Handle(new(action, action));
        s.Handle(new(InputAction.None, InputAction.None));
        return outcome;
    }

    private static void Settle(CardAlbumSession s) { for (int i = 0; i < 400 && (s.Turn is not null || s.Flipping || s.JumpFrame is not null); i++) s.Update(OneFrame); }

    [Fact]
    public void OpensOnTheFirstViewWithTheCursorOnTheFirstPocket()
    {
        var s = Session();
        Assert.Equal(4, s.Views);
        Assert.Equal((0, CardAlbumFocus.Pocket, true, 0), (s.View, s.Focus, s.RightPage, s.Pocket));
        Assert.Equal(1u, s.PreviewCard);
        Assert.Equal(1, s.CounterSheet);
    }

    [Fact]
    public void DirectionsMoveInThePageAndRightPastTheLastColumnReachesTheEdgeThatTurnsForward()
    {
        var s = Session();
        Tap(s, InputAction.Up); Tap(s, InputAction.Left);
        Assert.Equal(0, s.Pocket); // no left page on the first view; top row stays
        Tap(s, InputAction.Right); Tap(s, InputAction.Right); Tap(s, InputAction.Right);
        Assert.Equal(CardAlbumFocus.Edge, s.Focus);
        Tap(s, InputAction.Confirm);
        Assert.NotNull(s.Turn);
        Assert.Equal(1, s.View);
        Settle(s);
        Assert.Null(s.Turn);
    }

    [Fact]
    public void LeftFromTheRightPageCrossesToTheLeftPageAndTheCounterFollowsTheCursor()
    {
        var s = Session();
        Tap(s, InputAction.NextPage); Settle(s);
        Assert.Equal(2, s.CounterSheet);
        Tap(s, InputAction.Left);
        Assert.False(s.RightPage);
        Assert.Equal(2, s.Pocket);
        Assert.Equal(1, s.CounterSheet);
        Assert.Equal((uint)(9 + 2 + 1), s.PreviewCard); // back of sheet 1, top-right pocket = position 11
    }

    [Fact]
    public void ShoulderTurnsStopAtTheEndsAndInputWaitsForTheTurn()
    {
        var s = Session();
        Tap(s, InputAction.PreviousPage);
        Assert.Null(s.Turn);
        Tap(s, InputAction.NextPage);
        Tap(s, InputAction.NextPage); // ignored while turning
        Assert.Equal(1, s.View);
        Settle(s);
        for (int i = 0; i < 5; i++) { Tap(s, InputAction.NextPage); Settle(s); }
        Assert.Equal(3, s.View);
    }

    [Fact]
    public void BarButtonsWrapAndDoTheirMeasuredActions()
    {
        var s = Session();
        Tap(s, InputAction.Switch);
        Assert.Equal((CardAlbumFocus.Bar, 0), (s.Focus, s.BarButton));
        Tap(s, InputAction.Left);
        Assert.Equal(5, s.BarButton);
        Tap(s, InputAction.Confirm); Settle(s); // book button: fast flip to the last view
        Assert.Equal(3, s.View);
        Tap(s, InputAction.Right); Tap(s, InputAction.Confirm); Settle(s); // wraps to 0: flip to the first view
        Assert.Equal(0, s.View);
        Tap(s, InputAction.Right); Tap(s, InputAction.Right); Tap(s, InputAction.Right); Tap(s, InputAction.Right); // card +1
        Tap(s, InputAction.Confirm);
        Assert.Equal(1, s.Pocket);
        Assert.Equal(CardAlbumFocus.Bar, s.Focus);
        Assert.Equal(2u, s.PreviewCard);
    }

    [Fact]
    public void ConfirmAsksForDetailOnlyOnOwnedCardsAndBackCloses()
    {
        var s = Session(id => id != 1);
        Assert.Null(s.PreviewCard);
        Assert.Equal(CardAlbumOutcome.None, Tap(s, InputAction.Confirm));
        Tap(s, InputAction.Right);
        Assert.Equal(CardAlbumOutcome.DetailRequested, Tap(s, InputAction.Confirm));
        Assert.Equal(CardAlbumOutcome.Close, Tap(s, InputAction.Back));
    }

    [Fact]
    public void PageJumpOpensOnTheStarWrapsAndJumpsToTheFirstOwnedCardWithTheLetter()
    {
        // Positions 0-9 start with symbols, 10-24 with "A", 25-39 with "B"; card 11 ("A") is unowned.
        var initials = Enumerable.Range(0, 40).Select(i => i < 10 ? '*' : i < 25 ? 'A' : 'B').ToArray();
        var s = new CardAlbumSession(Cards, CardAlbumCollection.ReferenceAllObtained(Cards.Where(id => id != 11)), initials);
        Tap(s, InputAction.Menu);
        Assert.True(s.JumpOpen);
        Assert.Equal((0, 0), (s.JumpRow, s.JumpColumn));
        Tap(s, InputAction.Up); Tap(s, InputAction.Left);
        Assert.Equal((4, 7), (s.JumpRow, s.JumpColumn));
        Tap(s, InputAction.Confirm); // blank cell: nothing
        Assert.True(s.JumpOpen);
        Tap(s, InputAction.Down); Tap(s, InputAction.Right); Tap(s, InputAction.Right); // row 0, column 1: "A"
        Assert.True(s.JumpEnabled('A'));
        Assert.False(s.JumpEnabled('C'));
        Tap(s, InputAction.Confirm);
        Assert.False(s.JumpOpen);
        Assert.Equal(0, s.JumpFrame);
        Assert.Equal(1u, s.PreviewCard); // the old preview stays through the close and arrival
        Settle(s);
        Assert.Equal(12u, s.PreviewCard); // position 10 is card 11 (unowned), so the first owned "A" is position 11
        Tap(s, InputAction.Menu); Tap(s, InputAction.Right); Tap(s, InputAction.Right); Tap(s, InputAction.Right); // "C": disabled
        Tap(s, InputAction.Confirm);
        Assert.True(s.JumpOpen);
        Assert.Equal(CardAlbumOutcome.None, Tap(s, InputAction.Back));
        Assert.False(s.JumpOpen);
    }

    [Fact]
    public void PageJumpArrivalKeepsTheOldViewUntilAfterTheTurnPoseAndHidesTheHand()
    {
        var initials = Enumerable.Range(0, 40).Select(i => i < 25 ? '*' : 'B').ToArray();
        var s = new CardAlbumSession(Cards, CardAlbumCollection.ReferenceAllObtained(Cards), initials);
        s.UseJumpTiming(turnFrame: 5, length: 7);
        Assert.False(s.HandHidden);
        Tap(s, InputAction.Menu);
        Assert.True(s.HandHidden);
        Tap(s, InputAction.Right); Tap(s, InputAction.Right); Tap(s, InputAction.Confirm); // "B": position 25 = view 1, right page, pocket 7
        var seen = new List<(int? Frame, int View)>();
        for (int i = 0; i < 8; i++) { seen.Add((s.JumpFrame, s.View)); s.Update(OneFrame); }
        Assert.Equal(new (int?, int)[] { (0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 0), (6, 1), (null, 1) }, seen);
        Assert.Equal((1, 1), (s.JumpTargetView, s.JumpDirection));
        Assert.False(s.HandHidden);
        Assert.Equal((true, 7, 26u), (s.RightPage, s.Pocket, s.PreviewCard!.Value));
    }

    [Fact]
    public void GlintSlidesOutAndBackOverSixtyFrames()
    {
        var glint = CardAlbumProfile.LoadEmbedded().Glint;
        Assert.Equal(60, glint.Period);
        // Turn captures at two phases: 2.59 px per frame out to 127.0, then 11.545 px per frame back.
        Assert.Equal(new[] { 0f, 2.6f, 124.4f, 127f, 115.5f, 23.1f, 0f, 2.6f },
            new[] { 0, 1, 48, 49, 50, 58, 60, 61 }.Select(f => MathF.Round(glint.Left(f), 1)));
    }

    [Fact]
    public void CompletionIsTruncatedToOneDecimal()
    {
        var order = Enumerable.Range(1, 7159).Select(i => (uint)i).ToArray();
        Assert.Equal("0.6", CardAlbumCollection.ReferenceAllObtained(order.Take(49)).CompletionText(order));
        Assert.Equal("100", CardAlbumCollection.ReferenceAllObtained(order).CompletionText(order));
    }

    [Fact]
    public void CompletionCharactersSitAtTheCapturedPositions()
    {
        var cells = CardAlbumProfile.LoadEmbedded().PercentCells;
        // Fresh save "0.6" and unlocked save "100", glyph x from the album GE dumps.
        Assert.Equal(new[] { 433.8f, 442.1f, 446.8f }, CardAlbumRenderer.PercentPositions("0.6", cells).Select(p => MathF.Round(p.X, 1)));
        Assert.Equal(new[] { 428.8f, 437.8f, 446.8f }, CardAlbumRenderer.PercentPositions("100", cells).Select(p => MathF.Round(p.X, 1)));
    }
}
