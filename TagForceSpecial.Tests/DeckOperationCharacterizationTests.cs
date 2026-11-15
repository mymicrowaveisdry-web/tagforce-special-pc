using TagForceSpecial.Core;
using Xunit.Abstractions;
using static TagForceSpecial.Tests.CountQueryScenario;

namespace TagForceSpecial.Tests;

/// <summary>Deck and zone operations with observable choice continuations, event order and RNG state.</summary>
public sealed class DeckOperationCharacterizationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("deck-hand")]
    [InlineData("deck-grave")]
    [InlineData("grave-hand")]
    [InlineData("deck-or-grave")]
    [InlineData("two-choices")]
    public void SelectionsResumeTheSameLinkAndRunFollowingStepsExactlyOnce(string route)
    {
        EffectStep first = route switch
        {
            "deck-grave" => new SendFromDeckToGraveyardStep(new AnyCardFilter()),
            "grave-hand" => new AddFromGraveyardToHandStep(new AnyCardFilter()),
            "deck-or-grave" => new AddFromDeckOrGraveyardStep(new AnyCardFilter()),
            _ => new AddFromDeckToHandStep(new AnyCardFilter())
        };
        EffectStep[] steps = route == "two-choices"
            ? [first, new SendFromDeckToGraveyardStep(new AnyCardFilter()), new GainLifePointsStep(100)]
            : [first, new GainLifePointsStep(100)];
        var s = new CountQueryScenario(new("deck-operations", Probe, EffectKind.NormalSpell, steps),
            preparation: [new MillTopOfDeckStep(2)], output: new OperationOutput(output), random: true, mixedSpells: true);
        s.EnterMain();
        if (route is "grave-hand" or "deck-or-grave") { s.Cast(Preparation); s.Resolve(); }
        var before = s.Engine.State;
        s.Events.Clear(); s.Cast();
        while (s.Engine.State.PendingChain is { NextLink: 0 } chain)
            s.Execute(new PassChainResponse(chain.Responder, chain.DecisionId));
        s.Execute(new ResolveNextChainLink(PlayerId.One, s.Engine.State.PendingChain!.DecisionId));
        var choice = s.Engine.State.PendingChain!.DefinitionCardChoice!;
        Assert.Equal(1, choice.LinkNumber);
        Assert.Equal(0, choice.StepIndex);
        Assert.Empty(s.Events.OfType<LifePointsGained>());
        s.Execute(new ChooseDefinitionCard(choice.Player, choice.DecisionId - 1, choice.Options[0].InstanceId), DuelError.StaleDecision);
        s.Execute(new ChooseDefinitionCard(choice.Player, choice.DecisionId, 999999), DuelError.InvalidDecisionChoice);
        if (route == "deck-or-grave")
        {
            var player = before.GetPlayer(PlayerId.One);
            Assert.Equal(player.Deck.Concat(player.Graveyard).Select(c => c.InstanceId), choice.Options.Select(c => c.InstanceId));
            s.Execute(new ChooseDefinitionCard(choice.Player, choice.DecisionId, player.Graveyard[0].InstanceId));
        }
        s.Resolve();
        Assert.Null(s.Engine.State.PendingChain);
        Assert.Single(s.Events.OfType<LifePointsGained>());
        Assert.Single(s.Events.OfType<ChainLinkAdded>());
        Assert.Single(s.Events.OfType<ChainLinkResolved>());
        Assert.Equal(8100, s.Engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(8000, before.GetPlayer(PlayerId.One).LifePoints);
        int shuffles = route is "grave-hand" or "deck-or-grave" ? 0 : route == "two-choices" ? 2 : 1;
        Assert.Equal(shuffles, s.Events.OfType<DeckShuffled>().Count());
        if (shuffles > 0) Assert.NotEqual(before.Random, s.Engine.State.Random);
    }

    [Fact]
    public void DrawMillAndShuffleKeepPlayerAndEventOrder()
    {
        var s = new CountQueryScenario(new("deck-operations", Probe, EffectKind.NormalSpell,
            [new DrawCardsStep(2, EffectPlayers.Both), new MillTopOfDeckStep(2), new ShuffleDeckStep()]),
            output: new OperationOutput(output), random: true);
        s.EnterMain(); s.Events.Clear(); s.Cast(); s.Resolve();
        Assert.Equal(new[] { PlayerId.One, PlayerId.One, PlayerId.Two, PlayerId.Two }, s.Events.OfType<CardDrawn>().Select(e => e.Player));
        Assert.Equal(2, s.Events.OfType<CardSentFromDeckToGraveyard>().Count());
        Assert.Single(s.Events.OfType<DeckShuffled>());
        var operations = s.Events.Where(e => e is CardDrawn or CardSentFromDeckToGraveyard or DeckShuffled).ToArray();
        Assert.All(operations.Take(4), e => Assert.IsType<CardDrawn>(e));
        Assert.All(operations.Skip(4).Take(2), e => Assert.IsType<CardSentFromDeckToGraveyard>(e));
        Assert.IsType<DeckShuffled>(operations[6]);
    }

    [Fact]
    public void ChosenZoneCardsReturnBeforeTheirOwnersDeckIsShuffled()
    {
        var s = new CountQueryScenario(new("deck-operations", Probe, EffectKind.NormalSpell,
            [new ShuffleTargetsIntoDeckStep()], fieldTargets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new AnyCardFilter(), 1, 2)]),
            preparation: [new MillTopOfDeckStep(2)], output: new OperationOutput(output), random: true, mixedSpells: true);
        s.EnterMain(); s.Cast(Preparation); s.Resolve(); s.Events.Clear(); s.Cast(); s.Resolve();
        Assert.Equal(2, s.Events.OfType<CardShuffledIntoDeck>().Count());
        Assert.Single(s.Events.OfType<DeckShuffled>());
        var moves = s.Events.Where(e => e is CardShuffledIntoDeck or DeckShuffled).ToArray();
        Assert.IsType<CardShuffledIntoDeck>(moves[0]); Assert.IsType<CardShuffledIntoDeck>(moves[1]);
        Assert.IsType<DeckShuffled>(moves[2]);
    }

    private sealed class OperationOutput(ITestOutputHelper output) : ITestOutputHelper
    {
        public void WriteLine(string message) => output.WriteLine(message.Replace("COUNT_TRACE ", "OPERATION_TRACE ", StringComparison.Ordinal));
        public void WriteLine(string format, params object[] args) => WriteLine(string.Format(format, args));
    }
}
