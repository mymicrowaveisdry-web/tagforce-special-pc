namespace TagForceSpecial.Core;

/// <summary>
/// Shared activation pipeline and step runner for <see cref="EffectDefinition"/>s:
/// preflight (no mutation) → commit (place or flip the source, add the link) → response window,
/// then last-first resolution of each link's ordered steps through <see cref="ResolveDefinitionSteps"/>.
/// </summary>
public sealed partial class DuelEngine
{
    /// <summary>Where an activation is requested; each context has its own timing rules.</summary>
    private enum ActivationContext { Hand, OwnSetCard, OpenWindow, ChainResponse }

    /// <summary>
    /// The single legality check for definition activations (no mutation). Hand: Normal or Quick-Play Spell, own
    /// Main Phase. Own set card: Normal Spell any time, Quick-Play/Normal Trap not the turn it was set, Counter Trap
    /// never in open state. Open window: the non-turn player's set Quick-Play/Normal Trap. Chain response: set,
    /// not Normal Spell, not the turn it was set, Spell Speed at least that of the preceding link.
    /// </summary>
    private DuelError DefinitionActivationError(EffectDefinition definition, ActivationContext context,
        FieldSpellTrap? source, ChainLink? preceding = null)
    {
        bool setThisTurn = source is not null && source.SetOnTurn >= State.TurnNumber;
        if (context != ActivationContext.Hand && (source is null || source.IsFaceUp)) return DuelError.UnsupportedSpellEffect;
        return (context, definition.Kind) switch
        {
            (ActivationContext.Hand, EffectKind.NormalSpell or EffectKind.QuickPlaySpell) => DuelError.None,
            (ActivationContext.Hand, _) => DuelError.ActivationTimingNotAllowed,
            (ActivationContext.OwnSetCard, EffectKind.NormalSpell) => DuelError.None,
            (ActivationContext.OwnSetCard or ActivationContext.OpenWindow, EffectKind.QuickPlaySpell or EffectKind.NormalTrap)
                => setThisTurn ? DuelError.SetThisTurn : DuelError.None,
            (ActivationContext.OwnSetCard or ActivationContext.OpenWindow, _) => DuelError.ActivationTimingNotAllowed,
            (ActivationContext.ChainResponse, EffectKind.NormalSpell) => DuelError.ActivationTimingNotAllowed,
            (ActivationContext.ChainResponse, _) => setThisTurn ? DuelError.SetThisTurn
                : (int)definition.SpellSpeed < (int)preceding!.SpellSpeed ? DuelError.ChainResponseSpeedNotAllowed : DuelError.None,
            _ => DuelError.ActivationTimingNotAllowed
        };
    }

