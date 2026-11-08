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
    private enum ActivationContext { Hand, OwnSetCard, OpenWindow, ChainResponse, HandChainResponse, BattleWindow, BattleHand }

    private const BattleTiming DamageStepTimings = BattleTiming.DamageStart | BattleTiming.BeforeCalculation |
        BattleTiming.Calculation | BattleTiming.AfterCalculation | BattleTiming.DamageEnd;

    /// <summary>
    /// The single activation legality check for definitions: timing by context, then conditions, the summon lock and
    /// answerable choices. Does not mutate state.
    /// </summary>
    private DuelError DefinitionActivationError(EffectDefinition definition, ActivationContext context, PlayerId player,
        CardInstance card, FieldSpellTrap? source, ChainLink? preceding = null)
    {
        // No definition is yet allowed during the Damage Step.
        if (State.Battle is { } battle && (battle.Window.Timing & DamageStepTimings) != 0) return DuelError.ActivationTimingNotAllowed;
        bool fromHand = context is ActivationContext.Hand or ActivationContext.HandChainResponse or ActivationContext.BattleHand;
        bool setThisTurn = source is not null && source.SetOnTurn >= State.TurnNumber;
        if (!fromHand && (source is null || source.IsFaceUp))
            return context is ActivationContext.ChainResponse or ActivationContext.OpenWindow or ActivationContext.BattleWindow
                ? DuelError.UnsupportedChainResponse : DuelError.UnsupportedSpellEffect;
        bool slowerThanPreceding = preceding is not null && (int)definition.SpellSpeed < (int)preceding.SpellSpeed;
        var timing = (context, definition.Kind) switch
        {
            (ActivationContext.Hand, EffectKind.NormalSpell or EffectKind.QuickPlaySpell) => DuelError.None,
            (ActivationContext.Hand, _) => DuelError.ActivationTimingNotAllowed,
            (ActivationContext.BattleHand, EffectKind.QuickPlaySpell) => DuelError.None,
            (ActivationContext.OwnSetCard, EffectKind.NormalSpell) => DuelError.None,
            (ActivationContext.OwnSetCard or ActivationContext.OpenWindow or ActivationContext.BattleWindow, EffectKind.QuickPlaySpell or EffectKind.NormalTrap)
                => setThisTurn ? DuelError.SetThisTurn : DuelError.None,
            (ActivationContext.OwnSetCard or ActivationContext.OpenWindow or ActivationContext.BattleWindow, _) => DuelError.ActivationTimingNotAllowed,
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
                OpponentAttackDeclaredCondition => State.Battle is { Window.Timing: BattleTiming.AttackDeclaration, Attack: { Cancellation: null } attack } &&
                    attack.Attacker.Player != playerId && attack.Attacker.IsStillPresent(State),
                MonsterSummonedCondition summoned => SummonedMatches(State, playerId, summoned).Any(),
                _ => false
            };
            if (!met) return DuelError.ActivationConditionNotMet;
        }
        // A counted amount needs something to count when activated (it is read again at resolution).
        if (definition.Counts.Any(count => CountFor(State, playerId, count) == 0)) return DuelError.ActivationConditionNotMet;
        if (definition.SummonsMonsters && IsSummonLocked(State, playerId)) return DuelError.SummonLocked;
        if (definition.Cost is PayLifePointsCost pay && player.LifePoints <= pay.Amount) return DuelError.InsufficientLifePoints;
        if (definitionDecisions.TryGetValue(definition.CardId, out var sequence))
        {
            // Simulate one pick per field target so several targets need distinct cards.
            List<DecisionAnswer> simulated = [];
            for (int i = 0; i < sequence.Steps.Count && !sequence.Steps[i].Optional; i++)
            {
                var decision = CreateDecision(sequence, i, simulated, card.InstanceId, playerId);
                if (decision is null) return DuelError.DecisionUnavailable;
                if (decision.FieldTargetOptions is { } options) simulated.Add(new(DecisionKind.FieldTarget, FieldTarget: options[0]));
            }
        }
        if (definition.Steps.OfType<SpecialSummonStep>().Any(s => !SpecialSummonOptions(State, playerId, s, card).Any()))
            return DuelError.DecisionUnavailable;
        // Deck operations need enough cards now (counting targets shuffled in first); a choice needs a matching card.
        int incoming = definition.Steps.TakeWhile(s => s is not DrawCardsStep).Any(s => s is ShuffleTargetsIntoDeckStep)
            ? definition.FieldTargets.Sum(EffectDefinition.MinCount) : 0;
        foreach (var step in definition.Steps)
        {
            bool unavailable = step switch
            {
                DrawCardsStep draw => DrawingPlayers(playerId, draw.Players)
                    .Any(p => State.GetPlayer(p).Deck.Count + (p == playerId ? incoming : 0) < draw.Count),
                MillTopOfDeckStep mill => player.Deck.Count < mill.Count,
                AddFromDeckToHandStep search => !DeckMatches(State, playerId, search.Filter).Any(),
                SendFromDeckToGraveyardStep send => !DeckMatches(State, playerId, send.Filter).Any(),
                DestroyAllStep all => !FieldMatches(State, playerId, all.Kind, all.Side, all.Filter, card.InstanceId).Any(),
                ReturnAllToHandStep all => !FieldMatches(State, playerId, all.Kind, all.Side, FieldCardFilter.Any, card.InstanceId).Any(),
                ChangeAllToDefenseStep all => !FieldMatches(State, playerId, FieldCardKind.Monster, all.Side, FieldCardFilter.AttackPosition, null).Any(),
                DestroyExtremeMonsterStep extreme => ExtremeCandidates(State, playerId, extreme).Length == 0,
                BanishAllStep all => !FieldMatches(State, playerId, FieldCardKind.Monster, all.Side, all.Filter, null).Any(),
                _ => false
            };
            if (unavailable) return DuelError.DecisionUnavailable;
        }
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

    /// <summary>A set definition card that starts a chain in an open battle window (caller checked responder and decision ID).</summary>
    private DuelActionResult BeginBattleDefinition(PlayerId actor, int zone, FieldSpellTrap source, EffectDefinition definition)
    {
        if (DefinitionActivationError(definition, ActivationContext.BattleWindow, actor, source.Card, source) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, actor, source.Card, zone, definition, source, null, []);
    }

    /// <summary>The turn player's hand Quick-Play Spell starting a chain in an open battle window (caller checked the window).</summary>
    private DuelActionResult BeginBattleHandDefinition(ActivateHandBattleResponse command)
    {
        if (command.SpellTrapZone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var player = State.GetPlayer(command.Player);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == command.CardInstanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (!definitions.TryGetValue(card.GameCardId, out var definition)) return Reject(DuelError.UnsupportedBattleResponse);
        if (player.Id != State.ActivePlayer) return Reject(DuelError.ActivationTimingNotAllowed);
        if (player.SpellTrapFields[command.SpellTrapZone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        if (DefinitionActivationError(definition, ActivationContext.BattleHand, player.Id, card, null) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, player.Id, card, command.SpellTrapZone, definition, null, null, []);
    }

    /// <summary>A set definition card activated as the next chain link (caller checked responder and decision ID).</summary>
    private DuelActionResult BeginDefinitionResponse(PendingChain chain, int zone, FieldSpellTrap source, EffectDefinition definition)
    {
        if (DefinitionActivationError(definition, ActivationContext.ChainResponse, chain.Responder, source.Card, source,
            chain.Links[^1]) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, chain.Responder, source.Card, zone, definition, source, chain, []);
    }

    /// <summary>Activates the turn player's Quick-Play Spell from the hand as the next chain link.</summary>
    private DuelActionResult BeginHandDefinitionResponse(PendingChain chain, ActivateHandChainResponse command)
    {
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

    /// <summary>Places or flips the source, then asks for cost and target decisions or adds the link at once.</summary>
    private DuelActionResult CommitDefinition(DuelState state, PlayerId playerId, CardInstance card, int zone,
        EffectDefinition definition, FieldSpellTrap? source, PendingChain? chain, DuelEvent[] lead)
    {
        var player = state.GetPlayer(playerId);
        var slots = player.SpellTrapFields.ToArray();
        long placement = source?.PlacementId ?? checked(State.Revision + 1);
        slots[zone] = source is null ? new(card, true, placement) : source with { IsFaceUp = true };
        var placed = state.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != card.InstanceId), spellTraps: slots));
        var activated = new SpellActivated(playerId, definition.EffectId, card, zone);
        DuelEvent[] head = [.. lead, activated];
        if (definition.Cost is PayLifePointsCost pay)
        {
            var payer = placed.GetPlayer(playerId);
            placed = placed.With(player: payer.With(lifePoints: payer.LifePoints - pay.Amount));
            head = [.. head, new LifePointsPaid(playerId, pay.Amount, payer.LifePoints, payer.LifePoints - pay.Amount, definition.EffectId)];
        }
        if (definitionDecisions.TryGetValue(definition.CardId, out var sequence))
        {
            var decision = CreateDecision(sequence, 0, [], card.InstanceId, playerId)!;
            var pending = new PendingSpellActivation(playerId, definition.EffectId, card, zone,
                ResponseToLink: chain?.Links[^1].Number ?? 0, SourcePlacementId: placement);
            return Accept(placed.WithActivation(pending).WithDecision(decision), [.. head, new DecisionRequested(decision)]);
        }
        // The attacking or just-Summoned monsters are bound automatically; no decision is needed.
        IReadOnlyList<FieldCardTarget>? automatic = definition.FieldTargets switch
        {
            [AttackingMonsterTarget] when State.Battle?.Attack is { } attack =>
                [new(attack.Attacker.Player, FieldLocation.MonsterZone, attack.Attacker.Zone, attack.Attacker.Card, attack.Attacker.PlacementId)],
            [SummonedMonsterTarget] => [.. SummonedMatches(State, playerId, definition.Conditions.OfType<MonsterSummonedCondition>().First())],
            _ => null
        };
        return AddDefinitionLink(placed, new(playerId, definition.EffectId, card, zone, null, null, automatic), definition, placement,
            chain, [.. head]);
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
            // An optional pick with nothing left to choose ends the choices.
            if (decision is null && !sequence.Steps[pending.StepIndex + 1].Optional) return Reject(DuelError.DecisionUnavailable);
            if (decision is not null)
            {
                events.Add(new DecisionRequested(decision));
                return Accept(next.WithDecision(decision), events.ToArray());
            }
        }
        return CompleteDefinitionDecisions(pending, answers, next, activation, events);
    }

    /// <summary>Ends optional target choices early, committing the targets chosen so far.</summary>
    private DuelActionResult FinishDefinitionDecisions(PendingDecision pending)
    {
        var activation = State.PendingSpellActivation!;
        var player = State.GetPlayer(activation.Player);
        if (pending.Player != player.Id || player.SpellTrapFields[activation.SpellTrapZone] is not { IsFaceUp: true } source ||
            source.Card != activation.Spell || source.PlacementId != activation.SourcePlacementId)
            return Reject(DuelError.InvalidEffectState);
        return CompleteDefinitionDecisions(pending, pending.Answers.ToArray(), State, activation, []);
    }

    private DuelActionResult CompleteDefinitionDecisions(PendingDecision pending, DecisionAnswer[] answers, DuelState next,
        PendingSpellActivation activation, List<DuelEvent> events)
    {
        var definition = definitions[activation.Spell.GameCardId];
        var result = new DecisionSequenceResult(pending, answers);
        var target = answers.SingleOrDefault(a => a.Kind == DecisionKind.SpellTrapTarget)?.Target;
        var receipt = new CommittedActivation(activation.Player, definition.EffectId, activation.Spell,
            activation.SpellTrapZone, activation.DiscardedCost, target,
            answers.Any(a => a.FieldTarget is not null) ? Array.AsReadOnly(answers.Select(a => a.FieldTarget).OfType<FieldCardTarget>().ToArray()) : null);
        events.Add(new DecisionSequenceCompleted(result));
        return AddDefinitionLink(next.WithDecision(null, result).WithActivation(null), receipt, definition,
            activation.SourcePlacementId, State.PendingChain, events);
    }

    /// <summary>Starts a chain or appends the next link, applies any summon lock, and asks the other player to respond.</summary>
    private DuelActionResult AddDefinitionLink(DuelState state, CommittedActivation receipt, EffectDefinition definition,
        long placement, PendingChain? chain, List<DuelEvent> events)
    {
        var link = new ChainLink((chain?.Links.Count ?? 0) + 1, receipt, placement, ChainOperation.Definition, definition.SpellSpeed,
            TargetLinkNumber: definition.Steps.Any(s => s is NegateActivationStep) ? chain!.Links[^1].Number : null);
        var pending = new PendingChain(checked(State.Revision + 1), (chain?.Links ?? []).Append(link), Other(receipt.Player));
        var next = state.WithCommittedActivation(receipt).WithChain(pending);
        // A link added during battle reopens the battle window for the other player, as battle responses do.
        if (next.Battle is { } battle)
            next = next.WithBattle(battle with { Window = battle.Window with { ConsecutivePasses = 0, Responder = Other(receipt.Player) } });
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
        if (command is PassChainResponse && window.Summoned is not null)
        {
            // The turn player passes the summon response to the other player when that player can answer.
            var other = Other(window.Responder);
            if (window.Responder != State.ActivePlayer || !AnswersSummon(other)) return Accept(cleared, new OpenWindowPassed(window.Responder));
            var handed = new PendingOpenWindow(checked(State.Revision + 1), other, window.RequestedPhase) { Summoned = window.Summoned };
            return Accept(State.WithBookkeeping(State.Bookkeeping with { OpenWindow = handed }), new OpenWindowPassed(window.Responder),
                new SummonResponseWindowRequested(other, handed.DecisionId, window.Summoned));
        }
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
    private (DuelState State, PendingDefinitionChoice? Pause) ResolveDefinitionSteps(DuelState state, ChainLink[] links,
        ChainLink link, int start, DuelCommand? choice, List<DuelEvent> events)
    {
        var definition = definitions[link.Activation.Spell.GameCardId];
        var controller = link.Activation.Player;
        for (int i = start; i < definition.Steps.Count; i++)
        {
            var step = definition.Steps[i];
            if (step is AddFromDeckToHandStep or SendFromDeckToGraveyardStep)
            {
                if (choice is not ChooseDefinitionCard picked)
                {
                    var filter = step is AddFromDeckToHandStep search ? search.Filter : ((SendFromDeckToGraveyardStep)step).Filter;
                    var matches = DeckMatches(state, controller, filter).ToArray();
                    if (matches.Length == 0)
                    {
                        events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                        continue;
                    }
                    return (state, new PendingDefinitionCardChoice(checked(State.Revision + 1), controller, definition.EffectId,
                        link.Number, i, matches));
                }
                state = MoveChosenDeckCard(state, controller, picked.CardInstanceId, toHand: step is AddFromDeckToHandStep,
                    definition.EffectId, events);
                choice = null;
            }
            else if (EffectDefinition.IsFieldTargetStep(step) || EffectDefinition.IsZoneTargetStep(step))
            {
                var valid = QualifyingTargets(state, link, definition).ToArray();
                if (valid.Length == 0)
                {
                    // With every target gone, the rest of the effect does not resolve.
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
                    break;
                }
                if (step is SpecialSummonTargetStep)
                {
                    if (choice is not ChooseDefinitionSummon place)
                        return (state, new PendingDefinitionSummon(checked(State.Revision + 1), controller, definition.EffectId, link.Number, i,
                            [new(valid[0].Card, DecisionCardSource.Graveyard, valid[0].Player)],
                            Enumerable.Range(0, 5).Where(z => state.GetPlayer(controller).MonsterZones[z] is null)));
                    state = PlaceSpecialSummon(RemoveFromZone(state, valid[0]), controller, valid[0].Card, place.MonsterZone, place.Position,
                        DecisionCardSource.Graveyard, events, sourceAlreadyRemoved: true);
                    choice = null;
                    continue;
                }
                if (step is NegateAttackStep)
                {
                    if (state.Battle is not { Attack: { Cancellation: null } attack } battle || attack.Attacker.Card != valid[0].Card)
                    {
                        events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
                        break;
                    }
                    events.Add(new AttackCancelled(attack.Id, BattleAttackCancellation.Negated));
                    state = state.WithBattle(battle with { Attack = attack with { Cancellation = BattleAttackCancellation.Negated } });
                    continue;
                }
                if (step is ShuffleTargetsIntoDeckStep shuffle)
                {
                    state = ShuffleIntoDecks(state, valid, definition.EffectId, events);
                    if (shuffle.StopUnlessAll && valid.Length < (link.Activation.FieldTargets?.Count ?? 0)) break;
                    continue;
                }
                foreach (var target in valid)
                    state = step switch
                    {
                        DestroyTargetsStep => DestroyFieldCard(state, target, events),
                        ReturnTargetsToHandStep => ReturnFieldCardToHand(state, target, definition.EffectId, events),
                        ChangeTargetsToDefenseStep => ChangeToDefense(state, target, events),
                        BanishFieldTargetsStep => BanishFieldCard(state, target, definition.EffectId, events),
                        DestroyAndBanishTargetsStep => DestroyAndBanishFieldCard(state, target, definition.EffectId, events),
                        DamageByTargetAttackStep => GetMonsterStatistics(state, target.Player, target.Zone)?.Attack is int damage and > 0
                            ? DealEffectDamage(state, Other(controller), damage, definition.EffectId, events) : state,
                        GainByTargetAttackStep => GetMonsterStatistics(state, target.Player, target.Zone)?.Attack is int gain and > 0
                            ? GainLifePoints(state, controller, gain, definition.EffectId, events) : state,
                        AddTargetsToHandStep => MoveZoneCardToHand(state, target, definition.EffectId, events),
                        BanishTargetsStep => MoveZoneCard(state, target, toBanished: true, definition.EffectId, events),
                        _ => MoveZoneCard(state, target, toBanished: false, definition.EffectId, events)
                    };
            }
            else if (step is DestroyExtremeMonsterStep extreme)
            {
                var tied = ExtremeCandidates(state, controller, extreme);
                if (tied.Length == 0)
                {
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                    continue;
                }
                var chosen = tied.Length == 1 ? tied[0]
                    : choice is ChooseDefinitionCard pick ? tied.FirstOrDefault(t => t.Card.InstanceId == pick.CardInstanceId) : null;
                if (chosen is null)
                    return (state, new PendingDefinitionCardChoice(checked(State.Revision + 1), controller, definition.EffectId,
                        link.Number, i, tied.Select(t => t.Card)));
                state = DestroyFieldCard(state, chosen, events);
                choice = null;
            }
            else if (step is SpecialSummonStep summon)
            {
                if (choice is not ChooseDefinitionSummon summonChoice)
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
                state = ApplySpecialSummon(state, link, summon, summonChoice, definition.EffectId, events);
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
                    DrawCardsStep draw => DrawingPlayers(controller, draw.Players)
                        .Aggregate(state, (s, p) => s.Outcome is null ? DrawCards(s, p, draw.Count, events) : s),
                    MillTopOfDeckStep mill => MillTop(state, controller, mill.Count, definition.EffectId, events),
                    DestroyAllStep all => FieldMatches(state, controller, all.Kind, all.Side, all.Filter, link.Activation.Spell.InstanceId)
                        .ToArray().Aggregate(state, (s, t) => DestroyFieldCard(s, t, events)),
                    ReturnAllToHandStep all => FieldMatches(state, controller, all.Kind, all.Side, FieldCardFilter.Any, link.Activation.Spell.InstanceId)
                        .ToArray().Aggregate(state, (s, t) => ReturnFieldCardToHand(s, t, definition.EffectId, events)),
                    BanishAllStep all => FieldMatches(state, controller, FieldCardKind.Monster, all.Side, all.Filter, null)
                        .ToArray().Aggregate(state, (s, t) => BanishFieldCard(s, t, definition.EffectId, events)),
                    ChangeAllToDefenseStep all => FieldMatches(state, controller, FieldCardKind.Monster, all.Side, FieldCardFilter.AttackPosition, null)
                        .ToArray().Aggregate(state, (s, t) => ChangeToDefense(s, t, events)),
                    _ => throw new InvalidOperationException("Unsupported effect step.")
                };
            if (state.Outcome is not null) break;
        }
        return (state, null);
    }

    /// <summary>Matching field cards, the activating player's side first: monsters, then Spell/Trap and Pendulum zones.</summary>
    private static IEnumerable<FieldCardTarget> FieldMatches(DuelState state, PlayerId controller, FieldCardKind kind, TargetSide side,
        FieldCardFilter filter, int? excludeInstanceId)
    {
        PlayerId[] players = side switch
        {
            TargetSide.Own => [controller],
            TargetSide.Opponent => [Other(controller)],
            _ => [controller, Other(controller)]
        };
        foreach (var id in players)
        {
            var player = state.GetPlayer(id);
            if (kind != FieldCardKind.SpellTrap)
                for (int zone = 0; zone < 5; zone++)
                    if (player.MonsterZones[zone] is { } monster && monster.Card.InstanceId != excludeInstanceId && filter switch
                        {
                            FieldCardFilter.Any => true,
                            FieldCardFilter.FaceUp => monster.Position != BattlePosition.FaceDownDefense,
                            FieldCardFilter.AttackPosition or FieldCardFilter.FaceUpAttack => monster.Position == BattlePosition.FaceUpAttack,
                            FieldCardFilter.DefensePosition => monster.Position != BattlePosition.FaceUpAttack,
                            _ => false
                        })
                        yield return new(id, FieldLocation.MonsterZone, zone, monster.Card, monster.PlacementId);
            if (kind == FieldCardKind.Monster || filter is not (FieldCardFilter.Any or FieldCardFilter.FaceUp)) continue;
            for (int zone = 0; zone < 5; zone++)
                if (player.SpellTrapFields[zone] is { } field && field.Card.InstanceId != excludeInstanceId &&
                    (filter == FieldCardFilter.Any || field.IsFaceUp))
                    yield return new(id, FieldLocation.SpellTrapZone, zone, field.Card, field.PlacementId);
            for (int zone = 0; zone < 2; zone++)
                if (player.PendulumZones[zone] is { } scale && scale.Card.InstanceId != excludeInstanceId)
                    yield return new(id, FieldLocation.PendulumZone, zone, scale.Card, scale.PlacementId);
        }
    }

    /// <summary>The link's bound field targets that are still in place and still match their specification.</summary>
    private IEnumerable<FieldCardTarget> QualifyingTargets(DuelState state, ChainLink link, EffectDefinition definition)
    {
        var specs = definition.FieldTargets.SelectMany(s => Enumerable.Repeat(s, EffectDefinition.MaxCount(s)));
        foreach (var (spec, target) in specs.Zip(link.Activation.FieldTargets ?? []))
        {
            var matches = spec switch
            {
                FieldTarget f => FieldMatches(state, link.Activation.Player, f.Kind, f.Side, f.Filter, null),
                ZoneTarget z => ZoneMatches(state, link.Activation.Player, z),
                AttackingMonsterTarget or SummonedMonsterTarget => FieldMatches(state, link.Activation.Player, FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.Any, null),
                _ => []
            };
            if (target.IsStillPresent(state) && matches.Any(m => m.Card == target.Card)) yield return target;
        }
    }

    /// <summary>Graveyard or banished cards matching a zone target, the activating player's side first.</summary>
    private IEnumerable<FieldCardTarget> ZoneMatches(DuelState state, PlayerId controller, ZoneTarget spec)
    {
        PlayerId[] players = spec.Side switch
        {
            TargetSide.Own => [controller],
            TargetSide.Opponent => [Other(controller)],
            _ => [controller, Other(controller)]
        };
        foreach (var id in players)
        {
            var player = state.GetPlayer(id);
            foreach (var card in spec.Zone == CardZone.Graveyard ? player.Graveyard : player.Banished)
                if (MatchesFilter(card, spec.Filter) && (!spec.MustBeSummonable || Enumerable.Range(0, 5).Any(z =>
                        CheckSpecialSummon(state, controller, card, z, BattlePosition.FaceUpAttack, DecisionCardSource.Graveyard,
                            sourcePlayer: id) == DuelError.None)))
                    yield return new(id, spec.Zone == CardZone.Graveyard ? FieldLocation.Graveyard : FieldLocation.Banished, -1, card, 0);
        }
    }

    private bool MatchesFilter(CardInstance card, CardFilter filter) => rules.TryGetValue(card.GameCardId, out var rule) && filter switch
    {
        CardIdFilter ids => ids.CardIds.Contains(card.GameCardId),
        AnyMonsterFilter => rule.Category == CardCategory.Monster,
        AnyCardFilter => true,
        _ => false
    };

    /// <summary>Face-up monsters sharing the lowest or highest current stat; monsters without that stat are ignored.</summary>
    private FieldCardTarget[] ExtremeCandidates(DuelState state, PlayerId controller, DestroyExtremeMonsterStep step)
    {
        var rated = FieldMatches(state, controller, FieldCardKind.Monster, step.Side, step.Filter, null)
            .Select(t => (Target: t, Stats: GetMonsterStatistics(state, t.Player, t.Zone)))
            .Select(x => (x.Target, Value: step.Stat == MonsterStat.Attack ? x.Stats?.Attack : x.Stats?.Defense))
            .Where(x => x.Value is not null).ToArray();
        if (rated.Length == 0) return [];
        int best = step.Extreme == StatExtreme.Lowest ? rated.Min(x => x.Value!.Value) : rated.Max(x => x.Value!.Value);
        return rated.Where(x => x.Value == best).Select(x => x.Target).ToArray();
    }

    private static DuelState RemoveFromZone(DuelState state, FieldCardTarget target)
    {
        var player = state.GetPlayer(target.Player);
        return target.Location == FieldLocation.Graveyard
            ? state.With(player: player.With(graveyard: player.Graveyard.Where(c => c != target.Card)))
            : state.With(player: player.With(banished: player.Banished.Where(c => c != target.Card)));
    }

    /// <summary>Adds a Graveyard or banished card to its owner's hand; Extra Deck monsters go to the Extra Deck.</summary>
    private DuelState MoveZoneCardToHand(DuelState state, FieldCardTarget target, string effectId, List<DuelEvent> events)
    {
        state = RemoveFromZone(state, target);
        var owner = state.GetPlayer(target.Card.Owner);
        bool extra = rules.TryGetValue(target.Card.GameCardId, out var rule) && rule.AllowsExtraDeck;
        state = state.With(player: extra ? owner.With(extraDeck: owner.ExtraDeck.Append(target.Card)) : owner.With(hand: owner.Hand.Append(target.Card)));
        events.Add(new CardMovedToHand(owner.Id, target.Card, target.Location == FieldLocation.Graveyard ? CardZone.Graveyard : CardZone.Banished, effectId));
        return state;
    }

    /// <summary>Banishes a Graveyard card, or returns a banished card to the Graveyard.</summary>
    private static DuelState MoveZoneCard(DuelState state, FieldCardTarget target, bool toBanished, string effectId, List<DuelEvent> events)
    {
        state = RemoveFromZone(state, target);
        var owner = state.GetPlayer(target.Card.Owner);
        state = state.With(player: toBanished ? owner.With(banished: owner.Banished.Append(target.Card))
            : owner.With(graveyard: owner.Graveyard.Append(target.Card)));
        events.Add(toBanished ? new CardBanishedFromGraveyard(owner.Id, target.Card, effectId) : new CardReturnedToGraveyard(owner.Id, target.Card, effectId));
        return state;
    }

    /// <summary>Shuffles targets into their owners' Decks (Extra Deck monsters to the Extra Deck), then each touched Deck.</summary>
    private DuelState ShuffleIntoDecks(DuelState state, FieldCardTarget[] targets, string effectId, List<DuelEvent> events)
    {
        HashSet<PlayerId> shuffled = [];
        foreach (var target in targets)
        {
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

    private static DuelState RemovePlacement(DuelState state, FieldCardTarget target)
    {
        var player = state.GetPlayer(target.Player);
        switch (target.Location)
        {
            case FieldLocation.MonsterZone:
                var monsters = player.MonsterZones.ToArray(); monsters[target.Zone] = null;
                return state.With(player: player.With(monsters: monsters));
            case FieldLocation.SpellTrapZone:
                var slots = player.SpellTrapFields.ToArray(); slots[target.Zone] = null;
                return state.With(player: player.With(spellTraps: slots));
            default:
                var scales = player.PendulumZones.ToArray(); scales[target.Zone] = null;
                return state.With(player: player.With(pendulumZones: scales));
        }
    }

    private DuelState DestroyFieldCard(DuelState state, FieldCardTarget target, List<DuelEvent> events) =>
        target.Location == FieldLocation.MonsterZone
            ? SendFieldCardToGraveyard(RemovePlacement(state, target), target.Card, new MonsterDestroyed(target.Player, target.Zone, target.Card), events)
            : RemoveFieldSpell(state, new SpellTrapTarget(target.Player, target.Zone, target.Card, target.PlacementId,
                target.Location == FieldLocation.PendulumZone),
                new SpellTrapDestroyed(target.Player, target.Zone, target.Card, target.Location == FieldLocation.PendulumZone), events);

    /// <summary>Returns a field card to its owner's hand; Extra Deck monsters go to the Extra Deck and Tokens cease to exist.</summary>
    private DuelState ReturnFieldCardToHand(DuelState state, FieldCardTarget target, string effectId, List<DuelEvent> events)
    {
        state = RemovePlacement(state, target);
        var card = target.Card;
        if (IsTokenId(card.GameCardId))
        {
            events.Add(new CardReturnedToHand(card.Owner, card, effectId) { FieldDestination = FieldCardDestination.Removed });
            events.Add(new TokenRemoved(card.Owner, card) { FieldDestination = FieldCardDestination.Removed });
            return state;
        }
        bool extra = rules.TryGetValue(card.GameCardId, out var rule) && rule.AllowsExtraDeck;
        var owner = state.GetPlayer(card.Owner);
        state = state.With(player: extra ? owner.With(extraDeck: owner.ExtraDeck.Append(card)) : owner.With(hand: owner.Hand.Append(card)));
        events.Add(new CardReturnedToHand(card.Owner, card, effectId)
            { FieldDestination = extra ? FieldCardDestination.ExtraDeck : FieldCardDestination.Hand });
        return state;
    }

    /// <summary>Destroys a field monster and banishes it instead of sending it to the Graveyard; a Token ceases to exist.</summary>
    private DuelState DestroyAndBanishFieldCard(DuelState state, FieldCardTarget target, string effectId, List<DuelEvent> events)
    {
        state = RemovePlacement(state, target);
        var card = target.Card;
        bool token = IsTokenId(card.GameCardId);
        var destination = token ? FieldCardDestination.Removed : FieldCardDestination.Banished;
        events.Add(new MonsterDestroyed(target.Player, target.Zone, card) { FieldDestination = destination });
        events.Add(new MonsterBanished(target.Player, card, target.Zone, effectId) { FieldDestination = destination });
        if (token)
        {
            events.Add(new TokenRemoved(card.Owner, card) { FieldDestination = FieldCardDestination.Removed });
            return state;
        }
        var owner = state.GetPlayer(card.Owner);
        return state.With(player: owner.With(banished: owner.Banished.Append(card)));
    }

    /// <summary>The just-Summoned monsters, still in place, that meet a summon condition for this player.</summary>
    private IEnumerable<FieldCardTarget> SummonedMatches(DuelState state, PlayerId playerId, MonsterSummonedCondition condition)
    {
        if (state.Bookkeeping.RecentSummon is not { } summoned || condition.Single && summoned.Count != 1) yield break;
        foreach (var (monster, kind) in summoned)
        {
            if ((condition.Kinds & kind) == 0 || condition.ByOpponent && monster.Player == playerId || !monster.IsStillPresent(state)) continue;
            var stats = GetMonsterStatistics(state, monster.Player, monster.Zone);
            if ((condition.Stat == MonsterStat.Attack ? stats?.Attack : stats?.Defense) is int value && value >= condition.Min && value <= condition.Max)
                yield return new(monster.Player, FieldLocation.MonsterZone, monster.Zone, monster.Card, monster.PlacementId);
        }
    }

    private bool? summonResponders;
    private bool SummonResponders => summonResponders ??= definitions.Values.Any(d => d.Conditions.Any(c => c is MonsterSummonedCondition));

    /// <summary>
    /// Records the latest Summon for summon conditions. In an open state it offers a summon-response window to the
    /// turn player, else the other player, when they have a set card answering it; otherwise the record lapses.
    /// </summary>
    private void TrackSummons(List<DuelEvent> emitted)
    {
        if (!SummonResponders) return;
        SummonedMonster[] summoned = [.. emitted.Select(e => e switch
        {
            MonsterNormalSummoned n => new SummonedMonster(new(n.Player, n.Zone, n.Monster.Card, n.Monster.PlacementId), SummonKinds.Normal),
            MonsterFlipSummoned f => new SummonedMonster(new(f.Player, f.Zone, f.Monster.Card, f.Monster.PlacementId), SummonKinds.Flip),
            MonsterSpecialSummoned s => new SummonedMonster(new(s.Player, s.Zone, s.Monster.Card, s.Monster.PlacementId), SummonKinds.Special),
            _ => null
        }).OfType<SummonedMonster>()];
        bool open = State.Outcome is null && State.PendingChain is null && State.OpenWindow is null && State.PendingDecision is null &&
            State.PendingSummon is null && State.EventEffects.PendingTriggers is null && State.EventEffects.PendingCommitment is null;
        if (summoned.Length != 0)
        {
            var summoner = summoned[^1].Monster.Player;
            State = State.WithBookkeeping(State.Bookkeeping with { RecentSummon = [.. summoned.Where(s => s.Monster.Player == summoner)] });
        }
        if (!open || State.Bookkeeping.RecentSummon is not { } recent) return;
        PlayerId? responder = summoned.Length == 0 ? null
            : AnswersSummon(State.ActivePlayer) ? State.ActivePlayer : AnswersSummon(Other(State.ActivePlayer)) ? Other(State.ActivePlayer) : null;
        if (responder is not { } first)
        {
            State = State.WithBookkeeping(State.Bookkeeping with { RecentSummon = null });
            return;
        }
        State = State.WithBookkeeping(State.Bookkeeping with { OpenWindow = new(State.Revision, first, State.Phase) { Summoned = recent } });
        emitted.Add(new SummonResponseWindowRequested(first, State.Revision, recent));
    }

    /// <summary>True when the player has a set card whose summon condition answers the latest Summon now.</summary>
    private bool AnswersSummon(PlayerId playerId) => State.GetPlayer(playerId).SpellTrapFields.Any(source => source is not null &&
        definitions.TryGetValue(source.Card.GameCardId, out var definition) &&
        definition.Conditions.Any(c => c is MonsterSummonedCondition) &&
        DefinitionActivationError(definition, ActivationContext.OpenWindow, playerId, source.Card, source) == DuelError.None);

    /// <summary>Banishes a field monster to its owner's banished zone; a Token ceases to exist.</summary>
    private DuelState BanishFieldCard(DuelState state, FieldCardTarget target, string effectId, List<DuelEvent> events)
    {
        state = RemovePlacement(state, target);
        var card = target.Card;
        if (IsTokenId(card.GameCardId))
        {
            events.Add(new MonsterBanished(target.Player, card, target.Zone, effectId) { FieldDestination = FieldCardDestination.Removed });
            events.Add(new TokenRemoved(card.Owner, card) { FieldDestination = FieldCardDestination.Removed });
            return state;
        }
        var owner = state.GetPlayer(card.Owner);
        events.Add(new MonsterBanished(target.Player, card, target.Zone, effectId) { FieldDestination = FieldCardDestination.Banished });
        return state.With(player: owner.With(banished: owner.Banished.Append(card)));
    }

    private static DuelState ChangeToDefense(DuelState state, FieldCardTarget target, List<DuelEvent> events)
    {
        var player = state.GetPlayer(target.Player);
        if (player.MonsterZones[target.Zone] is not { Position: BattlePosition.FaceUpAttack } monster) return state;
        var zones = player.MonsterZones.ToArray();
        zones[target.Zone] = monster with { Position = BattlePosition.FaceUpDefense };
        events.Add(new MonsterPositionChanged(player.Id, target.Zone, BattlePosition.FaceUpAttack, BattlePosition.FaceUpDefense));
        return state.With(player: player.With(monsters: zones));
    }

    private static IEnumerable<PlayerId> DrawingPlayers(PlayerId controller, EffectPlayers players) => players switch
    {
        EffectPlayers.Self => [controller],
        EffectPlayers.Opponent => [Other(controller)],
        _ => [controller, Other(controller)]
    };

    /// <summary>Deck cards matching a reviewed filter, in Deck order; cards without explicit rules never match.</summary>
    private IEnumerable<CardInstance> DeckMatches(DuelState state, PlayerId playerId, CardFilter filter) =>
        state.GetPlayer(playerId).Deck.Where(c => MatchesFilter(c, filter));

    /// <summary>Draws one card at a time; a player who must draw from an empty Deck loses at once.</summary>
    private static DuelState DrawCards(DuelState state, PlayerId playerId, int count, List<DuelEvent> events)
    {
        for (int i = 0; i < count; i++)
        {
            var player = state.GetPlayer(playerId);
            if (player.Deck.Count == 0)
            {
                var outcome = new DuelOutcome(Other(playerId), playerId, DuelEndReason.UnableToDraw);
                events.Add(new DuelEnded(outcome));
                return state.With(outcome: outcome);
            }
            var card = player.Deck[0];
            state = state.With(player: player.With(deck: player.Deck.Skip(1), hand: player.Hand.Append(card)));
            events.Add(new CardDrawn(playerId, card));
        }
        return state;
    }

    private static DuelState MillTop(DuelState state, PlayerId playerId, int count, string effectId, List<DuelEvent> events)
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

    /// <summary>Moves the chosen Deck card (validated by the caller) to the hand (revealed) or Graveyard, then shuffles.</summary>
    private static DuelState MoveChosenDeckCard(DuelState state, PlayerId playerId, int instanceId, bool toHand, string effectId,
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

    private DuelActionResult AnswerDefinitionCardChoice(PendingChain chain, ChooseDefinitionCard command)
    {
        var pending = chain.DefinitionCardChoice!;
        if (command.DecisionId != pending.DecisionId) return Reject(DuelError.StaleDecision);
        if (command.Player != pending.Player) return Reject(DuelError.NotActivePlayer);
        if (!pending.Options.Any(c => c.InstanceId == command.CardInstanceId)) return Reject(DuelError.InvalidDecisionChoice);
        return ResolveChainLink(chain, definitionChoice: command);
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
                lockExemptSource: link.Activation.Spell, sourcePlayer: option.From) is not DuelError.None and var error)
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

    /// <summary>An absent or replaced target is neither destroyed nor retargeted.</summary>
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
    /// Summons all Tokens to the lowest free zones, or none. Skipped while another card's summon lock applies.
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
