namespace TagForceSpecial.Core;

/// <summary>Identifies a step pause using the accepted revision, without owning engine state or policy.</summary>
internal readonly record struct EffectStepCursor(string EffectId, int LinkNumber, int StepIndex, long AcceptedRevision);

/// <summary>Resolves card selections across hand, deck and recovery sources, returning a pause or the remaining command.</summary>
internal static class CardSelectionSteps
{
    internal static bool Handles(EffectStep step) => step is AddFromFaceUpExtraDeckStep or AddFromGraveyardToHandStep
        or AddFromDeckOrGraveyardStep or AddFromDeckToHandStep or SendFromDeckToGraveyardStep
        or ChooseFromOpponentHandStep or TakeExcavatedStep or OpponentSendsFromDeckStep;

    internal static (DuelState State, PendingDefinitionChoice? Pause, DuelCommand? Choice) Resolve(
        DuelState state, PlayerId controller, EffectStep step, EffectStepCursor cursor, DuelCommand? choice,
        IReadOnlyDictionary<uint, DuelCardRules> rules, Func<CardInstance, CardFilter, bool, bool> matches, List<DuelEvent> events)
    {
        PendingDefinitionCardChoice Pause(IEnumerable<CardInstance> options, PlayerId? chooser = null) =>
            new(checked(cursor.AcceptedRevision + 1), chooser ?? controller, cursor.EffectId, cursor.LinkNumber, cursor.StepIndex, options);
        void Skip() => events.Add(new ChainLinkSkipped(cursor.LinkNumber, ChainSkipReason.NoMatchingCard));
        var opponent = controller == PlayerId.One ? PlayerId.Two : PlayerId.One;
        if (step is ChooseFromOpponentHandStep fromHand)
        {
            var options = state.GetPlayer(opponent).Hand.Where(c => fromHand.Filter is null || matches(c, fromHand.Filter, false)).ToArray();
            if (options.Length == 0) return (state, null, choice);
            if (choice is not ChooseDefinitionCard picked) return (state, Pause(options), choice);
            return (CardZoneTransitions.MoveFromOpponentHand(state, controller,
                options.FirstOrDefault(c => c.InstanceId == picked.CardInstanceId) ?? options[0], fromHand.To, cursor.EffectId, events), null, null);
        }
        if (step is TakeExcavatedStep excavated)
        {
            var deck = state.GetPlayer(controller).Deck;
            var options = (state.Bookkeeping.LastRandom?.Excavated ?? []).Where(c => deck.Contains(c) &&
                (excavated.Filter is null || matches(c, excavated.Filter, false))).ToArray();
            if (options.Length == 0) return (state, null, choice);
            if (choice is not ChooseDefinitionCard picked) return (state, Pause(options), choice);
            return (CardZoneTransitions.TakeFromDeck(state, controller,
                options.FirstOrDefault(c => c.InstanceId == picked.CardInstanceId) ?? options[0], cursor.EffectId, events), null, null);
        }
        if (step is OpponentSendsFromDeckStep send)
        {
            var options = state.GetPlayer(opponent).Deck.Where(c => matches(c, send.Filter, false)).ToArray();
            if (options.Length == 0)
            {
                events.Add(new CardsLookedAt(controller, opponent, state.GetPlayer(opponent).Deck, cursor.EffectId));
                Skip(); return (state, null, choice);
            }
            if (choice is not ChooseDefinitionCard picked) return (state, Pause(options, opponent), choice);
            var sentCard = options.FirstOrDefault(c => c.InstanceId == picked.CardInstanceId) ?? options[0];
            return (CardZoneTransitions.MoveChosenDeckCard(state, opponent, sentCard.InstanceId, false, cursor.EffectId, events), null, null);
        }
        if (step is AddFromDeckToHandStep or SendFromDeckToGraveyardStep)
        {
            if (choice is ChooseDefinitionCard picked)
                return (CardZoneTransitions.MoveChosenDeckCard(state, controller, picked.CardInstanceId,
                    step is AddFromDeckToHandStep, cursor.EffectId, events), null, null);
            var filter = step is AddFromDeckToHandStep search ? search.Filter : ((SendFromDeckToGraveyardStep)step).Filter;
            var options = state.GetPlayer(controller).Deck.Where(c => matches(c, filter, false)).ToArray();
            if (options.Length == 0) { Skip(); return (state, null, choice); }
            return (state, Pause(options), choice);
        }

        var owned = state.GetPlayer(controller);
        var candidates = step switch
        {
            AddFromFaceUpExtraDeckStep extra => owned.FaceUpExtraDeck.Where(c => matches(c, extra.Filter, false)),
            AddFromGraveyardToHandStep grave => owned.Graveyard.Where(c => matches(c, grave.Filter, true)),
            AddFromDeckOrGraveyardStep either => owned.Deck.Where(c => matches(c, either.Filter, false))
                .Concat(owned.Graveyard.Where(c => matches(c, either.Filter, true))),
            _ => throw new InvalidOperationException("Unsupported deck selection step.")
        };
        var available = candidates.ToArray();
        if (available.Length == 0) { Skip(); return (state, null, choice); }
        if (choice is not ChooseDefinitionCard taken) return (state, Pause(available), choice);
        var card = available.FirstOrDefault(c => c.InstanceId == taken.CardInstanceId) ?? available[0];
        if (step is AddFromFaceUpExtraDeckStep)
        {
            state = state.With(player: owned.With(faceUpExtraDeck: owned.FaceUpExtraDeck.Where(c => c != card), hand: owned.Hand.Append(card)));
            events.Add(new CardAddedFromExtraDeck(controller, card, cursor.EffectId));
        }
        else if (step is AddFromGraveyardToHandStep || owned.Graveyard.Contains(card))
            state = CardZoneTransitions.MoveZoneCardToHand(state, new(controller, FieldLocation.Graveyard, -1, card, 0), cursor.EffectId, events, rules);
        else
            state = CardZoneTransitions.MoveChosenDeckCard(state, controller, card.InstanceId, true, cursor.EffectId, events);
        return (state, null, null);
    }
}
