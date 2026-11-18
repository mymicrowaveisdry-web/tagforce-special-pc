using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.Tests;

/// <summary>Options screen behavior. Confirm = the original Circle, Back = its Cross.</summary>
public class OptionsTests
{
    private static readonly int[] Defaults = OptionsCatalog.Defaults(DuelistLevelChoice.BeenThere);
    private static OptionsOutcome Tap(OptionsSession session, InputAction action) => session.Handle(new(action, action));

    [Fact]
    public void CatalogMatchesTheObservedRowsValuesAndNewGameDefaults()
    {
        Assert.Equal(13, OptionsCatalog.Rows.Count);
        Assert.Equal(new[] { 3, 16, 16, 16, 4, 3, 2, 2, 4, 2, 2, 2, 3 }, OptionsCatalog.Rows.Select(r => r.Count));
        Assert.Equal(new[] { 0, 15, 15, 15, 1, 1, 1, 1, 2, 0, 0, 0, 1 }, Defaults);
        // Only Duelist Level reaches a PC system; every other row says plainly that it is not applied yet.
        Assert.Equal("duelist-level", Assert.Single(OptionsCatalog.Rows, r => r.Applied).Id);
        Assert.All(OptionsCatalog.Rows.Where(r => !r.Applied), r => Assert.StartsWith("Stored;", r.PcEffect));
    }

    [Fact]
    public void ValuesClampAtBothEndsAndRowsWrap()
    {
        var session = new OptionsSession(Defaults);
        Tap(session, InputAction.Left);
        Assert.Equal(0, session.Values[0]);
        for (int i = 0; i < 5; i++) Tap(session, InputAction.Right);
        Assert.Equal(2, session.Values[0]);
        Tap(session, InputAction.Up);
        Assert.Equal(12, session.Focus);
        Tap(session, InputAction.Down);
        Assert.Equal(0, session.Focus);
        Tap(session, InputAction.Down);
        Tap(session, InputAction.Right);
        Assert.Equal(15, session.Values[1]);
        for (int i = 0; i < 20; i++) Tap(session, InputAction.Left);
        Assert.Equal(0, session.Values[1]);
    }

    [Fact]
    public void BackAlwaysAsksWithOkFocusedAndOkDiscards()
    {
        var session = new OptionsSession(Defaults);
        Assert.Equal(OptionsOutcome.None, Tap(session, InputAction.Back));
        Assert.True(session.CancelQuestion);
        Assert.Equal(0, session.CancelFocus);
        // Back does not dismiss the question.
        Tap(session, InputAction.Back);
        Assert.True(session.CancelQuestion);
        Tap(session, InputAction.Right);
        Assert.Equal(OptionsOutcome.None, Tap(session, InputAction.Confirm));
        Assert.False(session.CancelQuestion);
        Tap(session, InputAction.Back);
        Assert.Equal(OptionsOutcome.Discarded, Tap(session, InputAction.Confirm));
    }

    [Fact]
    public void ButtonsWithoutAnObservedRoleDoNothing()
    {
        var session = new OptionsSession(Defaults);
        foreach (var action in new[] { InputAction.Menu, InputAction.Switch, InputAction.Select, InputAction.Start, InputAction.PreviousPage, InputAction.NextPage })
            Assert.Equal(OptionsOutcome.None, Tap(session, action));
        Assert.False(session.Changed);
        Assert.False(session.CancelQuestion);
    }

    private sealed class Host : ITitleFlowHost
    {
        public bool ProfilePresent { get; set; } = true;
        public int Saves { get; private set; }
        public PlayerOptions? Saved { get; private set; }
        public DuelistLevelChoice SavedLevel { get; private set; }
        public bool TryLoadProfile(out string name, out DuelistLevelChoice level, out PlayerOptions options)
        {
            name = "Yuya"; level = DuelistLevelChoice.BeenThere; options = PlayerOptions.Defaults();
            return true;
        }
        public void CreateProfile(string name, DuelistLevelChoice level, PlayerOptions options) { }
        public void SaveProfile(string name, DuelistLevelChoice level, PlayerOptions options) { Saves++; Saved = options; SavedLevel = level; }
    }

    private static void Press(TitleFlow flow, InputAction action)
    {
        flow.Handle(new(action, action));
        flow.Handle(new(InputAction.None, InputAction.None));
    }