    private DuelActionResult ActivateSetDefinition(int zone)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var source = State.GetPlayer(State.ActivePlayer).SpellTrapFields[zone];
        if (source is null || source.IsFaceUp || !definitions.TryGetValue(source.Card.GameCardId, out var definition))
            return Reject(DuelError.UnsupportedSpellEffect);
        if (DefinitionActivationError(definition, ActivationContext.OwnSetCard, source) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, State.ActivePlayer, source.Card, zone, definition, source, null, []);
    }

    /// <summary>Hand activation by the turn player in an open Main Phase state (caller checked the phase).</summary>
    private DuelActionResult ActivateDefinition(CardInstance card, int zone, EffectDefinition definition)
    {
        if (State.GetPlayer(State.ActivePlayer).SpellTrapFields[zone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        if (DefinitionActivationError(definition, ActivationContext.Hand, null) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, State.ActivePlayer, card, zone, definition, null, null, []);
    }

    /// <summary>A set definition card activated as the next chain link (caller checked responder and decision ID).</summary>
    private DuelActionResult BeginDefinitionResponse(PendingChain chain, int zone, FieldSpellTrap source, EffectDefinition definition)
    {
        if (DefinitionActivationError(definition, ActivationContext.ChainResponse, source, chain.Links[^1]) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, chain.Responder, source.Card, zone, definition, source, chain, []);
    }

    /// <summary>
    /// Commit: place or flip the source (one placement identity through cleanup), then start a chain or append
    /// the next link; the other player is asked to respond. All legality was checked before this point.
    /// </summary>
    private DuelActionResult CommitDefinition(DuelState state, PlayerId playerId, CardInstance card, int zone,
        EffectDefinition definition, FieldSpellTrap? source, PendingChain? chain, DuelEvent[] lead)
    {
        var player = state.GetPlayer(playerId);
        var slots = player.SpellTrapFields.ToArray();
        long placement = source?.PlacementId ?? checked(State.Revision + 1);
        slots[zone] = source is null ? new(card, true, placement) : source with { IsFaceUp = true };
        var receipt = new CommittedActivation(playerId, definition.EffectId, card, zone, null, null);
        var link = new ChainLink((chain?.Links.Count ?? 0) + 1, receipt, placement, ChainOperation.Definition, definition.SpellSpeed);
        var pending = new PendingChain(checked(State.Revision + 1), (chain?.Links ?? []).Append(link), Other(playerId),
            freezeOnCompletion: chain?.FreezeOnCompletion ?? false);
        var next = state.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != card.InstanceId), spellTraps: slots))
            .WithCommittedActivation(receipt).WithChain(pending);
        return Accept(next, [.. lead, new SpellActivated(playerId, definition.EffectId, card, zone), new ActivationCommitted(receipt),
            new ChainLinkAdded(link), new ChainResponseRequested(pending.Responder, pending.DecisionId)]);
    }

    /// <summary>Zones holding set definition cards the player could activate in an open window right now.</summary>
    private IEnumerable<int> OpenWindowActivations(PlayerId playerId)
    {
        var player = State.GetPlayer(playerId);
        for (int zone = 0; zone < 5; zone++)
            if (player.SpellTrapFields[zone] is { } source && definitions.TryGetValue(source.Card.GameCardId, out var definition) &&
                DefinitionActivationError(definition, ActivationContext.OpenWindow, source) == DuelError.None)
                yield return zone;
    }

    /// <summary>
    /// The turn player's phase-change request. If the non-turn player has a legal open-window activation, the
    /// change is paused for them to activate or pass; otherwise it applies immediately (unchanged behaviour).
    /// </summary>
    private DuelActionResult RequestPhase(DuelPhase target)
    {
        if (PhaseChangeError(target) is not DuelError.None and var error) return Reject(error);
        var responder = Other(State.ActivePlayer);
        if (!OpenWindowActivations(responder).Any()) return Advance(target);
        var window = new PendingOpenWindow(checked(State.Revision + 1), responder, target);
        return Accept(State.WithBookkeeping(State.Bookkeeping with { OpenWindow = window }),
            new OpenWindowRequested(responder, window.DecisionId, target));
    }

    /// <summary>Pass applies the requested phase change; an activation starts a chain and the request lapses.</summary>
    private DuelActionResult ExecuteOpenWindow(DuelCommand command)
    {
        var window = State.OpenWindow!;
        long id = command switch { PassChainResponse c => c.DecisionId, ActivateChainResponse c => c.DecisionId, _ => -1 };
        if (id == -1) return Reject(DuelError.OpenWindowPending);
        if (id != window.DecisionId) return Reject(DuelError.StaleChainDecision);
        if (command.Player != window.Responder) return Reject(DuelError.NotChainResponder);
        var cleared = State.WithBookkeeping(State.Bookkeeping with { OpenWindow = null });
        if (command is PassChainResponse)
            return Advance(window.RequestedPhase, cleared, new OpenWindowPassed(window.Responder));
        var response = (ActivateChainResponse)command;
        if (response.SummonedCardInstanceId is not null || response.MonsterZone is not null) return Reject(DuelError.InvalidDecisionChoice);
        if (response.SpellTrapZone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var source = State.GetPlayer(window.Responder).SpellTrapFields[response.SpellTrapZone];
        if (source is null || !definitions.TryGetValue(source.Card.GameCardId, out var definition))
            return Reject(DuelError.UnsupportedChainResponse);
        if (DefinitionActivationError(definition, ActivationContext.OpenWindow, source) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(cleared, window.Responder, source.Card, response.SpellTrapZone, definition, source, null, []);
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
                ShuffleDeckStep => ShuffleDeck(state, link.Activation.Player, definition.EffectId, events),
                _ => throw new InvalidOperationException("Unsupported effect step.")
            };
            if (state.Outcome is not null) break;
        }
        return state;
    }

    /// <summary>Seeded Fisher–Yates over the whole Deck; the advanced generator state is part of the snapshot.</summary>
    private static DuelState ShuffleDeck(DuelState state, PlayerId playerId, string effectId, List<DuelEvent> events)
    {
        var random = state.Random ?? throw new InvalidOperationException("Shuffle steps require a seeded engine.");
        var player = state.GetPlayer(playerId);
        var (deck, next) = random.Shuffle(player.Deck);
        events.Add(new DeckShuffled(playerId, effectId));
        return state.With(player: player.With(deck: deck)).WithBookkeeping(state.Bookkeeping with { Random = next });
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
