namespace TagForceSpecial.Core;

/// <summary>
/// Shared activation pipeline and step runner for <see cref="EffectDefinition"/>s:
/// preflight (no mutation) → commit (place or flip the source, add the link) → response window,
/// then last-first resolution of each link's ordered steps through <see cref="ResolveDefinitionSteps"/>.
/// </summary>
public sealed partial class DuelEngine
{
    private DuelActionResult ActivateSetDefinition(int zone)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var source = State.GetPlayer(State.ActivePlayer).SpellTrapFields[zone];
        if (source is null || source.IsFaceUp || !definitions.TryGetValue(source.Card.GameCardId, out var definition))
            return Reject(DuelError.UnsupportedSpellEffect);
        return ActivateDefinition(source.Card, zone, definition, source);
    }

    /// <summary>Hand activation passes no source; set activation passes the face-down placement it flips.</summary>
    private DuelActionResult ActivateDefinition(CardInstance card, int zone, EffectDefinition definition,
        FieldSpellTrap? source = null)
    {
        // Preflight: every check precedes any mutation, so a rejection leaves state and events untouched.
        var player = State.GetPlayer(State.ActivePlayer);
        if (source is null && player.SpellTrapFields[zone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        // Ordinary Normal Spells may activate on the turn they were set; other kinds add their set-turn rule here.

        // Commit: the source keeps one placement identity from activation through cleanup.
        var slots = player.SpellTrapFields.ToArray();
        long placement = source?.PlacementId ?? checked(State.Revision + 1);
        slots[zone] = source is null ? new(card, true, placement) : source with { IsFaceUp = true };
        var receipt = new CommittedActivation(player.Id, definition.EffectId, card, zone, null, null);
        var link = new ChainLink(1, receipt, placement, ChainOperation.Definition, definition.SpellSpeed);
        var chain = new PendingChain(checked(State.Revision + 1), [link], Other(player.Id), freezeOnCompletion: false);
        var next = State.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != card.InstanceId), spellTraps: slots))
            .WithCommittedActivation(receipt).WithChain(chain);
        return Accept(next, new SpellActivated(player.Id, definition.EffectId, card, zone), new ActivationCommitted(receipt),
            new ChainLinkAdded(link), new ChainResponseRequested(chain.Responder, chain.DecisionId));
    }

    /// <summary>Runs the link's steps in order; stops at the first step that ends the duel.</summary>
    private DuelState ResolveDefinitionSteps(DuelState state, ChainLink link, List<DuelEvent> events)
    {
        var definition = definitions[link.Activation.Spell.GameCardId];
        foreach (var step in definition.Steps)
        {
            state = step switch
            {
                GainLifePointsStep gain => GainLifePoints(state, link.Activation.Player, gain.Amount, definition.EffectId, events),
                DealDamageStep damage => DealEffectDamage(state, Other(link.Activation.Player), damage.Amount, definition.EffectId, events),
                SummonTokensStep tokens => SummonTokens(state, link, tokens, events),
                _ => throw new InvalidOperationException("Unsupported effect step.")
            };
            if (state.Outcome is not null) break;
        }
        return state;
    }

    /// <summary>All or nothing, lowest free zones first (deliberate PC policy until a PSP observation exists).</summary>
    private DuelState SummonTokens(DuelState state, ChainLink link, SummonTokensStep step, List<DuelEvent> events)
    {
        var controller = link.Activation.Player;
        var free = Enumerable.Range(0, 5).Where(z => state.GetPlayer(controller).MonsterZones[z] is null).ToArray();
        if (free.Length < step.TokenCardIds.Count)
        {
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SummonUnavailable));
            return state;
        }
        var zones = state.GetPlayer(controller).MonsterZones.ToArray();
        for (int i = 0; i < step.TokenCardIds.Count; i++)
        {
            var card = AllocateInstance(ref state, step.TokenCardIds[i], controller);
            var token = NewSummonedMonster(state, card, step.Position);
            zones[free[i]] = token;
            events.Add(new TokenSpecialSummoned(controller, free[i], token));
        }
        return state.With(player: state.GetPlayer(controller).With(monsters: zones));
    }

    private DuelState GainLifePoints(DuelState state, PlayerId recipient, int amount, string effectId, List<DuelEvent> events)
    {
        var player = state.GetPlayer(recipient);
        // Same bounded integer policy as existing trigger LP recovery.
        int after = (int)Math.Min(int.MaxValue, (long)player.LifePoints + amount);
        events.Add(new LifePointsGained(recipient, after - player.LifePoints, player.LifePoints, after, effectId));
        return state.With(player: player.With(lifePoints: after));
    }

    private DuelState DealEffectDamage(DuelState state, PlayerId recipient, int amount, string effectId, List<DuelEvent> events)
    {
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
