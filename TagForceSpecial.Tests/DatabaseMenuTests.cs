using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.Tests;

/// <summary>Database submenu behaviour observed on the same build with the reference save. Confirm = the original Circle, Back = its Cross.
/// The PC keeps the first seven rows; the website and downloaded data rows are left out by owner decision.</summary>
public class DatabaseMenuTests
{
    private const int DatabaseRow = 5;
    private static readonly TimeSpan OneFrame = TimeSpan.FromSeconds(1 / MainMenuEntry.GameFramesPerSecond);

    private sealed class Host : ITitleFlowHost
    {
        public int Saves { get; private set; }
        public bool ProfilePresent => false;
        public bool TryLoadProfile(out string name, out DuelistLevelChoice level, out PlayerOptions options)
        {
            name = ""; level = DuelistLevelChoice.FirstTimer; options = PlayerOptions.Defaults();
            return false;
        }
        public void CreateProfile(string name, DuelistLevelChoice level, PlayerOptions options) => Saves++;
        public void SaveProfile(string name, DuelistLevelChoice level, PlayerOptions options) => Saves++;
    }

    private static void Tap(TitleFlow flow, InputAction action)
    {
        flow.Handle(new(action, action));
        flow.Handle(new(InputAction.None, InputAction.None));
    }

    private static TitleFlow OpenSubmenu(out Host host, bool timed = false)
    {
        host = new Host();
        var flow = new TitleFlow(host, "Tester", instantTransitions: !timed);
        flow.BeginMainMenu();
        if (timed) flow.Update(OneFrame * (MainMenuEntry.ReadyFrame + 1));
        for (int i = 0; i < DatabaseRow; i++) Tap(flow, InputAction.Down);
        Tap(flow, InputAction.Confirm);
        return flow;
    }

    [Fact]
    public void ProfileHoldsTheFirstSevenMeasuredRows()
    {
        var profile = SubmenuProfile.LoadEmbedded(SubmenuKind.Database);
        Assert.Equal(new[] { "list-of-duelists", "card-album", "photo-gallery", "music-player", "tutorial", "duel-ranking", "forbidden-limited" },
            profile.Rows.Select(r => r.Id));
        // Labels come from the same title_menu_j.ehp table as the Deck Menu's, directly before it.
        Assert.Equal(new[] { 5239122, 5239156, 5239178, 5239206, 5239232, 5239250, 5239276 }, profile.Rows.Select(r => r.Label.Offset));
        Assert.All(profile.Rows, r => Assert.Equal("title-menu", r.Label.Source));
        // Same bar left, base pieces and 23 px pitch as the Deck Menu, starting at y 51.
        Assert.Equal(134f, profile.BarLeft);
        Assert.Equal(Enumerable.Range(0, 7).Select(i => 51f + 23 * i), profile.Rows.Select(r => r.Y));
        Assert.Equal(new[] { new[] { 60, 0, 6, 24 }, [66, 0, 1, 24], [77, 0, 3, 24] }, profile.BasePieces);
        Assert.Equal(profile.Rows.Length, DatabaseMenuDestinations.All.Count);
    }

    [Fact]
    public void DatabaseRowOpensTheSubmenuOverTheDimmedMenuAndBackReturnsToTheRow()
    {
        Assert.Equal(MainMenuRoute.DatabaseMenu, MainMenuDestinations.All[DatabaseRow].Route);
        var flow = OpenSubmenu(out var host);
        Assert.Equal(TitleFlowStep.DatabaseMenu, flow.Step);
        Assert.Equal(SubmenuKind.Database, flow.Submenu!.Kind);
        Assert.True(flow.MainMenuDimmed);
        Assert.Equal(0, flow.Submenu.Focus);
        Tap(flow, InputAction.Back);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(DatabaseRow, flow.MenuFocus);
        Assert.Null(flow.Submenu);
        Assert.Same(flow.DatabaseMenuProfile, flow.ClosedSubmenuProfile);
        Assert.Equal(0, host.Saves);
    }

