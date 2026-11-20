using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.FreeDuel;

namespace TagForceSpecial.Tests;

public sealed class FreeDuelPickerTests
{
    private static readonly FreeDuelPickerProfile Roster = FreeDuelPickerProfile.LoadEmbedded();
    private const int Aki = 75; // No.076 in the measured picker

    private static void Tap(FreeDuelPickerSession s, InputAction action) => s.Handle(new(action, action));
    private static void Tap(FreeDuelSession s, InputAction action) => s.Handle(new(action, action));

    [Fact]
    public void RosterHasTheMeasuredSeriesSizesAndNumbering()
    {
        Assert.Equal(192, Roster.Characters.Length);
        Assert.Equal([41, 33, 41, 41, 36], Enumerable.Range(1, 5).Select(series => Roster.InSeries(series).Count));
        Assert.Equal((3, 1, 16), (Roster.Characters[Aki].Series, Roster.Characters[Aki].Rank, Roster.Characters[Aki].Bust));
        Assert.Equal(76, FreeDuelPickerProfile.Number(Aki));
    }

    [Fact]
    public void OpensOnTheCurrentOpponentAtTheTopRow()
    {
        var p = new FreeDuelPickerSession(Roster, Aki);
        Assert.Equal((3, 1, 1, Aki), (p.Series, p.Focus, p.Top, p.FocusedCharacter));
    }

    [Fact]
    public void TabsWrapAndEachTabKeepsItsOwnCursor()
    {
        var p = new FreeDuelPickerSession(Roster, Aki);
        Tap(p, InputAction.PreviousPage); Tap(p, InputAction.PreviousPage); Tap(p, InputAction.PreviousPage);
        Assert.Equal((5, 0, 0), (p.Series, p.Focus, p.Top)); // 5D's -> GX -> DM -> ARC-V (measured wrap)
        Tap(p, InputAction.NextPage); Tap(p, InputAction.NextPage); Tap(p, InputAction.NextPage);
        Assert.Equal((3, 1, 1), (p.Series, p.Focus, p.Top));
    }

    [Fact]
    public void RightShowsTheNextPageAndUpWrapsToTheLastRowScrolling()
    {
        var p = new FreeDuelPickerSession(Roster, Aki);
        Tap(p, InputAction.Right);
        Assert.Equal((5, 5), (p.Focus, p.Top)); // No.080 on top with the cursor (measured)
        var gx = new FreeDuelPickerSession(Roster, Roster.InSeries(2)[0]);
        Tap(gx, InputAction.Up);
        Assert.Equal((32, 29), (gx.Focus, gx.Top)); // No.074 Alice on the last row (measured)
    }

    [Theory]
    [InlineData(1, 3, 1, 45f, 20f)]    // 5D's, first shown No.076 (pk-open)
    [InlineData(5, 3, 5, 65f, 20f)]    // 5D's, No.080 (pk-page)
    [InlineData(23, 5, 23, 172f, 23f)] // ARC-V, No.180 (pk-arcv-mid)
    [InlineData(32, 5, 35, 224f, 23f)] // ARC-V, last page (pk-arcv-last)
    [InlineData(0, 2, 0, 40f, 25f)]    // GX, first page (pk-gx)
    public void ScrollThumbFollowsTheMeasuredRule(int top, int series, int rank, float y, float length)
    {
        var p = new FreeDuelPickerSession(Roster, Roster.InSeries(series)[rank]);
        Assert.Equal(top, p.Top);
        Assert.Equal((y, length), p.Thumb());
    }

    [Fact]
    public void PickingSetsTheOpponentAndCancelKeepsIt()
    {
        var s = new FreeDuelSession(new FreeDuelSettingsSession(44), "P", "D", Roster, Aki, new Random(7));
        for (int i = 0; i < 5; i++) Tap(s, InputAction.Confirm);
        Tap(s, InputAction.Confirm);
        Assert.NotNull(s.Picker);
        Tap(s, InputAction.Down); Tap(s, InputAction.Back);
        Assert.Equal(Aki, s.Opponent);
        Tap(s, InputAction.Confirm); Tap(s, InputAction.Down); Tap(s, InputAction.Confirm);
        Assert.Null(s.Picker);
        Assert.Equal(Aki + 1, s.Opponent); // Jack Atlas, No.077
        Tap(s, InputAction.Switch);
        Assert.Equal(new Random(7).Next(Roster.Characters.Length), s.Opponent);
    }
}
