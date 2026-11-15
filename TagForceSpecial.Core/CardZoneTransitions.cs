namespace TagForceSpecial.Core;

/// <summary>Applies immutable deck, hand and off-field transfers in event/RNG order; lock queries run synchronously on the current working state.</summary>
internal static class CardZoneTransitions
{
    internal static DuelState RemoveFromZone(DuelState state, FieldCardTarget target)
    {
        var player = state.GetPlayer(target.Player);
        return target.Location == FieldLocation.Graveyard
            ? state.With(player: player.With(graveyard: player.Graveyard.Where(c => c != target.Card)))
            : state.With(player: player.With(banished: player.Banished.Where(c => c != target.Card)));
    }

    internal static DuelState MoveZoneCardToHand(DuelState state, FieldCardTarget target, string effectId, List<DuelEvent> events, IReadOnlyDictionary<uint, DuelCardRules> rules)
    {
        state = RemoveFromZone(state, target);
        var owner = state.GetPlayer(target.Card.Owner);
        bool extra = rules.TryGetValue(target.Card.GameCardId, out var rule) && rule.AllowsExtraDeck;
        state = state.With(player: extra ? owner.With(extraDeck: owner.ExtraDeck.Append(target.Card)) : owner.With(hand: owner.Hand.Append(target.Card)));
        events.Add(new CardMovedToHand(owner.Id, target.Card, target.Location == FieldLocation.Graveyard ? CardZone.Graveyard : CardZone.Banished, effectId));
        return state;
    }

    internal static DuelState MoveZoneCard(DuelState state, FieldCardTarget target, bool toBanished, string effectId, List<DuelEvent> events, Func<DuelState, FieldCardTarget, bool> banishLocked)
    {
        if (toBanished && banishLocked(state, target)) return state;
        state = RemoveFromZone(state, target);
        var owner = state.GetPlayer(target.Card.Owner);
        state = state.With(player: toBanished ? owner.With(banished: owner.Banished.Append(target.Card))
            : owner.With(graveyard: owner.Graveyard.Append(target.Card)));
        events.Add(toBanished ? new CardBanishedFromGraveyard(owner.Id, target.Card, effectId) : new CardReturnedToGraveyard(owner.Id, target.Card, effectId));
        return state;
    }

    internal static DuelState ShuffleIntoDecks(DuelState state, FieldCardTarget[] targets, string effectId, List<DuelEvent> events,
        IReadOnlyDictionary<uint, DuelCardRules> rules, Func<DuelState, FieldCardTarget, bool> returnLocked)
    {
        HashSet<PlayerId> shuffled = [];
        foreach (var target in targets)
        {
            if (returnLocked(state, target)) continue;
            state = RemoveFromZone(state, target);
            var owner = state.GetPlayer(target.Card.Owner);
            bool extra = rules.TryGetValue(target.Card.GameCardId, out var rule) && rule.AllowsExtraDeck;
            state = state.With(player: extra ? owner.With(extraDeck: owner.ExtraDeck.Append(target.Card)) : owner.With(deck: owner.Deck.Append(target.Card)));
            if (!extra) shuffled.Add(owner.Id);
            events.Add(new CardShuffledIntoDeck(owner.Id, target.Card, effectId));
        }
        foreach (var owner in shuffled.Order()) state = ShuffleDeck(state, owner, effectId, events);
        return state;
    }

    internal static IEnumerable<PlayerId> DrawingPlayers(PlayerId controller, EffectPlayers players) => players switch
    {
        EffectPlayers.Self => [controller],
        EffectPlayers.Opponent => [controller == PlayerId.One ? PlayerId.Two : PlayerId.One],
        _ => [controller, controller == PlayerId.One ? PlayerId.Two : PlayerId.One]
    };

