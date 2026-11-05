using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.Tests;

/// <summary>Routes pinned from config/reference/title-flow.json. PC Cross = confirm, PC Circle = back.</summary>
public class TitleFlowTests
{
    private sealed class Host : ITitleFlowHost
    {
        public bool ProfilePresent { get; set; }
        public bool LoadSucceeds { get; set; } = true;
        public (string Name, DuelistLevelChoice Level)? Created { get; private set; }
        public int Saves { get; private set; }
        public PlayerOptions? SavedOptions { get; private set; }
        public DuelistLevelChoice? SavedLevel { get; private set; }
        public PlayerOptions Stored { get; set; } = PlayerOptions.Defaults();
        public bool TryLoadProfile(out string name, out DuelistLevelChoice level, out PlayerOptions options)
        {
            name = "Stored"; level = DuelistLevelChoice.BeenThere; options = Stored;
            return LoadSucceeds;
        }
        public void CreateProfile(string name, DuelistLevelChoice level, PlayerOptions options) { Created = (name, level); SavedOptions = options; ProfilePresent = true; }
        public void SaveProfile(string name, DuelistLevelChoice level, PlayerOptions options) { Saves++; SavedOptions = options; SavedLevel = level; }
    }

    private static void Press(TitleFlow flow, PspAction action) => flow.Handle(new(action, action));
    /// <summary>Press and release: the held state ends, so no repeat can carry over to the next step.</summary>
    private static void Tap(TitleFlow flow, PspAction action)
    {
        Press(flow, action);
        flow.Handle(new(PspAction.None, PspAction.None));
    }
    private static TitleFlow Create(out Host host, string name = "Tester")
    {
        host = new Host();
        return new TitleFlow(host, name, instantTransitions: true);
    }

    private static TitleFlow CreateTimed(out Host host)
    {
        host = new Host();
        return new TitleFlow(host, "Tester");
    }

    private static readonly TimeSpan OneFrame = TimeSpan.FromSeconds(1 / MainMenuEntry.GameFramesPerSecond);

