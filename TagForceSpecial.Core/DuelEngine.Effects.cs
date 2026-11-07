namespace TagForceSpecial.Core;

/// <summary>
/// Shared activation pipeline and step runner for <see cref="EffectDefinition"/>s:
/// preflight (no mutation) → commit (place or flip the source, answer cost/target decisions, pay the cost, add
/// the link) → response window, then last-first resolution of each link's ordered steps through
/// <see cref="ResolveDefinitionSteps"/>.
/// </summary>
public sealed partial class DuelEngine
{
    /// <summary>Where an activation is requested; each context has its own timing rules.</summary>
    private enum ActivationContext { Hand, OwnSetCard, OpenWindow, ChainResponse, HandChainResponse }

    /// <summary>
    /// The single legality check for definition activations (no mutation). Timing first — hand: Normal or
    /// Quick-Play Spell, own Main Phase; own set card: Normal Spell any time, Quick-Play/Normal Trap not the turn it
    /// was set, Counter Trap never in open state; open window: the non-turn player's set Quick-Play/Normal Trap;
    /// chain response: set, not Normal Spell, not the turn it was set, Spell Speed at least that of the preceding
    /// link; hand chain response: the turn player's Quick-Play. Then conditions, the summon lock, and that every
    /// cost, target and summon choice is answerable.
    /// </summary>
    private DuelError DefinitionActivationError(EffectDefinition definition, ActivationContext context, PlayerId player,
        CardInstance card, FieldSpellTrap? source, ChainLink? preceding = null)
    {
        bool fromHand = context is ActivationContext.Hand or ActivationContext.HandChainResponse;
        bool setThisTurn = source is not null && source.SetOnTurn >= State.TurnNumber;
        if (!fromHand && (source is null || source.IsFaceUp))
            return context is ActivationContext.ChainResponse or ActivationContext.OpenWindow
                ? DuelError.UnsupportedChainResponse : DuelError.UnsupportedSpellEffect;
        bool slowerThanPreceding = preceding is not null && (int)definition.SpellSpeed < (int)preceding.SpellSpeed;
        var timing = (context, definition.Kind) switch
        {
            (ActivationContext.Hand, EffectKind.NormalSpell or EffectKind.QuickPlaySpell) => DuelError.None,
            (ActivationContext.Hand, _) => DuelError.ActivationTimingNotAllowed,
            (ActivationContext.OwnSetCard, EffectKind.NormalSpell) => DuelError.None,
            (ActivationContext.OwnSetCard or ActivationContext.OpenWindow, EffectKind.QuickPlaySpell or EffectKind.NormalTrap)
                => setThisTurn ? DuelError.SetThisTurn : DuelError.None,
            (ActivationContext.OwnSetCard or ActivationContext.OpenWindow, _) => DuelError.ActivationTimingNotAllowed,
            (ActivationContext.ChainResponse, EffectKind.NormalSpell) => DuelError.ActivationTimingNotAllowed,
            (ActivationContext.ChainResponse, _) => setThisTurn ? DuelError.SetThisTurn
                : slowerThanPreceding ? DuelError.ChainResponseSpeedNotAllowed : DuelError.None,
            (ActivationContext.HandChainResponse, EffectKind.QuickPlaySpell) =>
                slowerThanPreceding ? DuelError.ChainResponseSpeedNotAllowed : DuelError.None,
            _ => DuelError.ActivationTimingNotAllowed
        };
        return timing != DuelError.None ? timing : DefinitionRequirementsError(definition, player, card, preceding);
    }

