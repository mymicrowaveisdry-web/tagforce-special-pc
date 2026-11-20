using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.FreeDuel;

namespace TagForceSpecial.Tests;

public sealed class FreeDuelSettingsTests
{
    private const int MusicRows = 44; // measured: two Random rows and 42 titles, "Track Title" included

    private static FreeDuelSettingsOutcome Tap(FreeDuelSettingsSession s, InputAction action) => s.Handle(new(action, action));

    [Fact]
    public void ListsWrapBothWays()
    {
        var s = new FreeDuelSettingsSession(MusicRows);
        Tap(s, InputAction.Up);
        Assert.Equal(2, s.Focus);
        Tap(s, InputAction.Down);
        Assert.Equal(0, s.Focus);
        Tap(s, InputAction.Left); Tap(s, InputAction.Right);
        Assert.Equal((FreeDuelStep.Format, 0), (s.Step, s.Focus));
    }

    [Fact]
    public void StandardPathReachesConfirmationWith8000LifePoints()
    {
        var s = new FreeDuelSettingsSession(MusicRows);
        foreach (var expected in new[] { FreeDuelStep.Ending, FreeDuelStep.Rules, FreeDuelStep.Music, FreeDuelStep.Confirm })
        {
            Assert.Equal(FreeDuelSettingsOutcome.None, Tap(s, InputAction.Confirm));
            Assert.Equal((expected, 0), (s.Step, s.Focus));
        }
        Assert.Equal(new FreeDuelSettings(0, 0, 0, 1, 0, 0), s.Settings);
        Assert.Equal(8000, s.Settings!.StartingLifePoints);
        Assert.Equal(FreeDuelSettingsOutcome.Next, Tap(s, InputAction.Confirm));
    }

    [Fact]
    public void RuleChangeAsksLifePointsWith4000FirstThenTimeLimit()
    {
        var s = new FreeDuelSettingsSession(MusicRows);
        Tap(s, InputAction.Confirm); Tap(s, InputAction.Confirm);
        Tap(s, InputAction.Down); Tap(s, InputAction.Confirm);
        Assert.Equal((FreeDuelStep.LifePoints, 0), (s.Step, s.Focus));
        Tap(s, InputAction.Confirm);
        Assert.Equal(FreeDuelStep.TimeLimit, s.Step);
        Tap(s, InputAction.Confirm); Tap(s, InputAction.Confirm);
        Assert.Equal(4000, s.Settings!.StartingLifePoints);
    }

    [Fact]
    public void BackReturnsOneStepWithTheEarlierChoiceFocusedAndForgetsLaterOnes()
    {
        var s = new FreeDuelSettingsSession(MusicRows);
        Tap(s, InputAction.Confirm); Tap(s, InputAction.Confirm);
        Tap(s, InputAction.Down); Tap(s, InputAction.Confirm); // Yes (Rule Change)
        Tap(s, InputAction.Confirm);                          // LP 4000
        Tap(s, InputAction.Back);
        Assert.Equal((FreeDuelStep.LifePoints, 0), (s.Step, s.Focus));
        Assert.Null(s.Chosen(FreeDuelStep.LifePoints));
        Tap(s, InputAction.Back);
        Assert.Equal((FreeDuelStep.Rules, 1), (s.Step, s.Focus));
        Assert.Equal(0, s.Chosen(FreeDuelStep.Ending));
        Tap(s, InputAction.Back);
        Assert.Equal((FreeDuelStep.Ending, 0), (s.Step, s.Focus));
        Tap(s, InputAction.Back);
        Assert.Equal((FreeDuelStep.Format, 0), (s.Step, s.Focus));
        Assert.Equal(FreeDuelSettingsOutcome.Close, Tap(s, InputAction.Back));
    }

    [Theory]
    [InlineData(false, FreeDuelStep.Rules)]
    [InlineData(true, FreeDuelStep.TimeLimit)]
    public void BackFromMusicReturnsToTheStepThatLedThere(bool ruleChange, FreeDuelStep expected)
    {
        var s = new FreeDuelSettingsSession(MusicRows);
        Tap(s, InputAction.Confirm); Tap(s, InputAction.Confirm);
        if (ruleChange) { Tap(s, InputAction.Down); Tap(s, InputAction.Confirm); Tap(s, InputAction.Confirm); }
        Tap(s, InputAction.Confirm);
        Assert.Equal(FreeDuelStep.Music, s.Step);
        Tap(s, InputAction.Down); Tap(s, InputAction.Confirm);
        Assert.Equal(FreeDuelStep.Confirm, s.Step);
        Tap(s, InputAction.Back);
        Assert.Equal((FreeDuelStep.Music, 1), (s.Step, s.Focus));
        Tap(s, InputAction.Back);
        Assert.Equal(expected, s.Step);
    }

