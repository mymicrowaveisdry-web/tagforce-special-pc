using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.Tests;

/// <summary>Deck Menu behaviour observed on the same build (config/reference/deck-menu.json). PC Cross = confirm, PC Circle = back.</summary>
public class DeckMenuTests
{
    private const int DeckMenuRow = 4;
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

    /// <summary>Press and release, so no held direction carries into the next step.</summary>
    private static void Tap(TitleFlow flow, PspAction action)
    {
        flow.Handle(new(action, action));
        flow.Handle(new(PspAction.None, PspAction.None));
    }

    private static TitleFlow OpenSubmenu(out Host host, bool timed = false)
    {
        host = new Host();
        var flow = new TitleFlow(host, "Tester", instantTransitions: !timed);
        flow.BeginMainMenu();
        if (timed) flow.Update(OneFrame * (MainMenuEntry.ReadyFrame + 1));
        for (int i = 0; i < DeckMenuRow; i++) Tap(flow, PspAction.Down);
        Tap(flow, PspAction.Cross);
        return flow;
    }

    [Fact]
    public void ProfileHoldsTheMeasuredSubmenu()
    {
        var profile = DeckMenuProfile.LoadEmbedded();
        Assert.Equal(new[] { "deck-edit", "partner-deck-edit", "recipe-viewer" }, profile.Rows.Select(r => r.Id));
        // Labels are read from title_menu_j.ehp at runtime, from the same table as the Main Menu's own labels.
        Assert.Equal(new[] { 5239420, 5239440, 5239476 }, profile.Rows.Select(r => r.Label.Offset));
        Assert.All(profile.Rows, r => Assert.Equal("title-menu", r.Label.Source));
        // Same bar size and 23 px pitch as the Main Menu, 20 px further right, starting over the Duel Puzzle row.
        Assert.Equal(134f, profile.BarLeft);
        Assert.Equal(new[] { 120f, 143f, 166f }, profile.Rows.Select(r => r.Y));
        Assert.Equal(new[] { new[] { 60, 0, 6, 24 }, [66, 0, 1, 24], [77, 0, 3, 24] }, profile.BasePieces);
        Assert.Equal(128, profile.MenuDim);
        Assert.Equal((1, 5, 9, 2), (profile.FirstBarFrame, profile.FirstLabelFrame, profile.ReadyFrame, profile.CloseFrames));
        Assert.Equal(209, profile.FocusPulse[0]);
    }

    [Fact]
    public void OpeningFadesRowsInOneFrameApartThenShowsLabelsAndFocus()
    {
        var profile = DeckMenuProfile.LoadEmbedded();
        // Measured from frame-stepped dumps: frame 0 only dims the menu; bars are drawn from frame 1 at alpha 0.
        Assert.Equal(new[] { 0, 0, 63, 127, 191, 255, 255 }, Enumerable.Range(0, 7).Select(f => profile.BarAlpha(0, f)));
        Assert.Equal(new[] { 0, 0, 0, 63, 127, 191, 255 }, Enumerable.Range(0, 7).Select(f => profile.BarAlpha(1, f)));
        Assert.Equal(new[] { 0, 0, 0, 0, 63, 127, 191 }, Enumerable.Range(0, 7).Select(f => profile.BarAlpha(2, f)));
        Assert.Equal(new[] { 5, 6, 7 }, Enumerable.Range(0, 3).Select(r => Enumerable.Range(0, 20).First(f => profile.LabelVisible(r, f))));
        Assert.False(profile.Ready(8));
        Assert.True(profile.Ready(9));
        Assert.Equal(profile.FocusPulse[0], profile.PulseAt(9));
    }