    private DuelError DefinitionRequirementsError(EffectDefinition definition, PlayerId playerId, CardInstance card, ChainLink? preceding)
    {
        var player = State.GetPlayer(playerId);
        foreach (var condition in definition.Conditions)
        {
            bool met = condition switch
            {
                FreeMonsterZonesCondition free => player.MonsterZones.Count(m => m is null) >= free.Count,
                // A Pendulum scale activation counts as a Spell activation; monster/trigger effects never do.
                RespondsToSpellActivationCondition => preceding is { Trigger: null } &&
                    (preceding.IsPendulumScale || rules[preceding.Activation.Spell.GameCardId].Category == CardCategory.Spell),
                NoSummonThisTurnCondition => !State.SummonHistory.Any(h => h.Player == playerId &&
                    h.TurnNumber == State.TurnNumber && h.Method != SummonMethod.Set),
                _ => false
            };
            if (!met) return DuelError.ActivationConditionNotMet;
        }
        // A counted amount needs something to count when activated (it is read again at resolution).
        if (definition.Counts.Any(count => CountFor(State, playerId, count) == 0)) return DuelError.ActivationConditionNotMet;
        if (definition.SummonsMonsters && IsSummonLocked(State, playerId)) return DuelError.SummonLocked;
        if (definitionDecisions.TryGetValue(definition.CardId, out var sequence) &&
            Enumerable.Range(0, sequence.Steps.Count).Any(i => CreateDecision(sequence, i, [], card.InstanceId, playerId) is null))
            return DuelError.DecisionUnavailable;
        if (definition.Steps.OfType<SpecialSummonStep>().Any(s => !SpecialSummonOptions(State, playerId, s, card).Any()))
            return DuelError.DecisionUnavailable;
        return DuelError.None;
    }