    [Fact]
    public void PressesWrapAtBothEnds()
    {
        var flow = OpenSubmenu(out _);
        var session = flow.Submenu!;
        int last = DatabaseMenuDestinations.All.Count - 1;
        Tap(flow, InputAction.Up); Assert.Equal(last, session.Focus);
        Tap(flow, InputAction.Down); Assert.Equal(0, session.Focus);
        foreach (var ignored in new[] { InputAction.Left, InputAction.Right, InputAction.Menu, InputAction.Switch, InputAction.PreviousPage, InputAction.NextPage, InputAction.Start, InputAction.Select })
        {
            Tap(flow, ignored);
            Assert.Equal(TitleFlowStep.DatabaseMenu, flow.Step);
            Assert.Equal(0, session.Focus);
            Assert.False(session.NoticeOpen);
        }
    }

    [Theory]
    [InlineData(InputAction.Confirm)]
    [InlineData(InputAction.Back)]
    public void EveryRowShowsAPcNoticeAndReturnsToItsRow(InputAction leave)
    {
        var flow = OpenSubmenu(out var host);
        var session = flow.Submenu!;
        for (int row = 0; row < DatabaseMenuDestinations.All.Count; row++)
        {
            Tap(flow, InputAction.Confirm);
            Assert.True(session.NoticeOpen);
            Assert.Equal(DatabaseMenuDestinations.All[row], session.Focused);
            Tap(flow, leave);
            Assert.False(session.NoticeOpen);
            Assert.Equal(TitleFlowStep.DatabaseMenu, flow.Step);
            Assert.Equal(row, session.Focus);
            Tap(flow, InputAction.Down);
        }
        Assert.Equal(0, host.Saves);
    }

    [Fact]
    public void RowsAreHonestAndLeaveOutThePspOnlyRows()
    {
        Assert.Equal(new[] { "List of Duelists", "Card Album", "Photo Gallery", "Music Player", "Tutorial", "Duel Ranking", "Forbidden/Limited Card List" },
            DatabaseMenuDestinations.All.Select(d => d.Label));
        Assert.All(DatabaseMenuDestinations.All, d =>
        {
            Assert.Equal(d.Id == "card-album" ? DatabaseMenuDestinations.CardAlbumStatus : MainMenuDestinations.SurfaceOnly, d.Status);
            Assert.False(string.IsNullOrWhiteSpace(d.Notice));
            Assert.False(string.IsNullOrWhiteSpace(d.OriginalBehavior));
            Assert.False(string.IsNullOrWhiteSpace(d.OriginalReturn));
        });
        Assert.DoesNotContain(DatabaseMenuDestinations.All, d => d.Label.Contains("Website", StringComparison.Ordinal) || d.Label.Contains("Downloaded", StringComparison.Ordinal));
    }

    [Fact]
    public void InputIsDroppedUntilTheFocusAppears()
    {
        var flow = OpenSubmenu(out _, timed: true);
        var profile = flow.DatabaseMenuProfile;
        flow.Update(OneFrame * (profile.ReadyFrame - 0.5));
        Tap(flow, InputAction.Down);
        Tap(flow, InputAction.Back);
        Assert.Equal(TitleFlowStep.DatabaseMenu, flow.Step);
        Assert.Equal(0, flow.Submenu!.Focus);
        flow.Update(OneFrame);
        Tap(flow, InputAction.Down);
        Assert.Equal(1, flow.Submenu.Focus);
    }

    [Fact]
    public void HeldDirectionsRepeatAndStopAtTheEndRows()
    {
        var flow = OpenSubmenu(out _, timed: true);
        flow.Update(OneFrame * (flow.DatabaseMenuProfile.ReadyFrame + 1));
        var down = new InputFrame(InputAction.None, InputAction.Down);
        flow.Handle(new(InputAction.Down, InputAction.Down));
        for (int i = 0; i < 240; i++) { flow.Update(OneFrame); flow.Handle(down); }
        Assert.Equal(DatabaseMenuDestinations.All.Count - 1, flow.Submenu!.Focus);
        flow.Handle(new(InputAction.None, InputAction.None));
        var up = new InputFrame(InputAction.None, InputAction.Up);
        flow.Handle(new(InputAction.Up, InputAction.Up));
        for (int i = 0; i < 240; i++) { flow.Update(OneFrame); flow.Handle(up); }
        Assert.Equal(0, flow.Submenu.Focus);
    }
}