    [Fact]
    public void DeckMenuRowOpensTheSubmenuOverTheDimmedMenuAndBackReturnsToTheRow()
    {
        Assert.Equal(MainMenuRoute.DeckMenu, MainMenuDestinations.All[DeckMenuRow].Route);
        var flow = OpenSubmenu(out var host);
        Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
        Assert.True(flow.MainMenuDimmed);
        Assert.Equal(0, flow.DeckMenu!.Focus);
        Assert.Equal(DeckMenuRow, flow.MenuFocus);
        Tap(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(DeckMenuRow, flow.MenuFocus);
        Assert.False(flow.MainMenuDimmed);
        Assert.Null(flow.DeckMenu);
        Assert.Equal(0, host.Saves);
    }

    [Fact]
    public void PressesWrapAndButtonsWithoutAnObservedRoleDoNothing()
    {
        var flow = OpenSubmenu(out _);
        var session = flow.DeckMenu!;
        Tap(flow, PspAction.Down); Assert.Equal(1, session.Focus);
        Tap(flow, PspAction.Down); Assert.Equal(2, session.Focus);
        Tap(flow, PspAction.Down); Assert.Equal(0, session.Focus);
        Tap(flow, PspAction.Up); Assert.Equal(2, session.Focus);
        foreach (var ignored in new[] { PspAction.Left, PspAction.Right, PspAction.Triangle, PspAction.Square, PspAction.L, PspAction.R, PspAction.Start, PspAction.Select })
        {
            Tap(flow, ignored);
            Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
            Assert.Equal(2, session.Focus);
            Assert.False(session.NoticeOpen);
        }
    }

    [Theory]
    [InlineData(PspAction.Cross)]
    [InlineData(PspAction.Circle)]
    public void EveryDestinationShowsAPcNoticeAndReturnsToItsRow(PspAction leave)
    {
        var flow = OpenSubmenu(out var host);
        var session = flow.DeckMenu!;
        for (int row = 0; row < DeckMenuDestinations.All.Count; row++)
        {
            Tap(flow, PspAction.Cross);
            Assert.True(session.NoticeOpen);
            Assert.Equal(DeckMenuDestinations.All[row], session.Focused);
            // Directions do nothing while the notice is up.
            Tap(flow, PspAction.Down);
            Assert.Equal(row, session.Focus);
            Tap(flow, leave);
            Assert.False(session.NoticeOpen);
            Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
            Assert.Equal(row, session.Focus);
            Tap(flow, PspAction.Down);
        }
        Assert.Equal(0, host.Saves);
    }

    [Fact]
    public void DestinationsAreHonestAndNeverSubstituteAPcTool()
    {
        Assert.Equal(new[] { "Deck Edit", "Partner Deck Edit", "Recipe Viewer" }, DeckMenuDestinations.All.Select(d => d.Label));
        Assert.All(DeckMenuDestinations.All, d =>
        {
            // Deck Edit's core screen is recreated (Phase 6.2a); the other two remain surface only.
            Assert.Equal(d.Id == "deck-edit" ? DeckMenuDestinations.DeckEditCore : MainMenuDestinations.SurfaceOnly, d.Status);
            Assert.False(string.IsNullOrWhiteSpace(d.Notice));
            Assert.False(string.IsNullOrWhiteSpace(d.OriginalBehavior));
            Assert.False(string.IsNullOrWhiteSpace(d.OriginalReturn));
            Assert.DoesNotContain("pc.", d.Id, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ReopeningAlwaysStartsOnDeckEdit()
    {
        var flow = OpenSubmenu(out _);
        Tap(flow, PspAction.Down); Tap(flow, PspAction.Down);
        Tap(flow, PspAction.Circle);
        Tap(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
        Assert.Equal(0, flow.DeckMenu!.Focus);
    }

    [Fact]
    public void InputIsDroppedUntilTheFocusAppears()
    {
        var flow = OpenSubmenu(out _, timed: true);
        var profile = flow.DeckMenuProfile;
        flow.Update(OneFrame * (profile.ReadyFrame - 0.5));
        Tap(flow, PspAction.Down);
        Tap(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
        Assert.Equal(0, flow.DeckMenu!.Focus);
        flow.Update(OneFrame);
        Tap(flow, PspAction.Down);
        Assert.Equal(1, flow.DeckMenu.Focus);
    }

    [Fact]
    public void CloseKeepsTheSubmenuWithoutFocusThenRestartsTheMenuPulse()
    {
        var flow = OpenSubmenu(out _, timed: true);
        var profile = flow.DeckMenuProfile;
        flow.Update(OneFrame * (profile.ReadyFrame + 1));
        Tap(flow, PspAction.Circle);
        var session = flow.DeckMenu!;
        Assert.True(session.Closing);
        Assert.False(session.FocusVisible(session.Frame));
        Assert.True(flow.MainMenuDimmed);
        // Input is ignored while the submenu closes.
        Tap(flow, PspAction.Down);
        Assert.Equal(0, session.Focus);
        flow.Update(OneFrame * (profile.CloseFrames - 0.5));
        Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
        flow.Update(OneFrame);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(DeckMenuRow, flow.MenuFocus);
        Assert.False(flow.MainMenuDimmed);
        Assert.Equal(0, flow.MenuFocusRestartFrame);
        flow.Update(OneFrame * 3);
        Assert.Equal(3, flow.MenuFocusRestartFrame);
        Assert.True(flow.MenuReady);
        // Any other route back to the menu (here the end-game question, answered with its default No) leaves the pulse on the menu's clock.
        Tap(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.QuitQuestion, flow.Step);
        Tap(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Null(flow.MenuFocusRestartFrame);
    }

    [Fact]
    public void HeldDirectionsRepeatAndStopAtTheEndRows()
    {
        var flow = OpenSubmenu(out _, timed: true);
        flow.Update(OneFrame * (flow.DeckMenuProfile.ReadyFrame + 1));
        var down = new PspInputFrame(PspAction.None, PspAction.Down);
        flow.Handle(new(PspAction.Down, PspAction.Down));
        for (int i = 0; i < 120; i++) { flow.Update(OneFrame); flow.Handle(down); }
        Assert.Equal(2, flow.DeckMenu!.Focus);
        flow.Handle(new(PspAction.None, PspAction.None));
        var up = new PspInputFrame(PspAction.None, PspAction.Up);
        flow.Handle(new(PspAction.Up, PspAction.Up));
        for (int i = 0; i < 120; i++) { flow.Update(OneFrame); flow.Handle(up); }
        Assert.Equal(0, flow.DeckMenu.Focus);
    }
}