    private DuelActionResult ActivateSetDefinition(int zone)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var source = State.GetPlayer(State.ActivePlayer).SpellTrapFields[zone];
        if (source is null || source.IsFaceUp || !definitions.TryGetValue(source.Card.GameCardId, out var definition))
            return Reject(DuelError.UnsupportedSpellEffect);
        if (DefinitionActivationError(definition, ActivationContext.OwnSetCard, State.ActivePlayer, source.Card, source) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, State.ActivePlayer, source.Card, zone, definition, source, null, []);
    }

    /// <summary>Hand activation by the turn player in an open Main Phase state (caller checked the phase).</summary>
    private DuelActionResult ActivateDefinition(CardInstance card, int zone, EffectDefinition definition)
    {
        if (State.GetPlayer(State.ActivePlayer).SpellTrapFields[zone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        if (DefinitionActivationError(definition, ActivationContext.Hand, State.ActivePlayer, card, null) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, State.ActivePlayer, card, zone, definition, null, null, []);
    }

    /// <summary>A set definition card activated as the next chain link (caller checked responder and decision ID).</summary>
    private DuelActionResult BeginDefinitionResponse(PendingChain chain, int zone, FieldSpellTrap source, EffectDefinition definition)
    {
        if (DefinitionActivationError(definition, ActivationContext.ChainResponse, chain.Responder, source.Card, source,
            chain.Links[^1]) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, chain.Responder, source.Card, zone, definition, source, chain, []);
    }

    /// <summary>
    /// The turn player's Quick-Play Spell from the hand as the next chain link (caller checked responder and
    /// decision ID). Battle and summon windows are later work.
    /// </summary>
    private DuelActionResult BeginHandDefinitionResponse(PendingChain chain, ActivateHandChainResponse command)
    {
        if (State.Battle is not null) return Reject(DuelError.UnsupportedBattleResponse);
        if (State.PendingSummon is not null) return Reject(DuelError.UnsupportedSummonResponse);
        if (command.SpellTrapZone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var player = State.GetPlayer(chain.Responder);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == command.CardInstanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (!definitions.TryGetValue(card.GameCardId, out var definition)) return Reject(DuelError.UnsupportedChainResponse);
        // A Quick-Play Spell can be activated from the hand only during its controller's own turn.
        if (player.Id != State.ActivePlayer) return Reject(DuelError.ActivationTimingNotAllowed);
        if (player.SpellTrapFields[command.SpellTrapZone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        if (DefinitionActivationError(definition, ActivationContext.HandChainResponse, player.Id, card, null,
            chain.Links[^1]) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, player.Id, card, command.SpellTrapZone, definition, null, chain, []);
    }

    /// <summary>
    /// Commit: place or flip the source (one placement identity through cleanup). A definition with a cost or
    /// target then asks its decisions (<see cref="AnswerDefinitionDecision"/>); otherwise its link is added at once.
    /// All legality was checked before this point.
    /// </summary>
    private DuelActionResult CommitDefinition(DuelState state, PlayerId playerId, CardInstance card, int zone,
        EffectDefinition definition, FieldSpellTrap? source, PendingChain? chain, DuelEvent[] lead)
    {
        var player = state.GetPlayer(playerId);
        var slots = player.SpellTrapFields.ToArray();
        long placement = source?.PlacementId ?? checked(State.Revision + 1);
        slots[zone] = source is null ? new(card, true, placement) : source with { IsFaceUp = true };
        var placed = state.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != card.InstanceId), spellTraps: slots));
        var activated = new SpellActivated(playerId, definition.EffectId, card, zone);
        if (definitionDecisions.TryGetValue(definition.CardId, out var sequence))
        {
            var decision = CreateDecision(sequence, 0, [], card.InstanceId, playerId)!;
            var pending = new PendingSpellActivation(playerId, definition.EffectId, card, zone,
                ResponseToLink: chain?.Links[^1].Number ?? 0, SourcePlacementId: placement);
            return Accept(placed.WithActivation(pending).WithDecision(decision), [.. lead, activated, new DecisionRequested(decision)]);
        }
        return AddDefinitionLink(placed, new(playerId, definition.EffectId, card, zone, null, null), definition, placement,
            chain, [.. lead, activated]);
    }

    /// <summary>Cost then target answers; the final answer adds the link. The cost stays paid if a later step waits.</summary>
    private DuelActionResult AnswerDefinitionDecision(PendingDecision pending, DecisionAnswer answer, DecisionAnswer[] answers)
    {
        var activation = State.PendingSpellActivation!;
        var definition = definitions[activation.Spell.GameCardId];
        var sequence = definitionDecisions[definition.CardId];
        var player = State.GetPlayer(activation.Player);
        var chain = State.PendingChain;
        if (pending.SequenceId != sequence.Id || pending.Player != player.Id ||
            player.SpellTrapFields[activation.SpellTrapZone] is not { IsFaceUp: true } source ||
            source.Card != activation.Spell || source.PlacementId != activation.SourcePlacementId ||
            (chain?.Links[^1].Number ?? 0) != activation.ResponseToLink)
            return Reject(DuelError.InvalidEffectState);
        var next = State;
        List<DuelEvent> events = [new DecisionAnswered(player.Id, pending.SequenceId, pending.DecisionId, answer)];
        if (pending.Kind == DecisionKind.Card)
        {
            var cost = player.Hand.SingleOrDefault(c => c.InstanceId == answer.CardInstanceId);
            if (cost is null || activation.DiscardedCost is not null || cost == activation.Spell)
                return Reject(DuelError.InvalidEffectState);
            activation = activation with { DiscardedCost = cost };
            next = next.With(player: player.With(hand: player.Hand.Where(c => c != cost),
                graveyard: player.Graveyard.Append(cost))).WithActivation(activation);
            events.Add(new ActivationCostPaid(player.Id, definition.EffectId, cost));
        }
        if (pending.StepIndex + 1 < sequence.Steps.Count)
        {
            var decision = CreateDecision(sequence, pending.StepIndex + 1, answers);
            if (decision is null) return Reject(DuelError.DecisionUnavailable);
            events.Add(new DecisionRequested(decision));
            return Accept(next.WithDecision(decision), events.ToArray());
        }
        var result = new DecisionSequenceResult(pending, answers);
        var target = answers.SingleOrDefault(a => a.Kind == DecisionKind.SpellTrapTarget)?.Target;
        var receipt = new CommittedActivation(player.Id, definition.EffectId, activation.Spell,
            activation.SpellTrapZone, activation.DiscardedCost, target);
        events.Add(new DecisionSequenceCompleted(result));
        return AddDefinitionLink(next.WithDecision(null, result).WithActivation(null), receipt, definition,
            activation.SourcePlacementId, chain, events);
    }

    /// <summary>Starts a chain or appends the next link, applies any summon lock, and asks the other player to respond.</summary>
    private DuelActionResult AddDefinitionLink(DuelState state, CommittedActivation receipt, EffectDefinition definition,
        long placement, PendingChain? chain, List<DuelEvent> events)
    {
        var link = new ChainLink((chain?.Links.Count ?? 0) + 1, receipt, placement, ChainOperation.Definition, definition.SpellSpeed,
            TargetLinkNumber: definition.Steps.Any(s => s is NegateActivationStep) ? chain!.Links[^1].Number : null);
        var pending = new PendingChain(checked(State.Revision + 1), (chain?.Links ?? []).Append(link), Other(receipt.Player));
        var next = state.WithCommittedActivation(receipt).WithChain(pending);
        events.Add(new ActivationCommitted(receipt));
        if (definition.LocksOtherSummonsThisTurn)
        {
            var applied = new SummonLock(receipt.Player, State.TurnNumber, receipt.Spell, definition.EffectId);
            next = next.WithBookkeeping(next.Bookkeeping with { SummonLocks = Array.AsReadOnly(
                next.SummonLocks.Where(l => l.TurnNumber == State.TurnNumber).Append(applied).ToArray()) });
            events.Add(new SummonLockApplied(applied));
        }
        events.Add(new ChainLinkAdded(link));
        events.Add(new ChainResponseRequested(pending.Responder, pending.DecisionId));
        return Accept(next, events.ToArray());
    }

    /// <summary>Zones holding set definition cards the player could activate in an open window right now.</summary>
    private IEnumerable<int> OpenWindowActivations(PlayerId playerId)
    {
        var player = State.GetPlayer(playerId);
        for (int zone = 0; zone < 5; zone++)
            if (player.SpellTrapFields[zone] is { } source && definitions.TryGetValue(source.Card.GameCardId, out var definition) &&
                DefinitionActivationError(definition, ActivationContext.OpenWindow, playerId, source.Card, source) == DuelError.None)
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
        if (DefinitionActivationError(definition, ActivationContext.OpenWindow, window.Responder, source.Card, source) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(cleared, window.Responder, source.Card, response.SpellTrapZone, definition, source, null, []);
    }

    /// <summary>
    /// Runs the link's steps in order from <paramref name="start"/>; stops at the first step that ends the duel.
    /// A Special Summon step without a <paramref name="choice"/> pauses the link for that choice.
    /// </summary>
    private (DuelState State, PendingDefinitionSummon? Pause) ResolveDefinitionSteps(DuelState state, ChainLink[] links,
        ChainLink link, int start, ChooseDefinitionSummon? choice, List<DuelEvent> events)
    {
        var definition = definitions[link.Activation.Spell.GameCardId];
        var controller = link.Activation.Player;
        for (int i = start; i < definition.Steps.Count; i++)
        {
            var step = definition.Steps[i];
            if (step is SpecialSummonStep summon)
            {
                if (choice is null)
                {
                    var options = SpecialSummonOptions(state, controller, summon, link.Activation.Spell).ToArray();
                    if (options.Length == 0)
                    {
                        events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SummonUnavailable));
                        continue;
                    }
                    var zones = Enumerable.Range(0, 5).Where(z => state.GetPlayer(controller).MonsterZones[z] is null);
                    return (state, new PendingDefinitionSummon(checked(State.Revision + 1), controller, definition.EffectId,
                        link.Number, i, options, zones));
                }
                state = ApplySpecialSummon(state, link, summon, choice, definition.EffectId, events);
                choice = null;
            }
            else
                state = step switch
                {
                    GainLifePointsStep gain => GainForPlayers(state, controller, gain, definition.EffectId, events),
                    DealDamageStep damage => Scaled(state, controller, damage.Amount, damage.PerCount) is int amount and > 0
                        ? DealEffectDamage(state, Other(controller), amount, definition.EffectId, events) : state,
                    DamageBothPlayersStep both => DamageBothPlayers(state, controller, both, definition.EffectId, events),
                    SummonTokensStep tokens => SummonTokens(state, link, tokens, events),
                    ShuffleDeckStep => ShuffleDeck(state, controller, definition.EffectId, events),
                    DestroyTargetStep => DestroyTarget(state, link, events),
                    DestroyOpponentFaceUpMonstersStep => DestroyOpponentFaceUpMonsters(state, Other(controller), events),
                    NegateActivationStep negate => NegateLink(state, links, link, ChainNegation.Activation, negate.DestroySource, events),
                    _ => throw new InvalidOperationException("Unsupported effect step.")
                };
            if (state.Outcome is not null) break;
        }
        return (state, null);
    }

    /// <summary>Eligible hand cards first, then Deck cards (in order), each with at least one legal zone.</summary>
    private IEnumerable<DefinitionSummonOption> SpecialSummonOptions(DuelState state, PlayerId playerId, SpecialSummonStep step,
        CardInstance source)
    {
        var player = state.GetPlayer(playerId);
        foreach (var (flag, from) in new[] { (SpecialSummonSources.Hand, DecisionCardSource.Hand), (SpecialSummonSources.Deck, DecisionCardSource.Deck) })
        {
            if (!step.Sources.HasFlag(flag)) continue;
            foreach (var card in SpecialSourceCards(player, from).Where(c => step.EligibleCardIds.Contains(c.GameCardId)))
                if (Enumerable.Range(0, 5).Any(z => CheckSpecialSummon(state, playerId, card, z, BattlePosition.FaceUpAttack, from,
                        lockExemptSource: source) == DuelError.None))
                    yield return new(card, from);
        }
    }

    private DuelActionResult AnswerDefinitionSummon(PendingChain chain, ChooseDefinitionSummon command)
    {
        var pending = chain.DefinitionSummon!;
        if (command.DecisionId != pending.DecisionId) return Reject(DuelError.StaleDecision);
        if (command.Player != pending.Player) return Reject(DuelError.NotActivePlayer);
        if (!pending.PositionOptions.Contains(command.Position)) return Reject(DuelError.InvalidBattlePosition);
        if (!pending.MonsterZoneOptions.Contains(command.MonsterZone)) return Reject(DuelError.InvalidMonsterZone);
        var option = pending.Options.SingleOrDefault(o => o.Card.InstanceId == command.CardInstanceId);
        if (option is null) return Reject(DuelError.InvalidDecisionChoice);
        var link = chain.Links[pending.LinkNumber - 1];
        if (CheckSpecialSummon(State, pending.Player, option.Card, command.MonsterZone, command.Position, option.Source,
                lockExemptSource: link.Activation.Spell) is not DuelError.None and var error)
            return Reject(error);
        return ResolveChainLink(chain, definitionChoice: command);
    }

    /// <summary>Places the chosen card, schedules its End Phase banishment, then shuffles after a Deck summon.</summary>
    private DuelState ApplySpecialSummon(DuelState state, ChainLink link, SpecialSummonStep step, ChooseDefinitionSummon choice,
        string effectId, List<DuelEvent> events)
    {
        var controller = link.Activation.Player;
        var from = state.GetPlayer(controller).Hand.Any(c => c.InstanceId == choice.CardInstanceId)
            ? DecisionCardSource.Hand : DecisionCardSource.Deck;
        var card = SpecialSourceCards(state.GetPlayer(controller), from).Single(c => c.InstanceId == choice.CardInstanceId);
        state = PlaceSpecialSummon(state, controller, card, choice.MonsterZone, choice.Position, from, events);
        if (step.BanishAtEndPhase)
        {
            var placed = state.GetPlayer(controller).MonsterZones[choice.MonsterZone]!;
            var delayed = new PendingEndPhaseBanishment(controller, effectId, link.Activation.Spell, card, choice.MonsterZone,
                state.TurnNumber, placed.PlacementId);
            state = state.WithEndPhaseBanishments(state.PendingEndPhaseBanishments.Append(delayed));
            events.Add(new EndPhaseBanishmentScheduled(delayed));
        }
        return from == DecisionCardSource.Deck ? ShuffleDeck(state, controller, effectId, events) : state;
    }

    /// <summary>Native Typhoon's validator returns without destroying an absent or different placement. No retargeting.</summary>
    private DuelState DestroyTarget(DuelState state, ChainLink link, List<DuelEvent> events)
    {
        var target = link.Activation.Target ?? throw new InvalidOperationException("Target step without a bound target.");
        if (!target.IsStillPresent(target.Current(state)))
        {
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
            return state;
        }
        return RemoveFieldSpell(state, target, new SpellTrapDestroyed(target.Player, target.Zone, target.Card, target.IsPendulumZone), events);
    }

    private DuelState DestroyOpponentFaceUpMonsters(DuelState state, PlayerId opponent, List<DuelEvent> events)
    {
        for (int zone = 0; zone < 5; zone++)
            if (state.GetPlayer(opponent).MonsterZones[zone] is { Position: not BattlePosition.FaceDownDefense } monster)
            {
                var current = state.GetPlayer(opponent);
                var zones = current.MonsterZones.ToArray();
                zones[zone] = null;
                state = state.With(player: current.With(monsters: zones));
                state = SendFieldCardToGraveyard(state, monster.Card, new MonsterDestroyed(opponent, zone, monster.Card), events);
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

    /// <summary>
    /// All or nothing, lowest free zones first (deliberate PC policy until a PSP observation exists). A summon
    /// lock from another source skips the step; the activation's own lock never applies to it.
    /// </summary>
    private DuelState SummonTokens(DuelState state, ChainLink link, SummonTokensStep step, List<DuelEvent> events)
    {
        var controller = link.Activation.Player;
        var free = Enumerable.Range(0, 5).Where(z => state.GetPlayer(controller).MonsterZones[z] is null).ToArray();
        if (free.Length < step.TokenCardIds.Count || IsSummonLocked(state, controller, link.Activation.Spell))
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

    /// <summary>The current value of a public count, from the activating player's point of view.</summary>
    private static int CountFor(DuelState state, PlayerId controller, EffectCount count)
    {
        var own = state.GetPlayer(controller);
        var opponent = state.GetPlayer(Other(controller));
        int Monsters(DuelPlayerState p) => p.MonsterZones.Count(m => m is not null);
        return count switch
        {
            EffectCount.None => 1,
            EffectCount.OpponentMonsters => Monsters(opponent),
            EffectCount.AllMonsters => Monsters(own) + Monsters(opponent),
            EffectCount.OpponentHand => opponent.Hand.Count,
            EffectCount.OpponentHandAndField => opponent.Hand.Count + Monsters(opponent) +
                opponent.SpellTrapFields.Count(f => f is not null) + opponent.PendulumZones.Count(f => f is not null),
            EffectCount.OpponentGraveyard => opponent.Graveyard.Count,
            EffectCount.OpponentBanished => opponent.Banished.Count,
            _ => throw new ArgumentOutOfRangeException(nameof(count))
        };
    }

    /// <summary>Amount times the count, saturated to the integer range; zero when nothing is counted.</summary>
    private static int Scaled(DuelState state, PlayerId controller, int amount, EffectCount count) =>
        (int)Math.Min(int.MaxValue, (long)amount * CountFor(state, controller, count));

    private DuelState GainForPlayers(DuelState state, PlayerId controller, GainLifePointsStep step, string effectId, List<DuelEvent> events)
    {
        int amount = Scaled(state, controller, step.Amount, step.PerCount);
        if (amount == 0) return state;
        if (step.Players is EffectPlayers.Self or EffectPlayers.Both) state = GainLifePoints(state, controller, amount, effectId, events);
        if (step.Players is EffectPlayers.Opponent or EffectPlayers.Both) state = GainLifePoints(state, Other(controller), amount, effectId, events);
        return state;
    }

    /// <summary>Both players take damage at once (opponent's event first); both at 0 LP is a draw.</summary>
    private static DuelState DamageBothPlayers(DuelState state, PlayerId controller, DamageBothPlayersStep step, string effectId,
        List<DuelEvent> events)
    {
        var opponent = state.GetPlayer(Other(controller));
        var self = state.GetPlayer(controller);
        int opponentAfter = Math.Max(0, opponent.LifePoints - step.ToOpponent);
        int selfAfter = Math.Max(0, self.LifePoints - step.ToSelf);
        events.Add(new EffectDamageDealt(opponent.Id, opponent.LifePoints - opponentAfter, opponent.LifePoints, opponentAfter, effectId));
        events.Add(new EffectDamageDealt(self.Id, self.LifePoints - selfAfter, self.LifePoints, selfAfter, effectId));
        var next = state.With(player: opponent.With(lifePoints: opponentAfter)).With(player: self.With(lifePoints: selfAfter));
        DuelOutcome? outcome = (opponentAfter, selfAfter) switch
        {
            (0, 0) => new(null, null, DuelEndReason.BothLifePointsDepleted),
            (0, _) => new(controller, opponent.Id, DuelEndReason.LifePointsDepleted),
            (_, 0) => new(opponent.Id, controller, DuelEndReason.LifePointsDepleted),
            _ => null
        };
        if (outcome is null) return next;
        events.Add(new DuelEnded(outcome));
        return next.With(outcome: outcome);
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