    [Fact]
    public void EntryIgnoresInputUntilTheFocusAppearsThenAcceptsIt()
    {
        var flow = CreateTimed(out _);
        flow.BeginMainMenu();
        flow.Update(OneFrame * (MainMenuEntry.ReadyFrame - 1));
        Tap(flow, PspAction.Down);
        Tap(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(0, flow.MenuFocus);
        flow.Update(OneFrame * 1.01);
        Assert.True(flow.MenuReady);
        Tap(flow, PspAction.Down);
        Assert.Equal(1, flow.MenuFocus);
    }

    [Fact]
    public void ReturningFromADialogDoesNotReplayTheEntrySequence()
    {
        var flow = CreateTimed(out _);
        flow.BeginMainMenu();
        flow.Update(OneFrame * (MainMenuEntry.ReadyFrame + 1));
        Tap(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.MainMenuUnavailable, flow.Step);
        Tap(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.True(flow.MenuReady);
    }

    [Fact]
    public void QuitYesWaitsBehindTheFadeAndIgnoresInputMeanwhile()
    {
        var flow = CreateTimed(out _);
        flow.BeginMainMenu();
        flow.Update(OneFrame * (MainMenuEntry.ReadyFrame + 1));
        Tap(flow, PspAction.Circle);
        Tap(flow, PspAction.Left);
        Tap(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.QuitQuestion, flow.Step);
        Assert.Equal(TitleFlowStep.ExitToTitle, flow.PendingStep);
        Tap(flow, PspAction.Right);
        Assert.Equal(0, flow.ButtonFocus);
        flow.CommitPending();
        Assert.Equal(TitleFlowStep.ExitToTitle, flow.Step);
        Assert.Null(flow.PendingStep);
    }

    [Theory]
    [InlineData(0, 255)]
    [InlineData(1, 239)]
    [InlineData(15, 15)]
    [InlineData(16, 0)]
    [InlineData(40, 0)]
    public void EntryOverlayMatchesTheMeasuredSteps(int frame, int alpha) => Assert.Equal(alpha, MainMenuEntry.OverlayAlpha(frame));

    [Theory]
    [InlineData(0, 18, 0)]
    [InlineData(0, 19, 63)]
    [InlineData(0, 20, 127)]
    [InlineData(0, 21, 191)]
    [InlineData(0, 22, 255)]
    [InlineData(3, 21, 0)]
    [InlineData(3, 22, 63)]
    [InlineData(7, 26, 63)]
    [InlineData(7, 29, 255)]
    public void BarsRevealOneRowPerFrameAsMeasured(int row, int frame, int alpha) => Assert.Equal(alpha, MainMenuEntry.BarAlpha(row, frame));

    [Theory]
    [InlineData(19, 0, -32f)]
    [InlineData(20, 51, -25.6f)]
    [InlineData(23, 204, -6.4f)]
    [InlineData(24, 255, 0f)]
    [InlineData(100, 255, 0f)]
    public void HeaderSlidesAndFadesTogetherAsMeasured(int frame, int alpha, float offset)
    {
        Assert.Equal(alpha, MainMenuEntry.HeaderAlpha(frame));
        Assert.Equal(offset, MainMenuEntry.HeaderOffset(frame), 3);
    }

    [Fact]
    public void LabelsAppearWholeAfterTheirBarAndFocusLast()
    {
        Assert.False(MainMenuEntry.LabelVisible(0, 21));
        Assert.True(MainMenuEntry.LabelVisible(0, 22));
        Assert.False(MainMenuEntry.LabelVisible(7, 28));
        Assert.True(MainMenuEntry.LabelVisible(7, 29));
        Assert.False(MainMenuEntry.FocusVisible(33));
        Assert.True(MainMenuEntry.FocusVisible(34));
    }

    [Fact]
    public void FadeRunsOutHoldInAndChangesTheScreenOnceAtBlack()
    {
        var fade = new ScreenFade();
        int changes = 0;
        fade.Begin(() => changes++, fadeIn: true);
        Assert.Equal(ScreenFadePhase.Out, fade.Phase);
        fade.Tick(OneFrame * 8.01);
        Assert.Equal(127, fade.Alpha);
        Assert.Equal(0, changes);
        fade.Tick(OneFrame * 8);
        Assert.Equal(ScreenFadePhase.Hold, fade.Phase);
        Assert.Equal(255, fade.Alpha);
        fade.Tick(OneFrame * ScreenFade.HoldFrames);
        Assert.Equal(1, changes);
        Assert.Equal(ScreenFadePhase.In, fade.Phase);
        fade.Tick(OneFrame * ScreenFade.FadeFrames);
        Assert.False(fade.Active);
        Assert.Equal(0, fade.Alpha);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void FadeWithoutFadeInEndsAtTheChangeAndLeadDelaysTheStart()
    {
        var fade = new ScreenFade();
        int changes = 0;
        fade.Begin(() => changes++, fadeIn: false, lead: ScreenFade.DialogLeadFrames);
        fade.Tick(OneFrame * (ScreenFade.DialogLeadFrames - 1));
        Assert.Equal(ScreenFadePhase.Lead, fade.Phase);
        Assert.Equal(0, fade.Alpha);
        fade.Tick(OneFrame * (1.01 + ScreenFade.FadeFrames + ScreenFade.HoldFrames));
        Assert.False(fade.Active);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ALargeTimeStepCompletesTheFadeWithoutSkippingTheChange()
    {
        var fade = new ScreenFade();
        int changes = 0;
        fade.Begin(() => changes++, fadeIn: true);
        fade.Tick(TimeSpan.FromSeconds(30));
        Assert.False(fade.Active);
        Assert.Equal(1, changes);
        Assert.Throws<InvalidOperationException>(() => { fade.Begin(() => { }, false); fade.Begin(() => { }, false); });
    }

    [Fact]
    public void ContinueWithProfileResolvesStorageCheckToMainMenuAndCannotBeCancelled()
    {
        var flow = Create(out var host);
        host.ProfilePresent = true;
        flow.BeginContinue();
        Press(flow, PspAction.Circle);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.StorageCheck, flow.Step);
        flow.Update(TitleFlow.StorageCheckDuration - TimeSpan.FromMilliseconds(1));
        Assert.Equal(TitleFlowStep.StorageCheck, flow.Step);
        flow.Update(TimeSpan.FromMilliseconds(1));
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(0, flow.MenuFocus);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ContinueWithoutUsableProfileReportsNoData(bool present, bool loads)
    {
        var flow = Create(out var host);
        host.ProfilePresent = present; host.LoadSucceeds = loads;
        flow.BeginContinue();
        flow.Update(TitleFlow.StorageCheckDuration);
        Assert.Equal(TitleFlowStep.NoData, flow.Step);
    }

    [Fact]
    public void NoDataOfferCancelLoadWhichRetriesOrExits()
    {
        var flow = Create(out _);
        flow.BeginContinue();
        flow.Update(TitleFlow.StorageCheckDuration);
        Press(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.NoData, flow.Step);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.CancelLoadNoData, flow.Step);
        Assert.Equal(0, flow.ButtonFocus);
        Press(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.CancelLoadNoData, flow.Step);
        Press(flow, PspAction.Right);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.NoData, flow.Step);
        Press(flow, PspAction.Cross);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.ExitToTitle, flow.Step);
    }

    [Fact]
    public void NewGameOpensNameEntryDirectlyBecauseTf6TransferIsUnsupported()
    {
        var flow = Create(out _);
        flow.BeginNewGame();
        Assert.Equal(TitleFlowStep.NameEntry, flow.Step);
        Assert.Equal(0, flow.ButtonCount);
        Assert.DoesNotContain(Enum.GetNames<TitleFlowStep>(), name => name.Contains("Transfer") || name.Contains("Tf6"));
    }

    [Fact]
    public void BackAnswersQuestionsAsNo()
    {
        var flow = Create(out _);
        flow.BeginNewGame();
        Press(flow, PspAction.Start);
        Assert.Equal(TitleFlowStep.RulesQuestion, flow.Step);
        Press(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.DuelistLevel, flow.Step);
        Assert.Equal(DuelistLevelChoice.FirstTimer, flow.Level);
        Press(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.DuelistLevel, flow.Step);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.CreateQuestion, flow.Step);
        Press(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
    }

    [Theory]
    [InlineData(true, PspAction.Left, DuelistLevelChoice.BringItOn)]
    [InlineData(true, PspAction.None, DuelistLevelChoice.BeenThere)]
    [InlineData(false, PspAction.None, DuelistLevelChoice.FirstTimer)]
    public void RulesAnswersSelectTheObservedDuelistLevel(bool knowsRules, PspAction confidence, DuelistLevelChoice expected)
    {
        var flow = Create(out _);
        flow.BeginNewGame();
        Press(flow, PspAction.Start);
        if (knowsRules)
        {
            Press(flow, PspAction.Left);
            Press(flow, PspAction.Cross);
            Assert.Equal(TitleFlowStep.ConfidenceQuestion, flow.Step);
            if (confidence != PspAction.None) Press(flow, confidence);
        }
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.DuelistLevel, flow.Step);
        Assert.Equal(expected, flow.Level);
    }

