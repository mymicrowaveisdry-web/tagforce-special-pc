namespace TagForceSpecial.Core;

/// <summary>The activating player takes effect damage.</summary>
public sealed record TakeDamageStep(int Amount) : EffectStep;

/// <summary>A player discards 1 random card from their hand; <paramref name="Player"/> is relative to the activating player.</summary>
public sealed record DiscardRandomStep(TargetSide Player = TargetSide.Own) : EffectStep;

/// <summary>Narrows a <see cref="MonsterSummonedCondition"/> to a Tribute Summon with exactly this many Tributes.</summary>
public sealed record TributeCountCondition(int Tributes) : EffectCondition;

/// <summary>Narrows a <see cref="MonsterSummonedCondition"/> to an Xyz Summon.</summary>
public sealed record XyzSummonedCondition : EffectCondition;

/// <summary>Narrows an <see cref="OpponentAttackDeclaredCondition"/> to a direct attack.</summary>
public sealed record DirectAttackCondition : EffectCondition;

/// <summary>
/// A chain response to an activated card or effect that would inflict effect damage to this player; with
/// <paramref name="ByOpponent"/>, only one the opponent activated. Effects whose damage depends on a coin or die do not count.
/// </summary>
public sealed record RespondsToDamageCondition(bool ByOpponent = true) : EffectCondition;

/// <summary>While the card or effect this responded to resolves, its effect damage to the activating player goes to the opponent instead.</summary>
public sealed record RedirectRespondedDamageStep : EffectStep;

/// <summary>The activating card, now a monster on the field, is banished when it leaves the field.</summary>
public sealed record BanishSourceWhenItLeavesStep : EffectStep;

/// <summary>A monster that left the field was banished instead of staying where it went.</summary>
public sealed record CardBanishedOnLeavingField(PlayerId Player, CardInstance Card) : DuelEvent;

internal sealed record LeaveBanish(CardInstance Card, long PlacementId);

public sealed partial class DuelEngine
{
    /// <summary>Whether an effect activated by <paramref name="controller"/> has a step that inflicts effect damage to <paramref name="player"/>.</summary>
    private static bool WouldDamage(EffectDefinition definition, PlayerId controller, PlayerId player) => definition.Steps.Any(step => step switch
    {
        DealDamageStep or DamageByTargetAttackStep => controller != player,
        TakeDamageStep => controller == player,
        DamageBothPlayersStep => true,
        _ => false
    });

    private static DuelState RedirectRespondedDamage(DuelState state, PlayerId controller, ChainLink link)
    {
        if (link.Number < 2) return state;
        return state.WithBookkeeping(state.Bookkeeping with { TimedDamageRules = Array.AsReadOnly(state.Bookkeeping.TimedDamageRules
            .Where(t => t.TurnNumber == state.TurnNumber)
            .Append(new(controller, state.TurnNumber, null, new DamageRule(DamageRuleKind.RedirectToOpponent, DamageKinds.Effect), link.Number - 1)).ToArray()) });
    }

    /// <summary>Rules tied to a link of a chain end with that chain.</summary>
    private static DuelState DropLinkDamageRules(DuelState state) =>
        state.PendingChain is not null || !state.Bookkeeping.TimedDamageRules.Any(t => t.LinkNumber is not null) ? state
            : state.WithBookkeeping(state.Bookkeeping with { TimedDamageRules = Array.AsReadOnly(state.Bookkeeping.TimedDamageRules.Where(t => t.LinkNumber is null).ToArray()) });

    /// <summary>Drops the per-card limits a card has used, so it counts as a new card; limits by name are kept.</summary>
    private static DuelState ForgetUses(DuelState state, CardInstance card)
    {
        var books = state.Bookkeeping;
        state = state.WithBookkeeping(books with
        {
            ActivatedUses = Array.AsReadOnly(books.ActivatedUses.Where(u => u.InstanceId != card.InstanceId).ToArray()),
            LockUses = Array.AsReadOnly(books.LockUses.Where(u => u.InstanceId != card.InstanceId).ToArray())
        });
        return state.WithEventEffects(state.EventEffects.WithUses(state.EventEffects.UsedTriggers.Where(u => u.InstanceId != card.InstanceId)));
    }

