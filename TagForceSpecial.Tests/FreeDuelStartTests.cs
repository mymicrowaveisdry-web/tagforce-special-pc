using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.FreeDuel;

namespace TagForceSpecial.Tests;

public sealed class FreeDuelStartTests
{
    [Theory]
    [InlineData(FreeDuelHand.Rock, FreeDuelHand.Rock, FreeDuelRpsResult.Draw)]
    [InlineData(FreeDuelHand.Scissors, FreeDuelHand.Scissors, FreeDuelRpsResult.Draw)]
    [InlineData(FreeDuelHand.Paper, FreeDuelHand.Paper, FreeDuelRpsResult.Draw)]
    [InlineData(FreeDuelHand.Rock, FreeDuelHand.Scissors, FreeDuelRpsResult.Win)]
    [InlineData(FreeDuelHand.Scissors, FreeDuelHand.Paper, FreeDuelRpsResult.Win)]
    [InlineData(FreeDuelHand.Paper, FreeDuelHand.Rock, FreeDuelRpsResult.Win)]
    [InlineData(FreeDuelHand.Rock, FreeDuelHand.Paper, FreeDuelRpsResult.Lose)]
    [InlineData(FreeDuelHand.Scissors, FreeDuelHand.Rock, FreeDuelRpsResult.Lose)]
    [InlineData(FreeDuelHand.Paper, FreeDuelHand.Scissors, FreeDuelRpsResult.Lose)]
    public void ResolvesEveryHandPair(FreeDuelHand player, FreeDuelHand opponent, FreeDuelRpsResult result) =>
        Assert.Equal(result, FreeDuelStartSession.Outcome(player, opponent));

    [Fact]
    public void TieDealsAgainAndBackReturnsToStartFocus()
    {
        var session = AtStart(new Rolls((int)FreeDuelHand.Paper));
        Tap(session, InputAction.Confirm);
        Assert.Equal(FreeDuelStartStep.Reveal, session.Start!.Step);
        session.Update(FreeDuelStartSession.RevealHold);
        Assert.Equal(FreeDuelStartStep.Choose, session.Start.Step);

        Tap(session, InputAction.Back);
        Assert.Null(session.Start);
        Assert.Equal(FreeDuelVersusFocus.Start, session.Versus!.Focus);
        Assert.Null(session.TakeReadyDuel());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WinnerChoosesOrderAndLaunchRequestIsConsumedOnce(bool first)
    {
        var session = AtStart(new Rolls((int)FreeDuelHand.Rock));
        Tap(session, InputAction.Confirm);
        session.Update(FreeDuelStartSession.RevealHold);
        Assert.Equal(FreeDuelStartStep.ChooseOrder, session.Start!.Step);
        Assert.True(session.Start.FirstFocused);
        if (!first) Tap(session, InputAction.Down);

        Tap(session, InputAction.Confirm);
        Assert.Equal(FreeDuelStartStep.Intro, session.Start.Step);
        Assert.Null(session.TakeReadyDuel());
        session.Update(FreeDuelStartSession.IntroHold);
        Assert.Equal((bool?)first, session.TakeReadyDuel());
        Assert.Null(session.Start);
        Assert.Null(session.TakeReadyDuel());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void OpponentChoosesOwnOrderAfterWinning(int roll, bool playerFirst)
    {
        var session = AtStart(new Rolls((int)FreeDuelHand.Scissors, roll));
        Tap(session, InputAction.Confirm);
        session.Update(FreeDuelStartSession.RevealHold);
        Assert.Equal(FreeDuelStartStep.OpponentChoosing, session.Start!.Step);
        session.Update(FreeDuelStartSession.ChoosingHold);
        Assert.Equal(FreeDuelStartStep.Intro, session.Start.Step);
        session.Update(FreeDuelStartSession.IntroHold);
        Assert.Equal((bool?)playerFirst, session.TakeReadyDuel());
    }

    private static FreeDuelSession AtStart(Random random)
    {
        var session = new FreeDuelSession(new(44), "PLAYER", "DECK", random: random);
        for (int i = 0; i < 5; i++) Tap(session, InputAction.Confirm);
        Tap(session, InputAction.Right);
        Tap(session, InputAction.Confirm);
        return session;
    }

    private static void Tap(FreeDuelSession session, InputAction action) => session.Handle(new(action, action));

    private sealed class Rolls(params int[] values) : Random
    {
        private readonly Queue<int> rolls = new(values);

        public override int Next(int maxValue)
        {
            int roll = rolls.Dequeue();
            Assert.InRange(roll, 0, maxValue - 1);
            return roll;
        }
    }
}