    private static TitleFlow AtOptions(out Host host)
    {
        host = new Host();
        var flow = new TitleFlow(host, "Tester", instantTransitions: true);
        flow.BeginContinue();
        flow.Update(TitleFlow.StorageCheckDuration);
        for (int i = 0; i < 6; i++) Press(flow, InputAction.Down);
        Press(flow, InputAction.Confirm);
        Assert.Equal(TitleFlowStep.Options, flow.Step);
        return flow;
    }

    [Fact]
    public void OptionsOpensWithTheLoadedProfileValuesAndLevel()
    {
        var flow = AtOptions(out _);
        Assert.Equal(Defaults, flow.Options!.Values);
    }

    [Fact]
    public void UnchangedAcceptReturnsToTheOptionsRowWithoutAsking()
    {
        var flow = AtOptions(out var host);
        Press(flow, InputAction.Confirm);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(6, flow.MenuFocus);
        Assert.Equal(0, host.Saves);
    }

    [Fact]
    public void AcceptedChangesStayForTheSessionWhenTheSaveQuestionIsAnsweredNo()
    {
        var flow = AtOptions(out var host);
        Press(flow, InputAction.Right);
        Press(flow, InputAction.Up);
        Press(flow, InputAction.Right);
        Press(flow, InputAction.Confirm);
        Assert.Equal(TitleFlowStep.OptionsSaveQuestion, flow.Step);
        Assert.Equal(0, flow.ButtonFocus);
        Press(flow, InputAction.Right);
        Press(flow, InputAction.Confirm);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(0, host.Saves);
        Assert.Equal(1, flow.SessionOptions.Values[0]);
        Assert.Equal(DuelistLevelChoice.BringItOn, flow.Level);
        Press(flow, InputAction.Confirm);
        Assert.Equal(1, flow.Options!.Values[0]);
        Assert.Equal((int)DuelistLevelChoice.BringItOn, flow.Options.Values[OptionsCatalog.DuelistLevelRow]);
    }

    [Fact]
    public void DiscardedChangesAreLost()
    {
        var flow = AtOptions(out _);
        Press(flow, InputAction.Right);
        Press(flow, InputAction.Back);
        Press(flow, InputAction.Confirm);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(0, flow.SessionOptions.Values[0]);
    }

    [Fact]
    public void YesSavesThroughTheOverwritePromptWhichDefaultsToNo()
    {
        var flow = AtOptions(out var host);
        Press(flow, InputAction.Down);
        Press(flow, InputAction.Left);
        Press(flow, InputAction.Confirm);
        Press(flow, InputAction.Confirm);
        Assert.Equal(TitleFlowStep.SystemSavePrompt, flow.Step);
        // Overwriting an existing save offers No first.
        Assert.Equal(1, flow.ButtonFocus);
        Press(flow, InputAction.Left);
        Press(flow, InputAction.Confirm);
        Assert.Equal(TitleFlowStep.SystemSaveDone, flow.Step);
        Assert.Equal(1, host.Saves);
        Assert.Equal(14, host.Saved!.Values[1]);
        Assert.Equal(DuelistLevelChoice.BeenThere, host.SavedLevel);
        Press(flow, InputAction.Confirm);
        Assert.Equal(TitleFlowStep.SystemSaveDone, flow.Step);
        Press(flow, InputAction.Back);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(6, flow.MenuFocus);
    }

    [Fact]
    public void OptionsIgnoresInputUntilItsEntrySequenceEnds()
    {
        var flow = new TitleFlow(new Host(), "Tester");
        flow.BeginMainMenu();
        flow.Update(TimeSpan.FromSeconds(2));
        for (int i = 0; i < 6; i++) Press(flow, InputAction.Down);
        Press(flow, InputAction.Confirm);
        Assert.Equal(TitleFlowStep.Options, flow.PendingStep);
        flow.CommitPending();
        Assert.Equal(TitleFlowStep.Options, flow.Step);
        flow.Update(TimeSpan.FromSeconds((MainMenuEntry.ReadyFrame - 1) / MainMenuEntry.GameFramesPerSecond));
        Press(flow, InputAction.Right);
        Assert.Equal(0, flow.Options!.Values[0]);
        flow.Update(TimeSpan.FromSeconds(2 / MainMenuEntry.GameFramesPerSecond));
        Press(flow, InputAction.Right);
        Assert.Equal(1, flow.Options.Values[0]);
    }
}
