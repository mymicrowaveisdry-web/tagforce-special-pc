namespace TagForceSpecial.Core;

/// <summary>Where a card that would go to the Graveyard goes instead.</summary>
public enum GraveyardDestination { Banished, Deck }

/// <summary>
/// "[Cards] sent to the Graveyard are [banished / shuffled into the Deck] instead". <paramref name="Owner"/> is whose Graveyard, relative to the
/// rule's controller; <paramref name="DestroyedByBattleOnly"/> limits it to monsters destroyed by battle.
/// </summary>
public sealed record GraveyardRedirect(GraveyardDestination To = GraveyardDestination.Banished, CardFilter? Cards = null,
    TargetSide Owner = TargetSide.Either, bool DestroyedByBattleOnly = false);

/// <summary>A redirect that applies while its source is face-up and its effect applies.</summary>
public sealed record GraveyardRedirectProfile(string EffectId, uint CardId, GraveyardRedirect Rule);

/// <summary>Applies a redirect until the end of this turn, relative to the activating player.</summary>
public sealed record ApplyGraveyardRedirectStep(GraveyardRedirect Rule) : EffectStep;

internal sealed record TimedGraveyardRedirect(PlayerId Controller, int TurnNumber, GraveyardRedirect Rule);

public sealed partial class DuelEngine
{
    private IEnumerable<(PlayerId Controller, GraveyardRedirect Rule)> ActiveRedirects(DuelState state)
    {
        foreach (var timed in state.Bookkeeping.TimedRedirects.Where(t => t.TurnNumber == state.TurnNumber))
            yield return (timed.Controller, timed.Rule);
        if (redirectProfiles.Count == 0) yield break;
        foreach (var (binding, monster) in FieldMonsters(state))
            if (monster.Position != BattlePosition.FaceDownDefense && !state.EventEffects.NegatedMonsters.Contains(binding) &&
                redirectProfiles.TryGetValue(monster.Card.GameCardId, out var monsterProfiles))
                foreach (var profile in monsterProfiles) yield return (binding.Player, profile.Rule);
        bool spellsNegated = IsSpellsNegated(state);
        foreach (var (player, _, field) in ActiveCards(state))
            if (!(spellsNegated && IsSpellCard(field)) && redirectProfiles.TryGetValue(field.Card.GameCardId, out var cardProfiles))
                foreach (var profile in cardProfiles) yield return (player, profile.Rule);
    }

    /// <summary>Where this card goes instead of the Graveyard now, or null when it goes there.</summary>
    private GraveyardDestination? RedirectFor(DuelState state, CardInstance card, bool destroyedByBattle)
    {
        foreach (var (controller, rule) in ActiveRedirects(state))
            if ((rule.Owner == TargetSide.Either || (rule.Owner == TargetSide.Own) == (card.Owner == controller)) &&
                (!rule.DestroyedByBattleOnly || destroyedByBattle) &&
                (rule.Cards is null || MatchesFilter(card, rule.Cards, inGraveyard: true)))
                return rule.To;
        return null;
    }

    /// <summary>The card an event sends to the Graveyard, if it is that kind of event.</summary>
    private static CardInstance? GraveyardArrival(DuelEvent emitted) => emitted switch
    {
        MonsterDestroyed e => e.Card,
        MonsterTributed e => e.Monster.Card,
        SpellTrapDestroyed { IsPendulumZone: false } e => e.Card,
        SpellSentToGraveyard { IsPendulumZone: false } e => e.Card,
        ChainTrapSentToGraveyard e => e.Card,
        SummonMaterialSentToGraveyard e => e.Material.Card,
        FusionMaterialSentToGraveyard e => e.Material.Card,
        RitualMaterialTributed e => e.Material.Card,
        XyzMaterialSentToGraveyard e => e.Material,
        CardDiscarded e => e.Card,
        ActivationCostPaid e => e.Card,
        TriggerCostPaid e => e.DiscardedCard,
        CardSentFromDeckToGraveyard e => e.Card,
        SummonNegated e => e.Attempt.Card,
        _ => null
    };

    /// <summary>
    /// Moves cards that just reached a Graveyard to where an active redirect sends them, and marks their events with the new destination so
    /// "sent to the Graveyard" effects do not apply; destruction still counts. A redirect whose source left at the same time no longer applies.
    /// </summary>
    private DuelState ApplyGraveyardRedirects(DuelState next, List<DuelEvent> emitted)
    {
        next = ApplyBanishOnLeave(next, emitted);
        if (redirectProfiles.Count == 0 && next.Bookkeeping.TimedRedirects.Count == 0) return next;
        Dictionary<CardInstance, GraveyardDestination> moved = [];
        HashSet<PlayerId> shuffle = [];
        for (int i = 0; i < emitted.Count; i++)
        {
            var e = emitted[i];
            if (e.FieldDestination is FieldCardDestination.Removed or FieldCardDestination.FaceUpExtraDeck or FieldCardDestination.Banished or FieldCardDestination.Deck ||
                GraveyardArrival(e) is not { } card) continue;
            if (!moved.TryGetValue(card, out var destination))
            {
                var owner = next.GetPlayer(card.Owner);
                if (!owner.Graveyard.Contains(card) || RedirectFor(next, card, e is MonsterDestroyed { ByBattle: true }) is not { } to) continue;
                destination = to;
                moved[card] = to;
                next = next.With(player: to == GraveyardDestination.Banished
                    ? owner.With(graveyard: owner.Graveyard.Where(c => c != card), banished: owner.Banished.Append(card))
                    : owner.With(graveyard: owner.Graveyard.Where(c => c != card), deck: owner.Deck.Append(card)));
                if (to == GraveyardDestination.Deck) shuffle.Add(owner.Id);
            }
            emitted[i] = e with { FieldDestination = destination == GraveyardDestination.Banished ? FieldCardDestination.Banished : FieldCardDestination.Deck };
        }
        List<DuelEvent> shuffled = [];
        foreach (var player in shuffle.Order()) next = ShuffleDeck(next, player, "graveyard-redirect", shuffled);
        emitted.AddRange(shuffled);
        return next;
    }

    /// <summary>A cost that sends cards to the Graveyard cannot be paid while every card it could use would be redirected.</summary>
    private bool GraveyardCostBlocked(DuelState state, PlayerId playerId, CardInstance card, EffectDefinition definition)
    {
        if (redirectProfiles.Count == 0 && state.Bookkeeping.TimedRedirects.Count == 0) return false;
        if (definition.Cost is SendSelfCost { Destination: SelfDestination.Graveyard }) return RedirectFor(state, card, false) is not null;
        if (definition.Cost is not DiscardCost discard) return false;
        return state.GetPlayer(playerId).Hand.Count(c => c != card && DiscardQualifies(c, discard.Filter) && RedirectFor(state, c, false) is null) < discard.Count;
    }

    private static DuelState AddTimedRedirect(DuelState state, PlayerId controller, GraveyardRedirect rule) =>
        state.WithBookkeeping(state.Bookkeeping with { TimedRedirects = Array.AsReadOnly(state.Bookkeeping.TimedRedirects
            .Where(t => t.TurnNumber == state.TurnNumber).Append(new(controller, state.TurnNumber, rule)).ToArray()) });
}
