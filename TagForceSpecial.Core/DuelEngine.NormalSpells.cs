namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult ActivateSetNormalSpell(int zone)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var source = State.GetPlayer(State.ActivePlayer).SpellTrapFields[zone];
        if (source is null || source.IsFaceUp || !normalSpells.TryGetValue(source.Card.GameCardId, out var profile))
            return Reject(DuelError.UnsupportedSpellEffect);
        // Ordinary Normal Spells may activate on the turn they were set.
        return ActivateNormalSpell(source.Card, zone, profile, source);
    }

    private DuelActionResult ActivateNormalSpell(CardInstance card, int zone, NormalSpellProfile profile,
        FieldSpellTrap? source = null)
    {
        var player = State.GetPlayer(State.ActivePlayer);
        if (source is null && player.SpellTrapFields[zone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        var slots = player.SpellTrapFields.ToArray();
        long placement = source?.PlacementId ?? checked(State.Revision + 1);
        slots[zone] = source is null ? new(card, true, placement) : source with { IsFaceUp = true };
        var receipt = new CommittedActivation(player.Id, profile.EffectId, card, zone, null, null);
        var link = new ChainLink(1, receipt, placement, ChainOperation.NormalSpell);
        var chain = new PendingChain(checked(State.Revision + 1), [link], Other(player.Id), freezeOnCompletion: false);
        var next = State.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != card.InstanceId), spellTraps: slots))
            .WithCommittedActivation(receipt).WithChain(chain);
        return Accept(next, new SpellActivated(player.Id, profile.EffectId, card, zone), new ActivationCommitted(receipt),
            new ChainLinkAdded(link), new ChainResponseRequested(chain.Responder, chain.DecisionId));
    }

    private DuelState ResolveNormalSpell(DuelState state, ChainLink link, List<DuelEvent> events)
    {
        var profile = normalSpells[link.Activation.Spell.GameCardId];
        var recipient = profile.Operation == NormalSpellOperation.GainLifePoints ? link.Activation.Player : Other(link.Activation.Player);
        var player = state.GetPlayer(recipient);
        if (profile.Operation == NormalSpellOperation.GainLifePoints)
        {
            // Same bounded integer policy as existing trigger LP recovery.
            int after = (int)Math.Min(int.MaxValue, (long)player.LifePoints + profile.Amount);
            events.Add(new LifePointsGained(recipient, after - player.LifePoints, player.LifePoints, after, profile.EffectId));
            return state.With(player: player.With(lifePoints: after));
        }
        int remaining = Math.Max(0, player.LifePoints - profile.Amount);
        events.Add(new EffectDamageDealt(recipient, player.LifePoints - remaining, player.LifePoints, remaining, profile.EffectId));
        var next = state.With(player: player.With(lifePoints: remaining));
        if (remaining != 0) return next;
        var outcome = new DuelOutcome(Other(recipient), recipient, DuelEndReason.LifePointsDepleted);
        events.Add(new DuelEnded(outcome));
        return next.With(outcome: outcome);
    }
}
