using TagForceSpecial.Core;
using Xunit.Abstractions;
using static TagForceSpecial.Tests.CountQueryScenario;

namespace TagForceSpecial.Tests;

/// <summary>Choice ownership and continuation behavior for private-hand, opponent-deck and excavated cards.</summary>
public sealed class CardSelectionCharacterizationTests(ITestOutputHelper output)
{
    private CountQueryScenario Scenario(params EffectStep[] steps) => new(
        new("card-selection", Probe, EffectKind.NormalSpell, steps), output: new SelectionOutput(output), random: true, mixedSpells: true);

    [Theory]
    [InlineData(HandMove.Discard)]
    [InlineData(HandMove.ShuffleIntoDeck)]
    public void OpponentHandChoicesKeepCardOwnerAndEffectController(HandMove move)
    {
        var s = Scenario(new ChooseFromOpponentHandStep(new AnyCardFilter(), move), new GainLifePointsStep(100));
        s.EnterMain(); var original = s.Engine.State.GetPlayer(PlayerId.Two).Hand[0]; s.Cast(); s.Resolve();
        Assert.DoesNotContain(original, s.Engine.State.GetPlayer(PlayerId.Two).Hand);
        Assert.Equal(8100, s.Engine.State.GetPlayer(PlayerId.One).LifePoints);
        if (move == HandMove.Discard)
        {
            Assert.Contains(original, s.Engine.State.GetPlayer(PlayerId.Two).Graveyard);
            Assert.Equal(PlayerId.One, Assert.Single(s.Events.OfType<CardDiscarded>()).EffectController);
        }
        else
        {
            Assert.Contains(original, s.Engine.State.GetPlayer(PlayerId.Two).Deck);
            Assert.Single(s.Events.OfType<DeckShuffled>());
        }
    }

    private static PendingDefinitionCardChoice Pause(CountQueryScenario s)
    {
        while (s.Engine.State.PendingChain is { NextLink: 0 } chain)
            s.Execute(new PassChainResponse(chain.Responder, chain.DecisionId));
        s.Execute(new ResolveNextChainLink(PlayerId.One, s.Engine.State.PendingChain!.DecisionId));
        return s.Engine.State.PendingChain!.DefinitionCardChoice!;
    }

    [Fact]
    public void OpponentChoosesTheirOwnDeckCardBeforeThatDeckIsShuffled()
    {
        var s = Scenario(new OpponentSendsFromDeckStep(new AnyCardFilter()), new GainLifePointsStep(100));
        s.EnterMain(); var top = s.Engine.State.GetPlayer(PlayerId.Two).Deck[0]; s.Cast();
        Assert.Equal(PlayerId.Two, Pause(s).Player);
        s.Resolve();
        Assert.Contains(top, s.Engine.State.GetPlayer(PlayerId.Two).Graveyard);
        Assert.Equal(PlayerId.Two, Assert.Single(s.Events.OfType<CardSentFromDeckToGraveyard>()).Player);
        Assert.Single(s.Events.OfType<DeckShuffled>());
        Assert.Equal(8100, s.Engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void NoOpponentDeckMatchRevealsConfirmationAndContinuesTheFollowingStep()
    {
        var s = Scenario(new OpponentSendsFromDeckStep(new NotFilter(new AnyCardFilter())), new GainLifePointsStep(100));
        s.EnterMain(); s.Cast(); s.Resolve();
        Assert.Single(s.Events.OfType<CardsLookedAt>());
        Assert.Equal(ChainSkipReason.NoMatchingCard, Assert.Single(s.Events.OfType<ChainLinkSkipped>()).Reason);
        Assert.Equal(35, s.Engine.State.GetPlayer(PlayerId.Two).Deck.Count);
        Assert.Equal(8100, s.Engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void ExcavatedChoiceKeepsDeckOrderAndDoesNotInventAShuffle()
    {
        var s = Scenario(new ExcavateStep(3), new TakeExcavatedStep(new AnyCardFilter()), new GainLifePointsStep(100));
        s.EnterMain(); var top = s.Engine.State.GetPlayer(PlayerId.One).Deck.Take(3).ToArray(); s.Cast();
        var pick = Pause(s);
        Assert.Equal(top, pick.Options);
        s.Execute(new ChooseDefinitionCard(pick.Player, pick.DecisionId, top[2].InstanceId)); s.Resolve();
        Assert.Contains(top[2], s.Engine.State.GetPlayer(PlayerId.One).Hand);
        Assert.DoesNotContain(top[2], s.Engine.State.GetPlayer(PlayerId.One).Deck);
        Assert.Empty(s.Events.OfType<DeckShuffled>());
        Assert.Equal(8100, s.Engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    private sealed class SelectionOutput(ITestOutputHelper output) : ITestOutputHelper
    {
        public void WriteLine(string message) => output.WriteLine(message.Replace("COUNT_TRACE ", "CARD_SELECTION_TRACE ", StringComparison.Ordinal));
        public void WriteLine(string format, params object[] args) => WriteLine(string.Format(format, args));
    }
}