    internal static DuelState DrawCards(DuelState state, PlayerId playerId, int count, List<DuelEvent> events, Func<DuelState, PlayerId, bool> drawLocked)
    {
        for (int i = 0; i < count; i++)
        {
            if (drawLocked(state, playerId)) break;
            var player = state.GetPlayer(playerId);
            if (player.Deck.Count == 0)
            {
                var outcome = new DuelOutcome(playerId == PlayerId.One ? PlayerId.Two : PlayerId.One, playerId, DuelEndReason.UnableToDraw);
                events.Add(new DuelEnded(outcome));
                return state.With(outcome: outcome);
            }
            var card = player.Deck[0];
            state = state.With(player: player.With(deck: player.Deck.Skip(1), hand: player.Hand.Append(card)));
            events.Add(new CardDrawn(playerId, card));
        }
        return state;
    }

    internal static DuelState MillTop(DuelState state, PlayerId playerId, int count, string effectId, List<DuelEvent> events)
    {
        var player = state.GetPlayer(playerId);
        var sent = player.Deck.Take(count).ToArray();
        state = state.With(player: player.With(deck: player.Deck.Skip(sent.Length)));
        foreach (var card in sent)
        {
            var owner = state.GetPlayer(card.Owner);
            state = state.With(player: owner.With(graveyard: owner.Graveyard.Append(card)));
            events.Add(new CardSentFromDeckToGraveyard(playerId, card, effectId));
        }
        return state;
    }

    internal static DuelState MoveChosenDeckCard(DuelState state, PlayerId playerId, int instanceId, bool toHand, string effectId,
        List<DuelEvent> events)
    {
        var player = state.GetPlayer(playerId);
        var card = player.Deck.Single(c => c.InstanceId == instanceId);
        player = player.With(deck: player.Deck.Where(c => c != card));
        if (toHand)
        {
            state = state.With(player: player.With(hand: player.Hand.Append(card)));
            events.Add(new CardSearched(playerId, card, effectId));
        }
        else
        {
            state = state.With(player: player.With(graveyard: player.Graveyard.Append(card)));
            events.Add(new CardSentFromDeckToGraveyard(playerId, card, effectId));
        }
        return ShuffleDeck(state, playerId, effectId, events);
    }

    internal static DuelState ShuffleDeck(DuelState state, PlayerId playerId, string effectId, List<DuelEvent> events)
    {
        var random = state.Random ?? throw new InvalidOperationException("Shuffle steps require a seeded engine.");
        var player = state.GetPlayer(playerId);
        var (deck, next) = random.Shuffle(player.Deck);
        events.Add(new DeckShuffled(playerId, effectId));
        return state.With(player: player.With(deck: deck)).WithBookkeeping(state.Bookkeeping with { Random = next });
    }
    internal static DuelState MoveFromOpponentHand(DuelState state, PlayerId controller, CardInstance card, HandMove move, string effectId, List<DuelEvent> events)
    {
        var opponent = state.GetPlayer(controller == PlayerId.One ? PlayerId.Two : PlayerId.One);
        if (!opponent.Hand.Contains(card)) return state;
        if (move == HandMove.Discard)
        {
            events.Add(new CardDiscarded(opponent.Id, card) { EffectController = controller, EffectId = effectId });
            return state.With(player: opponent.With(hand: opponent.Hand.Where(c => c != card), graveyard: opponent.Graveyard.Append(card)));
        }
        events.Add(new CardShuffledIntoDeck(opponent.Id, card, effectId));
        state = state.With(player: opponent.With(hand: opponent.Hand.Where(c => c != card), deck: opponent.Deck.Append(card)));
        return ShuffleDeck(state, opponent.Id, effectId, events);
    }

    internal static DuelState TakeFromDeck(DuelState state, PlayerId playerId, CardInstance card, string effectId, List<DuelEvent> events)
    {
        var player = state.GetPlayer(playerId);
        events.Add(new CardSearched(playerId, card, effectId));
        return state.With(player: player.With(deck: player.Deck.Where(c => c != card), hand: player.Hand.Append(card)));
    }
}