    [Theory]
    [InlineData(FreeDuelStep.Format, 1)]
    [InlineData(FreeDuelStep.Format, 2)]
    [InlineData(FreeDuelStep.Ending, 1)]
    [InlineData(FreeDuelStep.TimeLimit, 1)]
    public void RowsThePcCannotPlayShowANoticeAndKeepFocus(FreeDuelStep step, int row)
    {
        var s = new FreeDuelSettingsSession(MusicRows);
        if (step != FreeDuelStep.Format) Tap(s, InputAction.Confirm);
        if (step == FreeDuelStep.TimeLimit) { Tap(s, InputAction.Confirm); Tap(s, InputAction.Down); Tap(s, InputAction.Confirm); Tap(s, InputAction.Confirm); }
        Assert.Equal(step, s.Step);
        for (int i = 0; i < row; i++) Tap(s, InputAction.Down);
        Assert.Equal(FreeDuelSettingsOutcome.Notice, Tap(s, InputAction.Confirm));
        Tap(s, InputAction.Down); // ignored while the notice is open
        Tap(s, InputAction.Back);
        Assert.Equal((step, row, false), (s.Step, s.Focus, s.NoticeOpen));
    }

    [Fact]
    public void MusicListScrollsOnlyPastTheShownRowsAndWrapsToEitherPage()
    {
        var s = new FreeDuelSettingsSession(MusicRows, 6);
        Tap(s, InputAction.Confirm); Tap(s, InputAction.Confirm); Tap(s, InputAction.Confirm);
        Assert.Equal(FreeDuelStep.Music, s.Step);
        for (int i = 0; i < 5; i++) Tap(s, InputAction.Down);
        Assert.Equal((5, 0), (s.Focus, s.Top));
        Tap(s, InputAction.Down);
        Assert.Equal((6, 1), (s.Focus, s.Top));
        Tap(s, InputAction.Up);
        Assert.Equal((5, 1), (s.Focus, s.Top));
        for (int i = 0; i < 6; i++) Tap(s, InputAction.Up);
        Assert.Equal((MusicRows - 1, MusicRows - 6), (s.Focus, s.Top));
        Tap(s, InputAction.Down);
        Assert.Equal((0, 0), (s.Focus, s.Top));
    }

    [Fact]
    public void EmbeddedProfileMatchesTheSessionSteps()
    {
        var profile = FreeDuelSettingsProfile.LoadEmbedded();
        Assert.Equal(MusicRows, profile.StepProfile(FreeDuelStep.Music).Rows.Length);
        Assert.Equal([3, 2, 2, 2, 5], new[] { FreeDuelStep.Format, FreeDuelStep.Ending, FreeDuelStep.Rules, FreeDuelStep.LifePoints, FreeDuelStep.TimeLimit }
            .Select(step => profile.StepProfile(step).Rows.Length));
        Assert.Equal(6, profile.VisibleRows);
        Assert.Empty(profile.StepProfile(FreeDuelStep.Confirm).Rows);
        Assert.All(profile.AssetKeys(), key => Assert.True(key.StartsWith("title.flow.", StringComparison.Ordinal) || key.StartsWith("title.eboot.", StringComparison.Ordinal)));
    }

    [Fact]
    public void BackFromTheFirstStepClosesAndResetForgetsEverything()
    {
        var s = new FreeDuelSettingsSession(MusicRows);
        Assert.Equal(FreeDuelSettingsOutcome.Close, Tap(s, InputAction.Back));
        Tap(s, InputAction.Confirm); Tap(s, InputAction.Confirm);
        s.Reset();
        Assert.Equal((FreeDuelStep.Format, 0), (s.Step, s.Focus));
        Assert.Null(s.Chosen(FreeDuelStep.Format));
    }
}
