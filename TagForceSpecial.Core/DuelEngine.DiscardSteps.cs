namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private static IEnumerable<PlayerId> DiscardingPlayers(DuelState state, PlayerId controller, EffectPlayers players) =>
        players == EffectPlayers.Both ? [state.ActivePlayer, Other(state.ActivePlayer)] : DrawingPlayers(controller, players);

    /// <summary>Projects fixed draws and hand costs so a discard effect cannot start without enough cards to discard.</summary>
    private bool ChosenDiscardsAvailable(EffectDefinition definition, PlayerId controller, CardInstance source)
    {
        if (!definition.Steps.Any(s => s is DiscardCardsStep)) return true;
        Dictionary<PlayerId, int> counts = State.Players.ToDictionary(p => p.Id, p => p.Hand.Count);
        if (State.GetPlayer(controller).Hand.Contains(source) &&
            (rules[source.GameCardId].Category != CardCategory.Monster || definition.Cost is SendSelfCost)) counts[controller]--;
        if (definition.Cost is DiscardCost cost) counts[controller] -= cost.Count;
        foreach (var step in definition.Steps)
            switch (step)
            {
                case DrawCardsStep draw:
                    foreach (var player in DrawingPlayers(controller, draw.Players)) counts[player] += draw.Count;
                    break;
                case AddFromDeckToHandStep or AddFromGraveyardToHandStep or AddFromDeckOrGraveyardStep:
                    counts[controller]++;
                    break;
                case DiscardCardsStep discard:
                    foreach (var player in DiscardingPlayers(State, controller, discard.Players))
                    {
                        int minimum = discard.Minimum ?? discard.Count;
                        if (counts[player] < minimum) return false;
                        if (discard.Filter is { } filter && State.GetPlayer(player).Hand.Count(c => c != source && MatchesFilter(c, filter)) < minimum)
                            return false;
                        counts[player] -= minimum;
                    }
                    break;
            }
        return true;
    }

    /// <summary>Collects private choices without moving cards, then publishes one simultaneous discard.</summary>
    private (DuelState State, PendingDefinitionChoice? Pause) ResolveChosenDiscards(DuelState state, ChainLink link,
        EffectDefinition definition, DiscardCardsStep step, int index, DuelCommand? answer, List<DuelEvent> events)
    {
        var picks = answer is FinishDefinitionCardChoice ? state.PendingChain!.DefinitionCardChoice!.Picks.ToArray()
            : answer is ChooseDefinitionCard chosen
            ? state.PendingChain!.DefinitionCardChoice!.Picks.Append(
                state.PendingChain.DefinitionCardChoice.Options.Single(c => c.InstanceId == chosen.CardInstanceId)).ToArray()
            : [];
        foreach (var playerId in DiscardingPlayers(state, link.Activation.Player, step.Players))
        {
            var player = state.GetPlayer(playerId);
            var eligible = player.Hand.Where(c => step.Filter is null || MatchesFilter(c, step.Filter)).ToArray();
            int needed = Math.Min(step.Count, eligible.Length);
            int selected = picks.Count(c => c.Owner == playerId);
            if (selected >= needed || answer is FinishDefinitionCardChoice) continue;
            return (state, new PendingDefinitionCardChoice(checked(State.Revision + 1), playerId, definition.EffectId,
                link.Number, index, eligible.Where(c => !picks.Contains(c)), picks, step.Minimum is { } min && selected >= min));
        }
        foreach (var playerId in DiscardingPlayers(state, link.Activation.Player, step.Players))
        {
            var discarded = picks.Where(c => c.Owner == playerId).ToArray();
            if (discarded.Length == 0) continue;
            var player = state.GetPlayer(playerId);
            state = state.With(player: player.With(hand: player.Hand.Where(c => !discarded.Contains(c)),
                graveyard: player.Graveyard.Concat(discarded)));
            foreach (var card in discarded) events.Add(new CardDiscarded(playerId, card) { EffectController = link.Activation.Player, EffectId = definition.EffectId });
        }
        return (state, null);
    }
}
