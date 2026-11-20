using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.FreeDuel;

namespace TagForceSpecial.Tests;

public sealed class FreeDuelVersusTests
{
    private static void Tap(FreeDuelSession s, InputAction action) => s.Handle(new(action, action));
    private static FreeDuelVersusSession.Outcome TapVersus(FreeDuelVersusSession s, InputAction action) => s.Handle(new(action, action));

    private static FreeDuelSession AtVersus()
    {
        var s = new FreeDuelSession(new FreeDuelSettingsSession(44), "PLAYER", "MY DECK");
        for (int i = 0; i < 5; i++) Tap(s, InputAction.Confirm); // Normal Duel, Single, standard rules, music, Next
        return s;
    }

    [Fact]
    public void NextOnTheConfirmationOpensVersusWithTheOpponentNameFocused()
    {
        var s = AtVersus();
        Assert.NotNull(s.Versus);
        Assert.Equal(FreeDuelVersusFocus.OpponentName, s.Versus!.Focus);
        Assert.Equal(8000, s.Settings.Settings!.StartingLifePoints);
    }

    [Fact]
    public void DownCyclesNameDecksStartAndRightLeftJumpToAndFromStart()
    {
        var v = new FreeDuelVersusSession();
        var seen = new List<FreeDuelVersusFocus>();
        for (int i = 0; i < 4; i++) { TapVersus(v, InputAction.Down); seen.Add(v.Focus); }
        Assert.Equal([FreeDuelVersusFocus.OpponentDeck, FreeDuelVersusFocus.PlayerDeck, FreeDuelVersusFocus.Start, FreeDuelVersusFocus.OpponentName], seen);
        TapVersus(v, InputAction.Up);
        Assert.Equal(FreeDuelVersusFocus.Start, v.Focus);
        TapVersus(v, InputAction.Left);
        Assert.Equal(FreeDuelVersusFocus.OpponentName, v.Focus);
        TapVersus(v, InputAction.Down); TapVersus(v, InputAction.Right);
        Assert.Equal(FreeDuelVersusFocus.Start, v.Focus);
    }

    [Theory]
    [InlineData(0, FreeDuelVersusSession.Outcome.RandomCharacter, false)]
    [InlineData(1, FreeDuelVersusSession.Outcome.None, false)]
    [InlineData(2, FreeDuelVersusSession.Outcome.None, true)]
    [InlineData(3, FreeDuelVersusSession.Outcome.None, false)]
    public void SquarePicksARandomCharacterOnTheNameAndShowsANoticeOnThePlayerDeck(int downs, FreeDuelVersusSession.Outcome expected, bool notice)
    {
        var v = new FreeDuelVersusSession();
        for (int i = 0; i < downs; i++) TapVersus(v, InputAction.Down);
        Assert.Equal(expected, TapVersus(v, InputAction.Switch));
        Assert.Equal(notice, v.NoticeOpen);
    }

    [Fact]
    public void ConfirmOnTheOpponentNameOpensThePicker() => Assert.Equal(FreeDuelVersusSession.Outcome.OpenPicker, TapVersus(new FreeDuelVersusSession(), InputAction.Confirm));

    [Fact]
    public void ConfirmShowsANoticeThatKeepsFocusAndBackReturnsToTheResetFirstStep()
    {
        var s = AtVersus();
        Tap(s, InputAction.Right); Tap(s, InputAction.Up); Tap(s, InputAction.Switch);
        Assert.True(s.NoticeOpen);
        Tap(s, InputAction.Back);
        Assert.Equal((false, FreeDuelVersusFocus.PlayerDeck), (s.NoticeOpen, s.Versus!.Focus));
        Assert.False(s.Handle(new(InputAction.Back, InputAction.Back)));
        Assert.Null(s.Versus);
        Assert.Equal((FreeDuelStep.Format, 0), (s.Settings.Step, s.Settings.Focus));
        Assert.Null(s.Settings.Chosen(FreeDuelStep.Format));
        Assert.True(s.Handle(new(InputAction.Back, InputAction.Back)));
    }

    [Fact]
    public void EmbeddedVersusProfileHasBothSidesAndAllFocusFrames()
    {
        var p = FreeDuelVersusProfile.LoadEmbedded();
        Assert.Equal(5, p.PlayerLpDigits.Length);
        Assert.Equal(5, p.OpponentLpDigits.Length);
        Assert.All(new[] { p.Focus.OpponentName, p.Focus.OpponentDeck, p.Focus.PlayerDeck }, frame => Assert.Equal(9, frame.Length));
        Assert.Equal(1f / 32, p.DigitStep);
        Assert.StartsWith("PC: ", FreeDuelVersusRenderer.PcDeckLabel);
    }
}
