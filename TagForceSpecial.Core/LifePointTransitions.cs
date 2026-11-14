namespace TagForceSpecial.Core;

/// <summary>Applies LP arithmetic, conversion and terminal events; damage-rule queries run synchronously against the supplied state.</summary>
internal static class LifePointTransitions
{
    private static PlayerId Other(PlayerId player) => player == PlayerId.One ? PlayerId.Two : PlayerId.One;

    internal static DuelState DamageBothPlayers(DuelState state, PlayerId controller, DamageBothPlayersStep step, string effectId,
        List<DuelEvent> events, Func<DuelState, PlayerId, DamageKinds, PlayerId> damageRecipient,
        Func<DuelState, PlayerId, DamageRuleKind, DamageKinds, bool> hasRule)
    {
        var life = new Dictionary<PlayerId, int> { [PlayerId.One] = state.GetPlayer(PlayerId.One).LifePoints, [PlayerId.Two] = state.GetPlayer(PlayerId.Two).LifePoints };
        foreach (var (target, amount) in new[] { (Other(controller), step.ToOpponent), (controller, step.ToSelf) })
        {
            var recipient = damageRecipient(state, target, DamageKinds.Effect);
            int before = life[recipient];
            if (hasRule(state, recipient, DamageRuleKind.BecomesGain, DamageKinds.Effect))
            {
                life[recipient] = (int)Math.Min(int.MaxValue, (long)before + amount);
                events.Add(new LifePointsGained(recipient, life[recipient] - before, before, life[recipient], effectId));
            }
            else
            {
                life[recipient] = Math.Max(0, before - amount);
                events.Add(new EffectDamageDealt(recipient, before - life[recipient], before, life[recipient], effectId));
            }
        }
        var next = state.With(player: state.GetPlayer(PlayerId.One).With(lifePoints: life[PlayerId.One]))
            .With(player: state.GetPlayer(PlayerId.Two).With(lifePoints: life[PlayerId.Two]));
        DuelOutcome? outcome = (life[Other(controller)] == 0, life[controller] == 0) switch
        {
            (true, true) => new(null, null, DuelEndReason.BothLifePointsDepleted),
            (true, false) => new(controller, Other(controller), DuelEndReason.LifePointsDepleted),
            (false, true) => new(Other(controller), controller, DuelEndReason.LifePointsDepleted),
            _ => null
        };
        if (outcome is null) return next;
        events.Add(new DuelEnded(outcome));
        return next.With(outcome: outcome);
    }

    internal static DuelState GainLifePoints(DuelState state, PlayerId recipient, int amount, string effectId, List<DuelEvent> events,
        Func<DuelState, PlayerId, DamageKinds, PlayerId> damageRecipient,
        Func<DuelState, PlayerId, DamageRuleKind, DamageKinds, bool> hasRule, bool converted = false)
    {
        // An effect's LP gain can become damage; a gain that replaced damage does not turn back.
        if (!converted && hasRule(state, recipient, DamageRuleKind.GainBecomesDamage, DamageKinds.Effect))
            return DealEffectDamage(state, recipient, amount, effectId, events, damageRecipient, hasRule, converted: true);
        var player = state.GetPlayer(recipient);
        // Same bounded integer policy as existing trigger LP recovery.
        int after = (int)Math.Min(int.MaxValue, (long)player.LifePoints + amount);
        events.Add(new LifePointsGained(recipient, after - player.LifePoints, player.LifePoints, after, effectId));
        return state.With(player: player.With(lifePoints: after));
    }

    internal static DuelState DealEffectDamage(DuelState state, PlayerId recipient, int amount, string effectId, List<DuelEvent> events,
        Func<DuelState, PlayerId, DamageKinds, PlayerId> damageRecipient,
        Func<DuelState, PlayerId, DamageRuleKind, DamageKinds, bool> hasRule, bool converted = false)
    {
        recipient = damageRecipient(state, recipient, DamageKinds.Effect);
        if (!converted && hasRule(state, recipient, DamageRuleKind.BecomesGain, DamageKinds.Effect))
            return GainLifePoints(state, recipient, amount, effectId, events, damageRecipient, hasRule, converted: true);
        var player = state.GetPlayer(recipient);
        int remaining = Math.Max(0, player.LifePoints - amount);
        events.Add(new EffectDamageDealt(recipient, player.LifePoints - remaining, player.LifePoints, remaining, effectId));
        var next = state.With(player: player.With(lifePoints: remaining));
        if (remaining != 0) return next;
        var outcome = new DuelOutcome(Other(recipient), recipient, DuelEndReason.LifePointsDepleted);
        events.Add(new DuelEnded(outcome));
        return next.With(outcome: outcome);
    }
}