    [Fact]
    public void CreateNoEntersMainMenuWithoutWritingAndYesSavesThroughThePrompt()
    {
        var flow = Create(out var host);
        flow.BeginNewGame();
        Press(flow, PspAction.Start);
        Press(flow, PspAction.Cross);
        Press(flow, PspAction.Cross);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Null(host.Created);

        flow = Create(out host);
        flow.BeginNewGame();
        Press(flow, PspAction.Start);
        Press(flow, PspAction.Cross);
        Press(flow, PspAction.Cross);
        Press(flow, PspAction.Left);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.SystemSavePrompt, flow.Step);
        Assert.Null(host.Created);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.SystemSaveDone, flow.Step);
        Assert.Equal(("Tester", DuelistLevelChoice.FirstTimer), host.Created);
        Assert.Equal(PlayerOptions.Defaults().Values, host.SavedOptions!.Values);
        // Observed: "Save completed." ignores confirm and closes with back.
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.SystemSaveDone, flow.Step);
        Press(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
    }

    [Fact]
    public void NameEntryEditsWithinTheLimitAndCannotConfirmEmpty()
    {
        var flow = Create(out _, "ABCDEFGHIJK");
        Assert.Equal("ABCDEFGH", flow.Name);
        flow.BeginNewGame();
        flow.TypeCharacter('Z');
        Assert.Equal("ABCDEFGH", flow.Name);
        Press(flow, PspAction.Cross);
        Assert.True(flow.Editing);
        flow.TypeCharacter('Z');
        Assert.Equal("ABCDEFGH", flow.Name);
        for (int i = 0; i < 9; i++) Press(flow, PspAction.Circle);
        Assert.Equal("", flow.Name);
        flow.TypeCharacter('Q');
        flow.TypeCharacter('\b');
        flow.TypeCharacter('R');
        Press(flow, PspAction.Start);
        Assert.Equal(TitleFlowStep.NameEntry, flow.Step);
        Assert.Equal("R", flow.Name);
        Press(flow, PspAction.Cross);
        flow.TypeCharacter('\b');
        Press(flow, PspAction.Start);
        Press(flow, PspAction.Start);
        Assert.Equal(TitleFlowStep.NameEntry, flow.Step);
    }

    [Fact]
    public void NameEntryBackReturnsToTitle()
    {
        var flow = Create(out _);
        flow.BeginNewGame();
        Press(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.ExitToTitle, flow.Step);
    }

    [Fact]
    public void MainMenuPressWrapsAtBothEnds()
    {
        var flow = Create(out _);
        flow.BeginMainMenu();
        Tap(flow, PspAction.Up);
        Assert.Equal(TitleFlow.MainMenuRows - 1, flow.MenuFocus);
        Tap(flow, PspAction.Down);
        Assert.Equal(0, flow.MenuFocus);
    }

    [Fact]
    public void MainMenuHoldRepeatsAfterTheMeasuredDelayAndStopsAtTheEndRow()
    {
        var flow = Create(out _);
        flow.BeginMainMenu();
        Press(flow, PspAction.Down);
        Assert.Equal(1, flow.MenuFocus);
        var held = new PspInputFrame(PspAction.None, PspAction.Down);
        flow.Handle(held);
        flow.Update(TitleFlow.MenuRepeatDelay - TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, flow.MenuFocus);
        flow.Handle(held);
        flow.Update(TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, flow.MenuFocus);
        flow.Handle(held);
        flow.Update(TitleFlow.MenuRepeatInterval);
        Assert.Equal(3, flow.MenuFocus);
        // A long hold reaches the last row and stays; it never wraps.
        flow.Handle(held);
        flow.Update(TitleFlow.MenuRepeatInterval * 40);
        Assert.Equal(TitleFlow.MainMenuRows - 1, flow.MenuFocus);
    }

    [Fact]
    public void MainMenuHoldStartsAfterAWrappingPressAndWalksBack()
    {
        var flow = Create(out _);
        flow.BeginMainMenu();
        Press(flow, PspAction.Up);
        Assert.Equal(7, flow.MenuFocus);
        var held = new PspInputFrame(PspAction.None, PspAction.Up);
        flow.Handle(held);
        flow.Update(TitleFlow.MenuRepeatDelay);
        Assert.Equal(6, flow.MenuFocus);
        flow.Handle(held);
        flow.Update(TitleFlow.MenuRepeatInterval * 40);
        Assert.Equal(0, flow.MenuFocus);
    }

    [Fact]
    public void MainMenuReleaseStopsTheRepeatAndAKeyHeldFromAnotherScreenDoesNotScroll()
    {
        var flow = Create(out _);
        flow.BeginMainMenu();
        Press(flow, PspAction.Down);
        flow.Handle(new(PspAction.None, PspAction.None));
        flow.Update(TitleFlow.MenuRepeatDelay * 2);
        Assert.Equal(1, flow.MenuFocus);
        flow.Handle(new(PspAction.None, PspAction.Down));
        flow.Update(TitleFlow.MenuRepeatDelay * 2);
        Assert.Equal(1, flow.MenuFocus);
    }

    [Fact]
    public void MainMenuIgnoresButtonsWithoutAnObservedRole()
    {
        var flow = Create(out _);
        flow.BeginMainMenu();
        foreach (var action in new[] { PspAction.Left, PspAction.Right, PspAction.Triangle, PspAction.Square, PspAction.Select, PspAction.Start, PspAction.L, PspAction.R })
        {
            Tap(flow, action);
            Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
            Assert.Equal(0, flow.MenuFocus);
        }
    }

    [Fact]
    public void DestinationTableHasTheEightObservedRowsAndNeverSubstitutesAnotherFeature()
    {
        Assert.Equal(new[] { "story", "free-duel", "network", "duel-puzzle", "deck-menu", "database", "options", "save-data" },
            MainMenuDestinations.All.Select(d => d.Id));
        Assert.All(MainMenuDestinations.All.Where(d => d.Route == MainMenuRoute.Unavailable), d =>
        {
            Assert.Equal(MainMenuDestinations.SurfaceOnly, d.Status);
            Assert.False(string.IsNullOrWhiteSpace(d.Notice));
        });
        Assert.DoesNotContain(MainMenuDestinations.All, d => d.Status.Contains("feature complete", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(MainMenuRoute.NativeSave, Assert.Single(MainMenuDestinations.All, d => d.Id == "save-data").Route);
    }

    [Theory]
    [InlineData(PspAction.Cross)]
    [InlineData(PspAction.Circle)]
    public void UnavailableRowsShowANoticeAndReturnToTheSameRow(PspAction leave)
    {
        var flow = Create(out var host);
        flow.BeginMainMenu();
        for (int row = 0; row < TitleFlow.MainMenuRows - 1; row++)
        {
            if (MainMenuDestinations.All[row].Route != MainMenuRoute.Unavailable) { Tap(flow, PspAction.Down); continue; }
            Tap(flow, PspAction.Cross);
            Assert.Equal(TitleFlowStep.MainMenuUnavailable, flow.Step);
            Assert.Equal(MainMenuDestinations.All[row], flow.FocusedDestination);
            Tap(flow, leave);
            Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
            Assert.Equal(row, flow.MenuFocus);
            Tap(flow, PspAction.Down);
        }
        Assert.Equal(0, host.Saves);
    }

    [Fact]
    public void SaveDataUsesTheNativePromptAndAlwaysReturnsToItsRow()
    {
        var flow = Create(out var host);
        flow.BeginMainMenu();
        Tap(flow, PspAction.Up);
        Tap(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.SystemSavePrompt, flow.Step);
        Tap(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(7, flow.MenuFocus);
        Assert.Equal(0, host.Saves);
        Tap(flow, PspAction.Cross);
        Tap(flow, PspAction.Right);
        Tap(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(7, flow.MenuFocus);
        Assert.Equal(0, host.Saves);
        Tap(flow, PspAction.Cross);
        Tap(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.SystemSaveDone, flow.Step);
        Assert.Equal(1, host.Saves);
        Assert.Null(host.Created);
        Tap(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.SystemSaveDone, flow.Step);
        Tap(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(7, flow.MenuFocus);
    }

    [Fact]
    public void NewGameSavePromptStillBelongsToTheCreateQuestion()
    {
        var flow = Create(out var host);
        flow.BeginMainMenu();
        Tap(flow, PspAction.Up);
        Tap(flow, PspAction.Cross);
        Tap(flow, PspAction.Circle);
        flow.BeginNewGame();
        Press(flow, PspAction.Start);
        Press(flow, PspAction.Cross);
        Press(flow, PspAction.Cross);
        Press(flow, PspAction.Left);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.SystemSavePrompt, flow.Step);
        Press(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.CreateQuestion, flow.Step);
        Assert.Equal(0, host.Saves);
    }

    [Fact]
    public void QuitQuestionKeepsFocusAndExitsOnYes()
    {
        var flow = Create(out _);
        flow.BeginMainMenu();
        Tap(flow, PspAction.Down);
        Assert.Equal(1, flow.MenuFocus);
        Press(flow, PspAction.Circle);
        Assert.Equal(TitleFlowStep.QuitQuestion, flow.Step);
        Assert.Equal(1, flow.ButtonFocus);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.MainMenu, flow.Step);
        Assert.Equal(1, flow.MenuFocus);
        Press(flow, PspAction.Circle);
        Press(flow, PspAction.Left);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.ExitToTitle, flow.Step);
    }

    [Theory]
    [InlineData("Line one.\n$C1Colored$C0 end", "Line one.Colored end")]
    [InlineData("Stick%c or", "Stick™ or")]
    public void TextNormalizationMatchesTheMeasuredForm(string source, string expected) =>
        Assert.Equal(expected, TitleFlowText.Normalize(source));

    [Fact]
    public void TextReaderStopsAtTerminatorAndRejectsRangesBeyondIt()
    {
        byte[] data = System.Text.Encoding.Unicode.GetBytes("xxHello$C1 there\0tail");
        Assert.Equal("there", TitleFlowText.Read(data, 4, 6, 5));
        Assert.Throws<InvalidDataException>(() => TitleFlowText.Read(data, 4, 6, 6));
        Assert.Throws<InvalidDataException>(() => TitleFlowText.Read(data, data.Length, 0, 1));
    }

    [Fact]
    public void GeneratedFlowProfileHoldsNoOriginalStringsAndResolvesItsTextures()
    {
        var profile = TitleFlowVisuals.LoadProfile();
        string text = System.Text.Json.JsonSerializer.Serialize(profile);
        Assert.DoesNotContain("Cancel load", text);
        Assert.DoesNotContain("New Game", text);
        var pinned = TitleFlowReferenceResources.Cases.Select(c => c.AssetKey).ToHashSet();
        var embedded = profile.EbootTextures.Select(t => t.Key).ToHashSet();
        foreach (var key in profile.Frames.SelectMany(f => f.Draws).Select(d => d.AssetKey).OfType<string>())
            Assert.True(pinned.Contains(key) || embedded.Contains(key), key);
        foreach (string id in new[] { "start-menu", "storage", "no-data", "entry.rules", "entry.name", "main-menu", "main-menu.quit" })
            Assert.Contains(profile.Frames, f => f.Id == id);
        Assert.DoesNotContain(profile.Frames, f => f.Id is "entry.question" or "entry.no-tf6" or "entry.cancel-load");
    }
}
