using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.Tests;

/// <summary>Deck Menu behaviour observed on the same build (config/reference/deck-menu.json). Confirm = the original Circle, Back = its Cross.</summary>
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
        for (int i = 0; i < DeckMenuRow; i++) Tap(flow, InputAction.Down);
        Tap(flow, InputAction.Confirm);
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
        Tap(flow, InputAction.Back);
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
        Tap(flow, InputAction.Down); Assert.Equal(1, session.Focus);
        Tap(flow, InputAction.Down); Assert.Equal(2, session.Focus);
        Tap(flow, InputAction.Down); Assert.Equal(0, session.Focus);
        Tap(flow, InputAction.Up); Assert.Equal(2, session.Focus);
        foreach (var ignored in new[] { InputAction.Left, InputAction.Right, InputAction.Menu, InputAction.Switch, InputAction.PreviousPage, InputAction.NextPage, InputAction.Start, InputAction.Select })
        {
            Tap(flow, ignored);
            Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
            Assert.Equal(2, session.Focus);
            Assert.False(session.NoticeOpen);
        }
    }

    [Theory]
    [InlineData(InputAction.Confirm)]
    [InlineData(InputAction.Back)]
    public void EveryDestinationShowsAPcNoticeAndReturnsToItsRow(InputAction leave)
    {
        var flow = OpenSubmenu(out var host);
        var session = flow.DeckMenu!;
        for (int row = 0; row < DeckMenuDestinations.All.Count; row++)
        {
            Tap(flow, InputAction.Confirm);
            Assert.True(session.NoticeOpen);
            Assert.Equal(DeckMenuDestinations.All[row], session.Focused);
            // Directions do nothing while the notice is up.
            Tap(flow, InputAction.Down);
            Assert.Equal(row, session.Focus);
            Tap(flow, leave);
            Assert.False(session.NoticeOpen);
            Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
            Assert.Equal(row, session.Focus);
            Tap(flow, InputAction.Down);
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
            Assert.Equal(d.Id switch { "deck-edit" => DeckMenuDestinations.DeckEditCore, "recipe-viewer" => "read-only source recipes; returned deck selection unverified", _ => MainMenuDestinations.SurfaceOnly }, d.Status);
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
        Tap(flow, InputAction.Down); Tap(flow, InputAction.Down);
        Tap(flow, InputAction.Back);
        Tap(flow, InputAction.Confirm);
        Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
        Assert.Equal(0, flow.DeckMenu!.Focus);
    }

    [Fact]
    public void InputIsDroppedUntilTheFocusAppears()
    {
        var flow = OpenSubmenu(out _, timed: true);
        var profile = flow.DeckMenuProfile;
        flow.Update(OneFrame * (profile.ReadyFrame - 0.5));
        Tap(flow, InputAction.Down);
        Tap(flow, InputAction.Back);
        Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
        Assert.Equal(0, flow.DeckMenu!.Focus);
        flow.Update(OneFrame);
        Tap(flow, InputAction.Down);
        Assert.Equal(1, flow.DeckMenu.Focus);
    }

    [Fact]
    public void CloseKeepsTheSubmenuWithoutFocusThenRestartsTheMenuPulse()
    {
        var flow = OpenSubmenu(out _, timed: true);
        var profile = flow.DeckMenuProfile;
        flow.Update(OneFrame * (profile.ReadyFrame + 1));
        Tap(flow, InputAction.Back);
        var session = flow.DeckMenu!;
        Assert.True(session.Closing);
        Assert.False(session.FocusVisible(session.Frame));
        Assert.True(flow.MainMenuDimmed);
        // Input is ignored while the submenu closes.
        Tap(flow, InputAction.Down);
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
        Tap(flow, InputAction.Back);
        Assert.Equal(TitleFlowStep.QuitQuestion, flow.Step);
        Tap(flow, InputAction.Confirm);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Null(flow.MenuFocusRestartFrame);
    }

    [Fact]
    public void HeldDirectionsRepeatAndStopAtTheEndRows()
    {
        var flow = OpenSubmenu(out _, timed: true);
        flow.Update(OneFrame * (flow.DeckMenuProfile.ReadyFrame + 1));
        var down = new InputFrame(InputAction.None, InputAction.Down);
        flow.Handle(new(InputAction.Down, InputAction.Down));
        for (int i = 0; i < 120; i++) { flow.Update(OneFrame); flow.Handle(down); }
        Assert.Equal(2, flow.DeckMenu!.Focus);
        flow.Handle(new(InputAction.None, InputAction.None));
        var up = new InputFrame(InputAction.None, InputAction.Up);
        flow.Handle(new(InputAction.Up, InputAction.Up));
        for (int i = 0; i < 120; i++) { flow.Update(OneFrame); flow.Handle(up); }
        Assert.Equal(0, flow.DeckMenu.Focus);
    }
}