    private static DuelState BanishSourceWhenItLeaves(DuelState state, ChainLink link)
    {
        var placed = FieldMonsters(state).Where(e => e.Binding.Card == link.Activation.Spell).Select(e => (long?)e.Binding.PlacementId).FirstOrDefault();
        if (placed is not long placement) return state;
        return state.WithBookkeeping(state.Bookkeeping with { BanishOnLeave = Array.AsReadOnly([.. state.Bookkeeping.BanishOnLeave, new LeaveBanish(link.Activation.Spell, placement)]) });
    }

    /// <summary>
    /// Banishes each marked monster that is no longer in its placement, from the Graveyard, hand or Deck it went to; an arrival in the
    /// Graveyard is re-marked so it does not count as sent there. A card that became Xyz Material stays attached.
    /// </summary>
    private static DuelState ApplyBanishOnLeave(DuelState next, List<DuelEvent> emitted)
    {
        var marked = next.Bookkeeping.BanishOnLeave;
        if (marked.Count == 0) return next;
        var onField = FieldMonsters(next).Select(e => (e.Binding.Card, e.Binding.PlacementId)).ToHashSet();
        var gone = marked.Where(m => !onField.Contains((m.Card, m.PlacementId))).ToArray();
        if (gone.Length == 0) return next;
        foreach (var entry in gone)
        {
            var card = entry.Card;
            var owner = next.GetPlayer(card.Owner);
            bool inGraveyard = owner.Graveyard.Contains(card);
            if (!inGraveyard && !owner.Hand.Contains(card) && !owner.Deck.Contains(card)) continue;
            next = next.With(player: owner.With(graveyard: owner.Graveyard.Where(c => c != card), hand: owner.Hand.Where(c => c != card),
                deck: owner.Deck.Where(c => c != card), banished: owner.Banished.Append(card)));
            if (inGraveyard)
                for (int i = 0; i < emitted.Count; i++)
                    if (GraveyardArrival(emitted[i]) == card && emitted[i].FieldDestination is not (FieldCardDestination.Removed or FieldCardDestination.FaceUpExtraDeck))
                        emitted[i] = emitted[i] with { FieldDestination = FieldCardDestination.Banished };
            emitted.Add(new CardBanishedOnLeavingField(owner.Id, card));
        }
        return next.WithBookkeeping(next.Bookkeeping with { BanishOnLeave = Array.AsReadOnly(marked.Except(gone).ToArray()) });
    }

    /// <summary>Whether a just-Summoned monster also meets the conditions that say how it was Summoned.</summary>
    private static bool SummonedHow(SummonedMonster summoned, IEnumerable<EffectCondition> conditions) => conditions.All(c => c switch
    {
        TributeCountCondition tributes => summoned.Kind == SummonKinds.Normal && summoned.Tributes == tributes.Tributes,
        XyzSummonedCondition => summoned.Xyz,
        _ => true
    });

    private DuelState DiscardRandom(DuelState state, PlayerId controller, DiscardRandomStep step, List<DuelEvent> events)
    {
        var player = state.GetPlayer(step.Player == TargetSide.Opponent ? Other(controller) : controller);
        if (player.Hand.Count == 0) return state;
        var (index, random) = RandomOf(state).NextBelow(player.Hand.Count);
        var discarded = player.Hand[index];
        events.Add(new CardDiscarded(player.Id, discarded));
        return WithResults(state.With(player: player.With(hand: player.Hand.Where(c => c != discarded), graveyard: player.Graveyard.Append(discarded))),
            Results(state), random);
    }
}
