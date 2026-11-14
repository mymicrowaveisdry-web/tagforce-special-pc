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
    private enum ActivationContext { Hand, OwnSetCard, OpenWindow, ChainResponse, HandChainResponse, BattleWindow, BattleHand, SummonWindow, MonsterField, MonsterChainResponse, MonsterWindow, SpellTrapField, SpellTrapChain, SpellTrapWindow, ZoneField, ZoneChain, ZoneWindow }

    private const BattleTiming DamageStepTimings = BattleTiming.DamageStart | BattleTiming.BeforeCalculation |
        BattleTiming.Calculation | BattleTiming.AfterCalculation | BattleTiming.DamageEnd;

    /// <summary>
    /// The single activation legality check for definitions: timing by context, then conditions, the summon lock and
    /// answerable choices. Does not mutate state.
    /// </summary>
    private DuelError DefinitionActivationError(EffectDefinition definition, ActivationContext context, PlayerId player,
        CardInstance card, FieldSpellTrap? source, ChainLink? preceding = null)
    {
        // A Pendulum Effect is used only from an active scale.
        if (definition.Origin == EffectOrigin.PendulumZone && context is not (ActivationContext.SpellTrapField or ActivationContext.SpellTrapChain or ActivationContext.SpellTrapWindow))
            return DuelError.UnsupportedSpellEffect;
        // The Damage Step admits only definitions whose battle timing condition lists the current timing.
        if (State.Battle is { } battle && (battle.Window.Timing & DamageStepTimings) != 0 &&
            !definition.Conditions.OfType<BattleTimingCondition>().Any(c => c.Windows.HasFlag(battle.Window.Timing)))
            return DuelError.ActivationTimingNotAllowed;
        bool fromHand = context is ActivationContext.Hand or ActivationContext.HandChainResponse or ActivationContext.BattleHand;
        bool setThisTurn = source is not null && source.SetOnTurn >= State.TurnNumber;
        bool fromMonster = context is ActivationContext.MonsterField or ActivationContext.MonsterChainResponse or ActivationContext.MonsterWindow or ActivationContext.SpellTrapField or
            ActivationContext.SpellTrapChain or ActivationContext.SpellTrapWindow or ActivationContext.ZoneField or ActivationContext.ZoneChain or ActivationContext.ZoneWindow;
        if (!fromHand && !fromMonster && (source is null || source.IsFaceUp))
            return context switch
            {
                ActivationContext.ChainResponse or ActivationContext.OpenWindow => DuelError.UnsupportedChainResponse,
                ActivationContext.BattleWindow => DuelError.UnsupportedBattleResponse,
                ActivationContext.SummonWindow => DuelError.UnsupportedSummonResponse,
                _ => DuelError.UnsupportedSpellEffect
            };
        bool slowerThanPreceding = preceding is not null && (int)definition.SpellSpeed < (int)preceding.SpellSpeed;
        var timing = (context, definition.Kind) switch
        {
            (ActivationContext.MonsterField, EffectKind.MonsterIgnition or EffectKind.MonsterQuick) => DuelError.None,
            (ActivationContext.MonsterChainResponse, EffectKind.MonsterQuick) => slowerThanPreceding ? DuelError.ChainResponseSpeedNotAllowed : DuelError.None,
            (ActivationContext.MonsterWindow, EffectKind.MonsterQuick) => DuelError.None,
            (ActivationContext.SpellTrapField, EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick) => DuelError.None,
            (ActivationContext.SpellTrapChain, EffectKind.SpellTrapQuick) => slowerThanPreceding ? DuelError.ChainResponseSpeedNotAllowed : DuelError.None,
            (ActivationContext.SpellTrapWindow, EffectKind.SpellTrapQuick) => DuelError.None,
            (ActivationContext.ZoneField, EffectKind.MonsterIgnition or EffectKind.MonsterQuick or EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick) => DuelError.None,
            (ActivationContext.ZoneChain, EffectKind.MonsterQuick or EffectKind.SpellTrapQuick) => slowerThanPreceding ? DuelError.ChainResponseSpeedNotAllowed : DuelError.None,
            (ActivationContext.ZoneWindow, EffectKind.MonsterQuick or EffectKind.SpellTrapQuick) => DuelError.None,
            (ActivationContext.Hand, EffectKind.NormalSpell or EffectKind.QuickPlaySpell or EffectKind.ContinuousSpell or EffectKind.FieldSpell or EffectKind.EquipSpell) => DuelError.None,
            (ActivationContext.Hand, _) => DuelError.ActivationTimingNotAllowed,
            (ActivationContext.BattleHand, EffectKind.QuickPlaySpell) => DuelError.None,
            // Only Counter Traps answer a Summon that has not happened yet.
            (ActivationContext.SummonWindow, EffectKind.CounterTrap) => setThisTurn ? DuelError.SetThisTurn : DuelError.None,
            (ActivationContext.OwnSetCard, EffectKind.NormalSpell or EffectKind.ContinuousSpell or EffectKind.FieldSpell or EffectKind.EquipSpell) => DuelError.None,
            (ActivationContext.OwnSetCard or ActivationContext.OpenWindow or ActivationContext.BattleWindow, EffectKind.QuickPlaySpell or EffectKind.NormalTrap or EffectKind.ContinuousTrap)
                => setThisTurn ? DuelError.SetThisTurn : DuelError.None,
            // A Counter Trap that answers an attack is activated in the battle window it waits for.
            (ActivationContext.BattleWindow, EffectKind.CounterTrap) when definition.Conditions.Any(c => c is OpponentAttackDeclaredCondition or BattleTimingCondition)
                => setThisTurn ? DuelError.SetThisTurn : DuelError.None,
            // So is one whose own timing is a Summon that just happened, in the window after it.
            (ActivationContext.OpenWindow, EffectKind.CounterTrap) when definition.Conditions.Any(c => c is MonsterSummonedCondition)
                => setThisTurn ? DuelError.SetThisTurn : DuelError.None,
            (ActivationContext.OwnSetCard or ActivationContext.OpenWindow or ActivationContext.BattleWindow, _) => DuelError.ActivationTimingNotAllowed,
            (ActivationContext.ChainResponse, EffectKind.NormalSpell or EffectKind.ContinuousSpell or EffectKind.FieldSpell or EffectKind.EquipSpell) => DuelError.ActivationTimingNotAllowed,
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
        if (definition.NameLimit is { } limit && NameLimitReached(State, playerId, limit)) return DuelError.EffectAlreadyUsed;
        if (GraveyardCostBlocked(State, playerId, card, definition)) return DuelError.LockedByEffect;
        if (definition.Steps.OfType<AttachFromStep>().ToArray() is { Length: > 0 } attaches &&
            AttachCandidates(State, playerId, attaches[0], card).Count() < attaches.Length) return DuelError.DecisionUnavailable;
        if (ActivationLocked(State, playerId, card, definition)) return DuelError.LockedByEffect;
        if (definition.Cost is TributeSelfCost && RuleLocked(State, LockedAction.BeTributed, playerId, card, MonsterOf(State, card))) return DuelError.LockedByEffect;
        foreach (var condition in definition.Conditions)
        {
            bool met = condition switch
            {
                FreeMonsterZonesCondition free => player.MonsterZones.Count(m => m is null) >= free.Count,
                ControlsNoMonstersCondition => player.MonsterZones.All(m => m is null),
                ControlsMonstersCondition controls => ControlsMonsters(State, playerId, controls),
                TurnTimingCondition turn => (turn.Owner == TurnOwner.Either || turn.Owner == TurnOwner.Own == (State.ActivePlayer == playerId)) &&
                    (!turn.MainPhaseOnly || State.Phase is DuelPhase.Main1 or DuelPhase.Main2),
                // A Pendulum scale activation counts as a Spell activation; monster/trigger effects never do.
                RespondsToActivationCondition responds => preceding is not null && (responds.Category is not { } category ||
                    (preceding.IsPendulumScale ? CardCategory.Spell : rules[preceding.Activation.Spell.GameCardId].Category) == category),
                RespondsToSpellActivationCondition => preceding is { Trigger: null } &&
                    (preceding.IsPendulumScale || rules[preceding.Activation.Spell.GameCardId].Category == CardCategory.Spell),
                NoSummonThisTurnCondition => !State.SummonHistory.Any(h => h.Player == playerId &&
                    h.TurnNumber == State.TurnNumber && h.Method != SummonMethod.Set),
                OpponentAttackDeclaredCondition => State.Battle is { Window.Timing: BattleTiming.AttackDeclaration, Attack: { Cancellation: null } attack } &&
                    attack.Attacker.Player != playerId && attack.Attacker.IsStillPresent(State),
                MonsterSummonedCondition summoned => SummonedMatches(State, playerId, summoned, definition.Conditions).Any(),
                // Read by SummonedMatches together with the summon condition.
                TributeCountCondition or XyzSummonedCondition => true,
                DirectAttackCondition => State.Battle?.Attack is { Defender: null },
                RespondsToDamageCondition damages => preceding is not null && (!damages.ByOpponent || preceding.Activation.Player != playerId) &&
                    // A scale activation, effect Summon or older trigger link has no definition, and inflicts no damage.
                    (preceding.Trigger?.Profile.Definition ?? effectsById.GetValueOrDefault(preceding.Activation.EffectId)) is { } answered &&
                    WouldDamage(answered, preceding.Activation.Player, playerId),
                OpponentHandAtLeastCondition hand => State.GetPlayer(Other(playerId)).Hand.Count >= hand.Count,
                PhaseCondition phase => State.Phase == phase.Phase,
                LifePointsBehindCondition behind => (long)State.GetPlayer(Other(playerId)).LifePoints - player.LifePoints >= behind.Amount,
                NoSpecialSummonThisTurnCondition => !State.SummonHistory.Any(h => h.Player == playerId && h.TurnNumber == State.TurnNumber &&
                    h.Status != SummonStatus.Negated && h.Method is SummonMethod.SpecialEffect or SummonMethod.SpecialProcedure or SummonMethod.Token or SummonMethod.Pendulum),
                BattleTimingCondition timing => State.Battle is { } current && timing.Windows.HasFlag(current.Window.Timing),
                MonsterWouldBeSummonedCondition would => preceding is null
                    ? State.PendingChain is null && State.PendingSummon is { Negated: false } attempt && (!would.ByOpponent || attempt.Player != playerId) &&
                        (would.Group || attempt.PendulumGroup.Count <= 1) &&
                        would.Kinds.HasFlag(attempt.IsFlipSummon ? SummonKinds.Flip : attempt.IsSpecialSummon ? SummonKinds.Special : SummonKinds.Normal)
                    : would.OrSpellTrapActivation && preceding is { Trigger: null } &&
                        (preceding.IsPendulumScale || rules[preceding.Activation.Spell.GameCardId].Category is CardCategory.Spell or CardCategory.Trap),
                _ => false
            };
            if (!met) return DuelError.ActivationConditionNotMet;
        }
        // An attacker or defender target needs that combatant on the field and not destroyed by battle.
        if (definition.FieldTargets is [AttackingMonsterTarget or DefendingMonsterTarget] && LiveCombatant(State, definition) is null)
            return DuelError.ActivationConditionNotMet;
        // A counted amount needs something to count when activated (it is read again at resolution).
        if (definition.Counts.Any(c => c.Count is not (EffectCount.DieRoll or EffectCount.Heads or EffectCount.MilledLevel) && CountFor(State, playerId, c.Count, c.Filter) == 0))
            return DuelError.ActivationConditionNotMet;
        if (definition.SummonsMonsters && IsSummonLocked(State, playerId)) return DuelError.SummonLocked;
        if (definition.Cost is PayLifePointsCost pay && player.LifePoints <= pay.Amount) return DuelError.InsufficientLifePoints;
        if (definition.Cost is TributeCost && !player.MonsterZones.Any(m => m is not null)) return DuelError.DecisionUnavailable;
        if (definition.Cost is DiscardCost discards && player.Hand.Count(c => c != card && DiscardQualifies(c, discards.Filter)) < discards.Count) return DuelError.DecisionUnavailable;
        if (definition.Cost is RemoveCounterCost remove && !player.MonsterZones.Any(m => m?.Card == card && m.Counters >= remove.Count)) return DuelError.DecisionUnavailable;
        if (definition.Steps.Any(s => s is UnionEquipStep) && player.SpellTrapFields.All(f => f is not null)) return DuelError.DecisionUnavailable;
        if (definition.Cost is DetachCost detach && !player.MonsterZones.Any(m => m?.Card == card && m.XyzMaterials.Count >= detach.Count)) return DuelError.DecisionUnavailable;
        if (definitionDecisions.TryGetValue(definition.EffectId, out var sequence))
        {
            // Simulate one pick per field target so several targets need distinct cards.
            List<DecisionAnswer> simulated = [];
            var preview = DiscardPreview(playerId, card, definition) ?? TributePreview(card, definition);
            for (int i = 0; i < sequence.Steps.Count && !sequence.Steps[i].Optional; i++)
            {
                var decision = CreateDecision(sequence, i, simulated, DecisionExclusion(definition, card, i), playerId,
                    sequence.Steps[i].Kind == DecisionKind.Card ? null : preview);
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
                ExcavateStep excavate => player.Deck.Count < excavate.Count,
                SwapChosenMonstersStep => !Swappable(State, playerId).Any() || !Swappable(State, Other(playerId)).Any(),
                AddFromDeckToHandStep search => !DeckMatches(State, playerId, search.Filter).Any(),
                AddFromGraveyardToHandStep add => !player.Graveyard.Any(c => MatchesFilter(c, add.Filter, inGraveyard: true)),
                SendFromDeckToGraveyardStep send => !DeckMatches(State, playerId, send.Filter).Any(),
                DestroyAllStep all => !FieldMatches(State, playerId, all.Kind, all.Side, all.Filter, all.IncludeSelf ? null : card.InstanceId).Any(t => all.Cards is null || MatchesFilter(t.Card, all.Cards)),
                ReturnAllToHandStep all => !FieldMatches(State, playerId, all.Kind, all.Side, FieldCardFilter.Any, card.InstanceId).Any(),
                ChangeAllToDefenseStep all => !FieldMatches(State, playerId, FieldCardKind.Monster, all.Side, FieldCardFilter.AttackPosition, null).Any(),
                DestroyExtremeMonsterStep extreme => ExtremeCandidates(State, playerId, extreme).Length == 0,
                SpecialSummonSelfStep self => !SelfSummonPossible(playerId, card, definition, self),
                BanishAllStep all => !FieldMatches(State, playerId, FieldCardKind.Monster, all.Side, all.Filter, null).Any(),
                _ => false
            };
            if (unavailable) return DuelError.DecisionUnavailable;
        }
        return ChosenDiscardsAvailable(definition, playerId, card) ? DuelError.None : DuelError.DecisionUnavailable;
    }

    private DuelActionResult ActivateSetDefinition(int zone, int effectIndex = 0)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or > DuelPlayerState.FieldZoneIndex) return Reject(DuelError.InvalidSpellTrapZone);
        var source = State.GetPlayer(State.ActivePlayer).SpellTrapAt(zone);
        if (source is { IsFaceUp: false } && summonEffects.TryGetValue(source.Card.GameCardId, out var summon))
            return ActivateSummonEffect(source.Card, zone, summon, source);
        if (source is null || source.IsFaceUp || !EffectAt(source.Card.GameCardId, effectIndex, out var definition))
            return Reject(DuelError.UnsupportedSpellEffect);
        if (DefinitionActivationError(definition, ActivationContext.OwnSetCard, State.ActivePlayer, source.Card, source) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, State.ActivePlayer, source.Card, zone, definition, source, null, []);
    }

    /// <summary>Hand activation by the turn player in an open Main Phase state (caller checked the phase).</summary>
    private DuelActionResult ActivateDefinition(CardInstance card, int zone, EffectDefinition definition)
    {
        if ((definition.Kind == EffectKind.FieldSpell) != (zone == DuelPlayerState.FieldZoneIndex)) return Reject(DuelError.InvalidSpellTrapZone);
        if (definition.Kind != EffectKind.FieldSpell && State.GetPlayer(State.ActivePlayer).SpellTrapFields[zone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
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
        if (!EffectAt(card.GameCardId, command.EffectIndex, out var definition)) return Reject(DuelError.UnsupportedBattleResponse);
        if (player.Id != State.ActivePlayer) return Reject(DuelError.ActivationTimingNotAllowed);
        if (player.SpellTrapFields[command.SpellTrapZone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        if (DefinitionActivationError(definition, ActivationContext.BattleHand, player.Id, card, null) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, player.Id, card, command.SpellTrapZone, definition, null, null, []);
    }

    /// <summary>A set Counter Trap answering a Summon that has not happened yet (caller checked responder and decision ID).</summary>
    private DuelActionResult BeginSummonDefinition(PlayerId actor, int zone, FieldSpellTrap source, EffectDefinition definition)
    {
        if (DefinitionActivationError(definition, ActivationContext.SummonWindow, actor, source.Card, source) is not DuelError.None and var error)
            return Reject(error);
        return CommitDefinition(State, actor, source.Card, zone, definition, source, null, []);
    }

    private bool? timedCombatModifiers;
    private bool HasTimedCombatModifiers => timedCombatModifiers ??= allEffects.Any(d => d.Steps.Any(s => s is ModifyCombatantStatsStep or ModifyTargetsUntilEndStep));

    private bool? summonNegators;
    private bool SummonNegators => summonNegators ??= allEffects.Any(d => d.Conditions.Any(c => c is MonsterWouldBeSummonedCondition));

    /// <summary>
    /// True when a Summon attempt pauses for responses: its card opted in, or the responder of
    /// <paramref name="attempt"/> has a set Counter Trap that can negate it.
    /// </summary>
    private bool OpensSummonWindow(DuelState attempt, uint cardId, bool optedIn = true)
    {
        if (unnegatableSummons.Contains(cardId)) return false;
        if (optedIn && summonWindows.Contains(cardId)) return true;
        if (!SummonNegators) return false;
        var saved = State;
        State = attempt;
        try
        {
            var responder = attempt.PendingSummon!.Responder;
            return attempt.GetPlayer(responder).SpellTrapFields.Any(source => source is not null &&
                effectsByCard.TryGetValue(source.Card.GameCardId, out var effects) && effects.Any(definition =>
                    definition.Conditions.Any(c => c is MonsterWouldBeSummonedCondition) &&
                    DefinitionActivationError(definition, ActivationContext.SummonWindow, responder, source.Card, source) == DuelError.None));
        }
        finally { State = saved; }
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
        if (!EffectAt(card.GameCardId, command.EffectIndex, out var definition)) return Reject(DuelError.UnsupportedChainResponse);
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
        List<DuelEvent> replaced = [.. lead];
        // A new Field Spell sends its controller's old one to the Graveyard at placement; the old card is not destroyed.
        if (zone == DuelPlayerState.FieldZoneIndex && source is null && state.GetPlayer(playerId).FieldSpell is { } old)
            state = ReplaceFieldSpell(state, playerId, old, replaced);
        var player = state.GetPlayer(playerId);
        long placement = source?.PlacementId ?? checked(State.Revision + 1);
        FieldSpellTrap card_ = source is null ? new(card, true, placement) : source with { IsFaceUp = true };
        var placed = state.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != card.InstanceId)).WithSpellTrapAt(zone, card_));
        var activated = new SpellActivated(playerId, definition.EffectId, card, zone);
        var (paid, head) = PayLifePointCost(placed, playerId, definition, [.. replaced, activated]);
        return ContinueCommit(paid, head, playerId, card, zone, definition, placement, chain);
    }

    private static (DuelState State, DuelEvent[] Head) PayLifePointCost(DuelState state, PlayerId playerId, EffectDefinition definition, DuelEvent[] head)
    {
        if (definition.Cost is not (PayLifePointsCost or PayHalfLifePointsCost)) return (state, head);
        var payer = state.GetPlayer(playerId);
        int amount = definition.Cost is PayLifePointsCost pay ? pay.Amount : payer.LifePoints / 2;
        return (state.With(player: payer.With(lifePoints: payer.LifePoints - amount)),
            [.. head, new LifePointsPaid(playerId, amount, payer.LifePoints, payer.LifePoints - amount, definition.EffectId)]);
    }

    /// <summary>A monster may Tribute itself as a cost; every other choice excludes the source.</summary>
    private static int? DecisionExclusion(EffectDefinition definition, CardInstance card, int step) =>
        definition.IsMonsterEffect && definition.Cost is TributeCost && step < definition.CostTargetSteps ? null : card.InstanceId;

    /// <summary>Asks for the remaining cost and target decisions, or adds the link at once.</summary>
    private DuelActionResult ContinueCommit(DuelState placed, DuelEvent[] head, PlayerId playerId, CardInstance card, int zone,
        EffectDefinition definition, long placement, PendingChain? chain)
    {
        if (definitionDecisions.TryGetValue(definition.EffectId, out var sequence))
        {
            var decision = CreateDecision(sequence, 0, [], DecisionExclusion(definition, card, 0), playerId, placed)!;
            var pending = new PendingSpellActivation(playerId, definition.EffectId, card, zone,
                ResponseToLink: chain?.Links[^1].Number ?? 0, SourcePlacementId: placement) { IsMonsterEffect = definition.IsMonsterEffect };
            return Accept(placed.WithActivation(pending).WithDecision(decision), [.. head, new DecisionRequested(decision)]);
        }
        return AddDefinitionLink(placed, new(playerId, definition.EffectId, card, zone, null, null, AutomaticTargets(State, playerId, definition)), definition, placement,
            chain, [.. head]);
    }

    /// <summary>The combatant or just-Summoned monsters, bound automatically without a decision; null for other targets.</summary>
    private IReadOnlyList<FieldCardTarget>? AutomaticTargets(DuelState state, PlayerId playerId, EffectDefinition definition) => definition.FieldTargets switch
    {
        [AttackingMonsterTarget or DefendingMonsterTarget] when LiveCombatant(state, definition) is { } combatant =>
            [new(combatant.Player, FieldLocation.MonsterZone, combatant.Zone, combatant.Card, combatant.PlacementId)],
        [SummonedMonsterTarget] => [.. SummonedMatches(state, playerId, definition.Conditions.OfType<MonsterSummonedCondition>().First(), definition.Conditions)],
        _ => null
    };

    /// <summary>Cost then target answers; the final answer adds the link. The cost stays paid if a later step waits.</summary>
    private DuelActionResult AnswerDefinitionDecision(PendingDecision pending, DecisionAnswer answer, DecisionAnswer[] answers)
    {
        var activation = State.PendingSpellActivation!;
        var definition = effectsById[activation.EffectId];
        var sequence = definitionDecisions[definition.EffectId];
        var player = State.GetPlayer(activation.Player);
        var chain = State.PendingChain;
        if (pending.SequenceId != sequence.Id || pending.Player != player.Id || !ActivationSourceIntact(activation, player, definition) ||
            (chain?.Links[^1].Number ?? 0) != activation.ResponseToLink)
            return Reject(DuelError.InvalidEffectState);
        var next = State;
        List<DuelEvent> events = [new DecisionAnswered(player.Id, pending.SequenceId, pending.DecisionId, answer)];
        if (pending.Kind == DecisionKind.Card)
        {
            var cost = player.Hand.SingleOrDefault(c => c.InstanceId == answer.CardInstanceId);
            if (cost is null || definition.Cost is not DiscardCost discard || pending.StepIndex >= discard.Count || cost == activation.Spell || !DiscardQualifies(cost, discard.Filter))
                return Reject(DuelError.InvalidEffectState);
            activation = activation with { DiscardedCost = activation.DiscardedCost ?? cost };
            next = next.With(player: player.With(hand: player.Hand.Where(c => c != cost),
                graveyard: player.Graveyard.Append(cost))).WithActivation(activation);
            events.Add(new ActivationCostPaid(player.Id, definition.EffectId, cost));
        }
        else if (definition.Cost is TributeCost tributes && pending.StepIndex < tributes.Count)
        {
            if (answer.FieldTarget is not { Location: FieldLocation.MonsterZone } tribute || tribute.Player != player.Id || !tribute.IsStillPresent(next))
                return Reject(DuelError.InvalidEffectState);
            next = SendFieldCardToGraveyard(RemovePlacement(next, tribute), tribute.Card,
                new MonsterTributed(new(tribute.Player, tribute.Zone, tribute.Card, tribute.PlacementId)), events);
        }
        else if (definition.Cost is BanishFromGraveyardCost banish && pending.StepIndex < banish.Count)
        {
            if (answer.FieldTarget is not { Location: FieldLocation.Graveyard } banished || banished.Player != player.Id || !banished.IsStillPresent(next))
                return Reject(DuelError.InvalidEffectState);
            next = MoveZoneCard(next, banished, toBanished: true, definition.EffectId, events);
        }
        // A paid cost goes where it really goes before the next choice is offered.
        next = ApplyGraveyardRedirects(next, events);
        if (pending.StepIndex + 1 < sequence.Steps.Count)
        {
            var decision = CreateDecision(sequence, pending.StepIndex + 1, answers, from: next);
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
        if (pending.Player != player.Id || !ActivationSourceIntact(activation, player, effectsById[activation.EffectId]))
            return Reject(DuelError.InvalidEffectState);
        return CompleteDefinitionDecisions(pending, pending.Answers.ToArray(), State, activation, []);
    }

    /// <summary>The activating source is still in place, unless a monster paid its cost by leaving the field.</summary>
    private static bool ActivationSourceIntact(PendingSpellActivation activation, DuelPlayerState player, EffectDefinition definition) =>
        definition.Origin != EffectOrigin.Field || (activation.IsMonsterEffect
            ? definition.Cost is TributeSelfCost or TributeCost ||
                player.MonsterZones[activation.SpellTrapZone] is { } monster && monster.Card == activation.Spell && monster.PlacementId == activation.SourcePlacementId
            : player.SpellTrapAt(activation.SpellTrapZone) is { IsFaceUp: true } source && source.Card == activation.Spell && source.PlacementId == activation.SourcePlacementId);

    private DuelActionResult CompleteDefinitionDecisions(PendingDecision pending, DecisionAnswer[] answers, DuelState next,
        PendingSpellActivation activation, List<DuelEvent> events)
    {
        var definition = effectsById[activation.EffectId];
        var result = new DecisionSequenceResult(pending, answers);
        var target = answers.SingleOrDefault(a => a.Kind == DecisionKind.SpellTrapTarget)?.Target;
        var receipt = new CommittedActivation(activation.Player, definition.EffectId, activation.Spell,
            activation.SpellTrapZone, activation.DiscardedCost, target,
            answers.Skip(definition.CostTargetSteps).Any(a => a.FieldTarget is not null)
                ? Array.AsReadOnly(answers.Skip(definition.CostTargetSteps).Select(a => a.FieldTarget).OfType<FieldCardTarget>().ToArray())
                // A cost was chosen but the targets are bound automatically.
                : AutomaticTargets(next, activation.Player, definition));
        events.Add(new DecisionSequenceCompleted(result));
        return AddDefinitionLink(next.WithDecision(null, result).WithActivation(null), receipt, definition,
            activation.SourcePlacementId, State.PendingChain, events);
    }

    /// <summary>Starts a chain or appends the next link, applies any summon lock, and asks the other player to respond.</summary>
    private DuelActionResult AddDefinitionLink(DuelState state, CommittedActivation receipt, EffectDefinition definition,
        long placement, PendingChain? chain, List<DuelEvent> events)
    {
        var link = new ChainLink((chain?.Links.Count ?? 0) + 1, receipt, placement,
            definition.Origin != EffectOrigin.Field ? ChainOperation.ZoneEffect : definition.IsMonsterEffect ? ChainOperation.MonsterEffect : definition.IsFaceUpEffect ? ChainOperation.FieldEffect : ChainOperation.Definition, definition.SpellSpeed,
            TargetLinkNumber: definition.Steps.Any(s => s is NegateActivationStep or NegateEffectStep) || chain is not null && definition.Steps.Any(s => s is NegateSummonStep)
                ? chain!.Links[^1].Number : null,
            SummonAttemptId: chain is null && definition.Steps.Any(s => s is NegateSummonStep) ? State.PendingSummon?.AttemptId : null);
        var pending = new PendingChain(checked(State.Revision + 1), (chain?.Links ?? []).Append(link), Other(receipt.Player));
        // A declaring effect declares as it is activated, before either player can respond.
        if (definition.Declares is { } declares && !definition.DeclaresAtResolution) pending = AskDeclaration(pending, link, definition, receipt.Player, DeclarationChoices(definition, receipt, declares));
        var next = state.WithCommittedActivation(receipt).WithChain(pending);
        if (definition.NameLimit is { } named) next = RecordNamedUse(next, receipt.Player, named);
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
        events.Add(pending.Declaration is { } declaring ? new DeclarationRequested(declaring) : new ChainResponseRequested(pending.Responder, pending.DecisionId));
        return Accept(next, events.ToArray());
    }

    /// <summary>Zones holding set definition cards the player could activate in an open window right now.</summary>
    private IEnumerable<int> OpenWindowActivations(PlayerId playerId)
    {
        var player = State.GetPlayer(playerId);
        for (int zone = 0; zone < 5; zone++)
            if (player.SpellTrapFields[zone] is { } source && effectsByCard.TryGetValue(source.Card.GameCardId, out var effects) &&
                effects.Any(definition => DefinitionActivationError(definition, ActivationContext.OpenWindow, playerId, source.Card, source) == DuelError.None))
                yield return zone;
    }

    /// <summary>
    /// The turn player's phase-change request. If the non-turn player has a legal open-window activation, the
    /// change is paused for them to activate or pass; otherwise it applies immediately (unchanged behaviour).
    /// </summary>
    private DuelActionResult RequestPhase(DuelPhase target)
    {
        if (PhaseChangeError(target) is not DuelError.None and var error) return Reject(error);
        if (State.Phase == DuelPhase.Standby && AnyUpkeepDue(State.ActivePlayer)) return Reject(DuelError.UpkeepPending);
        var responder = Other(State.ActivePlayer);
        if (!OpenWindowActivations(responder).Any() && !QuickEffectReady(responder)) return Advance(target);
        var window = new PendingOpenWindow(checked(State.Revision + 1), responder, target);
        return Accept(State.WithBookkeeping(State.Bookkeeping with { OpenWindow = window }),
            new OpenWindowRequested(responder, window.DecisionId, target));
    }

    /// <summary>Pass applies the requested phase change; an activation starts a chain and the request lapses.</summary>
    private DuelActionResult ExecuteOpenWindow(DuelCommand command)
    {
        var window = State.OpenWindow!;
        long id = command switch { PassChainResponse c => c.DecisionId, ActivateChainResponse c => c.DecisionId, ActivateMonsterChainResponse c => c.DecisionId, ActivateZoneEffectResponse c => c.DecisionId, ActivateFieldEffectResponse c => c.DecisionId, _ => -1 };
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
        if (command is ActivateMonsterChainResponse quick) return BeginMonsterEffect(quick.Player, quick.MonsterZone, null, cleared, window: true, detach: quick.DetachInstanceIds, effectIndex: quick.EffectIndex);
        if (command is ActivateZoneEffectResponse zoneQuick) return BeginZoneEffect(zoneQuick.Player, zoneQuick.CardInstanceId, zoneQuick.EffectIndex, null, cleared, window: true);
        if (command is ActivateFieldEffectResponse fieldQuick) return BeginFieldEffect(fieldQuick.Player, fieldQuick.SpellTrapZone, fieldQuick.EffectIndex, null, cleared, window: true);
        var response = (ActivateChainResponse)command;
        if (response.SpellTrapZone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var source = State.GetPlayer(window.Responder).SpellTrapFields[response.SpellTrapZone];
        if (source is null || !EffectAt(source.Card.GameCardId, response.EffectIndex, out var definition))
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
        try
        {
            resolvingDeclaration = LinkDefinition(link).Declares is { } kind && link.Declared is { } value ? (kind, value) : null;
            return ResolveDefinitionStepsCore(state, links, link, start, choice, events);
        }
        finally { replacementContext = null; resolvingDeclaration = null; identityView = null; }
    }

    private (DuelState State, PendingDefinitionChoice? Pause) ResolveDefinitionStepsCore(DuelState state, ChainLink[] links,
        ChainLink link, int start, DuelCommand? choice, List<DuelEvent> events)
    {
        var definition = LinkDefinition(link);
        var controller = link.Activation.Player;
        for (int i = start; i < definition.Steps.Count; i++)
        {
            var step = definition.Steps[i];
            identityView = state;
            replacementContext = null;
            // Optional replacements are answered before the step destroys anything; nothing can happen in between.
            if (DestructionBatch(state, links, link, definition, step, controller) is { Count: > 0 } batch)
            {
                if (choice is ReplacementAnswers answers)
                {
                    replacementContext = new(batch, answers.Decisions);
                    choice = null;
                }
                else if (OptionalOffers(state, batch, DestructionCause.Effect) is { Count: > 0 } offers)
                    return (state, new PendingReplacementChoice(new(checked(State.Revision + 1), offers, []), definition.EffectId, link.Number, i));
                else replacementContext = new(batch, []);
            }
            if (step is SpecialSummonSelfStep self)
            {
                var card = link.Activation.Spell;
                var from = definition.Origin == EffectOrigin.Hand ? DecisionCardSource.Hand : DecisionCardSource.Graveyard;
                if (!SpecialSourceCards(state.GetPlayer(controller), from).Contains(card))
                {
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SummonUnavailable));
                    break;
                }
                if (choice is not ChooseDefinitionSummon place)
                    return (state, new PendingDefinitionSummon(checked(State.Revision + 1), controller, definition.EffectId, link.Number, i,
                        [new(card, from, controller)], Enumerable.Range(0, 5).Where(z => state.GetPlayer(controller).MonsterZones[z] is null), self.Position));
                state = PlaceSpecialSummon(state, controller, card, place.MonsterZone, place.Position, from, events);
                choice = null;
                continue;
            }
            if (step is DiscardCardsStep chosenDiscard)
            {
                var discarded = ResolveChosenDiscards(state, link, definition, chosenDiscard, i, choice, events);
                state = discarded.State;
                if (discarded.Pause is not null) return discarded;
                choice = null;
                continue;
            }
            if (step is StopUnlessRollStep or StopUnlessHeadsStep)
            {
                var last = state.Bookkeeping.LastRandom;
                if (step is StopUnlessRollStep roll ? last is not null && roll.Values.Contains(last.Die) : last is not null && last.Heads >= ((StopUnlessHeadsStep)step).Minimum)
                    continue;
                events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                break;
            }
            if (step is ChooseFromOpponentHandStep fromHand)
            {
                var options = state.GetPlayer(Other(controller)).Hand.Where(c => fromHand.Filter is null || MatchesFilter(c, fromHand.Filter)).ToArray();
                if (options.Length == 0) continue;
                if (choice is not ChooseDefinitionCard taken)
                    return (state, new PendingDefinitionCardChoice(checked(State.Revision + 1), controller, definition.EffectId, link.Number, i, options));
                state = MoveFromOpponentHand(state, controller, options.FirstOrDefault(c => c.InstanceId == taken.CardInstanceId) ?? options[0], fromHand.To,
                    definition.EffectId, events);
                choice = null;
                continue;
            }
            if (step is StopUnlessOwnTurnStep)
            {
                if (state.ActivePlayer == controller) continue;
                events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                break;
            }
            if (step is StopUnlessSpecialSummonedStep)
            {
                if (link.Trigger?.Source is { } summonedSource && WasSpecialSummoned(state, summonedSource)) continue;
                events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                break;
            }
            if (step is StopUnlessActivatedThisTurnStep)
            {
                if (ScaleActivatedThisTurn(state, controller, link.Activation.SpellTrapZone, link.Activation.Spell, link.SourcePlacementId)) continue;
                events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                break;
            }
            if (step is StopUnlessHandHoldsStep holds)
            {
                if (HandHolds(state, controller, holds)) continue;
                events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                break;
            }
            if (step is StopUnlessMilledStep milled)
            {
                if (state.Bookkeeping.LastRandom?.Milled is { } top && MatchesFilter(top, milled.Filter)) continue;
                if (milled.DestroySourceOtherwise) state = DestroyInHand(state, controller, link.Activation.Spell, definition.EffectId, events);
                events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                break;
            }
            if (step is TakeExcavatedStep excavated)
            {
                var deck = state.GetPlayer(controller).Deck;
                var options = (state.Bookkeeping.LastRandom?.Excavated ?? []).Where(c => deck.Contains(c) && (excavated.Filter is null || MatchesFilter(c, excavated.Filter))).ToArray();
                if (options.Length == 0) continue;
                if (choice is not ChooseDefinitionCard taken)
                    return (state, new PendingDefinitionCardChoice(checked(State.Revision + 1), controller, definition.EffectId, link.Number, i, options));
                state = TakeFromDeck(state, controller, options.FirstOrDefault(c => c.InstanceId == taken.CardInstanceId) ?? options[0], definition.EffectId, events);
                choice = null;
                continue;
            }
            if (step is OpponentSendsFromDeckStep send)
            {
                var options = state.GetPlayer(Other(controller)).Deck.Where(c => MatchesFilter(c, send.Filter)).ToArray();
                if (options.Length == 0)
                {
                    // The activating player may look at the opponent's Deck to confirm (Vampire Lord, OCG ruling).
                    events.Add(new CardsLookedAt(controller, Other(controller), state.GetPlayer(Other(controller)).Deck, definition.EffectId));
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                    continue;
                }
                if (choice is not ChooseDefinitionCard sent)
                    return (state, new PendingDefinitionCardChoice(checked(State.Revision + 1), Other(controller), definition.EffectId, link.Number, i, options));
                state = OpponentSendsFromDeck(state, controller, options.FirstOrDefault(c => c.InstanceId == sent.CardInstanceId) ?? options[0], definition.EffectId, events);
                choice = null;
                continue;
            }
            if (step is TakeChosenControlStep takeChosen)
            {
                (state, var pick) = TakeChosenControl(state, link, definition, i, takeChosen, choice, events);
                if (pick is not null) return (state, pick);
                choice = null;
                continue;
            }
            if (step is SwapChosenMonstersStep)
            {
                (state, var swap, bool none) = SwapChosen(state, link, definition, i, choice, events);
                if (swap is not null) return (state, swap);
                choice = null;
                if (none) break;
                continue;
            }
            if (step is OpponentCallsRandomHandCardStep)
            {
                (state, var call, bool stop) = CallRandomHandCard(state, link, definition, i, choice, events);
                if (call is not null) return (state, call);
                choice = null;
                if (stop) break;
                continue;
            }
            if (step is StopUnlessNoOwnMonstersStep)
            {
                if (state.GetPlayer(controller).MonsterZones.All(m => m is null)) continue;
                events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                break;
            }
            if (step is AddFromFaceUpExtraDeckStep extra)
            {
                var options = state.GetPlayer(controller).FaceUpExtraDeck.Where(c => MatchesFilter(c, extra.Filter)).ToArray();
                if (options.Length == 0)
                {
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                    continue;
                }
                if (choice is not ChooseDefinitionCard taken)
                    return (state, new PendingDefinitionCardChoice(checked(State.Revision + 1), controller, definition.EffectId, link.Number, i, options));
                var card = options.FirstOrDefault(c => c.InstanceId == taken.CardInstanceId) ?? options[0];
                var holder = state.GetPlayer(controller);
                state = state.With(player: holder.With(faceUpExtraDeck: holder.FaceUpExtraDeck.Where(c => c != card), hand: holder.Hand.Append(card)));
                events.Add(new CardAddedFromExtraDeck(controller, card, definition.EffectId));
                choice = null;
                continue;
            }
            if (step is AddFromGraveyardToHandStep fromGraveyard)
            {
                var options = state.GetPlayer(controller).Graveyard.Where(c => MatchesFilter(c, fromGraveyard.Filter, inGraveyard: true)).ToArray();
                if (options.Length == 0)
                {
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                    continue;
                }
                if (choice is not ChooseDefinitionCard recovered)
                    return (state, new PendingDefinitionCardChoice(checked(State.Revision + 1), controller, definition.EffectId, link.Number, i, options));
                state = AddFromGraveyard(state, controller, options.FirstOrDefault(c => c.InstanceId == recovered.CardInstanceId) ?? options[0], definition.EffectId, events);
                choice = null;
                continue;
            }
            if (step is AddFromDeckOrGraveyardStep either)
            {
                var owned = state.GetPlayer(controller);
                var options = DeckMatches(state, controller, either.Filter).Concat(owned.Graveyard.Where(c => MatchesFilter(c, either.Filter, inGraveyard: true))).ToArray();
                if (options.Length == 0)
                {
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                    continue;
                }
                if (choice is not ChooseDefinitionCard taken)
                    return (state, new PendingDefinitionCardChoice(checked(State.Revision + 1), controller, definition.EffectId, link.Number, i, options));
                var card = options.FirstOrDefault(c => c.InstanceId == taken.CardInstanceId) ?? options[0];
                state = owned.Graveyard.Contains(card)
                    ? MoveZoneCardToHand(state, new(controller, FieldLocation.Graveyard, -1, card, 0), definition.EffectId, events)
                    : MoveChosenDeckCard(state, controller, card.InstanceId, toHand: true, definition.EffectId, events);
                choice = null;
                continue;
            }
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
            else if (step is AttachSourceStep attachSource)
            {
                // The bound monster only has to still be there; it may have been turned face-down.
                if (BoundTarget(link, attachSource.Target) is { } host && host.IsStillPresent(state)) state = AttachSourceAsMaterial(state, link, host, events);
                else events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
            }
            else if (step is DestroyTargetAtStep destroyAt)
            {
                if (BoundTarget(link, destroyAt.Target) is not { } doomed || !QualifyingTargets(state, link, definition).Contains(doomed))
                {
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
                    break;
                }
                state = DestroyFieldCard(state, doomed, events);
                // "Then": the next part happens only if this one did.
                if (doomed.IsStillPresent(state)) break;
            }
            else if (step is GiveTargetControlStep giveAt)
            {
                if (BoundTarget(link, giveAt.Target) is { } given && given.Player == controller && QualifyingTargets(state, link, definition).Contains(given))
                    state = ChangeControl(state, link, given, Other(controller), ControlDuration.Permanent, definition.EffectId, events);
            }
            else if (step is DetachAllStep detachAll)
            {
                if (BoundTarget(link, detachAll.Target) is not { } bound || !bound.IsStillPresent(state) ||
                    state.GetPlayer(bound.Player).MonsterZones[bound.Zone] is not { XyzMaterials.Count: > 0 })
                {
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
                    break;
                }
                state = DetachAll(state, bound, events);
            }
            else if (step is RankUpStep rankUp)
            {
                if (BoundTarget(link, 0) is not { } bound || !bound.IsStillPresent(state) ||
                    state.GetPlayer(bound.Player).MonsterZones[bound.Zone]!.Position == BattlePosition.FaceDownDefense)
                {
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
                    break;
                }
                var options = RankUpOptions(state, bound, rankUp).ToArray();
                var picked = choice is ChooseDefinitionSummon place ? options.FirstOrDefault(c => c.InstanceId == place.CardInstanceId) : null;
                if (options.Length == 0 || choice is ChooseDefinitionSummon && picked is null)
                {
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SummonUnavailable));
                    break;
                }
                if (picked is null)
                    return (state, new PendingDefinitionSummon(checked(State.Revision + 1), controller, definition.EffectId, link.Number, i,
                        options.Select(c => new DefinitionSummonOption(c, DecisionCardSource.ExtraDeck, controller)), [bound.Zone]));
                state = RankUpSummon(state, bound, picked, ((ChooseDefinitionSummon)choice!).Position, definition.EffectId, events);
                choice = null;
            }
            else if (step is TakeOpponentMaterialStep)
            {
                var pool = state.GetPlayer(Other(controller)).MonsterZones.Where(m => m is not null).SelectMany(m => m!.XyzMaterials).ToArray();
                if (BoundTarget(link, 0) is not { } summonedAt || state.GetPlayer(controller).MonsterZones[summonedAt.Zone] is not { } summoned || pool.Length == 0) continue;
                if (pool.Length > 1 && choice is not ChooseDefinitionCard)
                    return (state, new PendingDefinitionCardChoice(checked(State.Revision + 1), controller, definition.EffectId, link.Number, i, pool));
                var taken = choice is ChooseDefinitionCard take ? pool.FirstOrDefault(c => c.InstanceId == take.CardInstanceId) ?? pool[0] : pool[0];
                state = MoveXyzMaterial(state, Other(controller), taken, new(controller, FieldLocation.MonsterZone, summonedAt.Zone, summoned.Card, summoned.PlacementId),
                    definition.EffectId, events);
                choice = null;
            }
            else if (step is AttachFromStep attachFrom)
            {
                var host = attachFrom.ToTarget ? BoundTarget(link, 0) : ActivatingMonster(state, link);
                if (host is null || !host.IsStillPresent(state))
                {
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
                    break;
                }
                var candidates = AttachCandidates(state, controller, attachFrom, link.Activation.Spell).ToArray();
                if (candidates.Length == 0)
                {
                    events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                    continue;
                }
                if (choice is not ChooseDefinitionCard chosen)
                    return (state, new PendingDefinitionCardChoice(checked(State.Revision + 1), controller, definition.EffectId, link.Number, i, candidates));
                state = AttachFromZone(state, controller, candidates.FirstOrDefault(c => c.InstanceId == chosen.CardInstanceId) ?? candidates[0], host, definition.EffectId, events);
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
                if (step is SpecialSummonTargetStep revive)
                {
                    if (choice is not ChooseDefinitionSummon place)
                        return (state, new PendingDefinitionSummon(checked(State.Revision + 1), controller, definition.EffectId, link.Number, i,
                            [new(valid[0].Card, DecisionCardSource.Graveyard, valid[0].Player)],
                            Enumerable.Range(0, 5).Where(z => state.GetPlayer(controller).MonsterZones[z] is null), revive.Position));
                    state = PlaceSpecialSummon(RemoveFromZone(state, valid[0]), controller, valid[0].Card, place.MonsterZone, place.Position,
                        DecisionCardSource.Graveyard, events, sourceAlreadyRemoved: true);
                    if (revive.Link == RevivalLink.Material)
                    {
                        var revived = state.GetPlayer(controller).MonsterZones[place.MonsterZone]!;
                        state = AttachSourceAsMaterial(state, link, new(controller, FieldLocation.MonsterZone, place.MonsterZone, revived.Card, revived.PlacementId), events);
                    }
                    else if (revive.Link != RevivalLink.None) state = AttachRevival(state, link, place.MonsterZone, revive.Link);
                    choice = null;
                    continue;
                }
                if (step is EquipTargetStep)
                {
                    state = AttachEquip(state, link, valid[0]);
                    continue;
                }
                if (step is TakeControlStep or GiveControlStep)
                {
                    var receiver = step is TakeControlStep ? controller : Other(controller);
                    var duration = step is TakeControlStep take ? take.Duration : ((GiveControlStep)step).Duration;
                    foreach (var target in valid.Where(t => t.Location == FieldLocation.MonsterZone).ToArray())
                        state = ChangeControl(state, link, target, receiver, duration, definition.EffectId, events);
                    continue;
                }
                if (step is ChangeTargetsTraitStep or SetTargetScaleStep)
                {
                    if (link.Declared is int declared)
                        foreach (var target in valid)
                            state = step is ChangeTargetsTraitStep trait ? ChangeTrait(state, target, trait, declared, definition.EffectId, events)
                                : SetScale(state, target, declared, definition.EffectId, events);
                    continue;
                }
                if (step is ChangeTargetsNameStep nameChange)
                {
                    foreach (var target in valid) state = ChangeName(state, target, nameChange, definition.EffectId, events);
                    continue;
                }
                if (step is LockTargetsUntilEndStep lockTargets)
                {
                    foreach (var target in valid.Where(t => t.Location == FieldLocation.MonsterZone))
                        state = AddBoundLock(state, controller, lockTargets.Actions, target, definition.EffectId, events);
                    continue;
                }
                if (step is SwitchControlStep)
                {
                    // Both monsters must still be there, or neither switches.
                    if (valid.Length == 2 && valid[0].Player != valid[1].Player)
                        state = SwitchControl(state, Bound(valid[0]), Bound(valid[1]), definition.EffectId, events);
                    else events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
                    continue;
                }
                if (step is UnionEquipStep)
                {
                    state = EquipUnion(state, link, valid[0], events);
                    continue;
                }
                if (step is PlaceCounterStep counters)
                {
                    state = PlaceCounters(state, valid[0], counters, events);
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
                if (step is DestroyTargetsStep destroy)
                {
                    bool destroyed = false;
                    foreach (var target in valid)
                    {
                        int firstEvent = events.Count;
                        state = DestroyFieldCard(state, target, events);
                        destroyed |= events.Skip(firstEvent).Any(e => e is MonsterDestroyed m && m.Card == target.Card ||
                            e is SpellTrapDestroyed s && s.Card == target.Card);
                    }
                    if (destroy.StopUnlessAnyDestroyed && !destroyed)
                    {
                        events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                        break;
                    }
                    continue;
                }
                foreach (var target in valid)
                    state = step switch
                    {
                        ReturnTargetsToHandStep => ReturnFieldCardToHand(state, target, definition.EffectId, events),
                        ChangeTargetsToDefenseStep => ChangeToDefense(state, target, events),
                        ModifyTargetsUntilEndStep stats => ModifyUntilEnd(state, target, stats, definition.EffectId, events),
                        ModifyTargetsStep lasting => target.Location == FieldLocation.MonsterZone
                            ? ModifyLasting(state, new(target.Player, target.Zone, target.Card, target.PlacementId), lasting.Attack, lasting.Defense, definition.EffectId, events) : state,
                        FlipTargetsFaceDownStep => FlipFaceDown(state, target, events),
                        FlipTargetsFaceUpStep flip => FlipFaceUp(state, target, flip, events),
                        AttachTargetsToSelfStep => AttachMonsterAsMaterial(state, link, target, definition.EffectId, events),
                        BanishFieldTargetsStep => BanishFieldCard(state, target, definition.EffectId, events),
                        DestroyAndBanishTargetsStep => DestroyAndBanishFieldCard(state, target, definition.EffectId, events),
                        ModifyCombatantStatsStep stats => ModifyCombatant(state, link, target, stats, definition.EffectId, events),
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
                if (choice is not (ChooseDefinitionSummon or FinishDefinitionSummon))
                {
                    var options = SpecialSummonOptions(state, controller, summon, link.Activation.Spell).ToArray();
                    if (options.Length == 0)
                    {
                        events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SummonUnavailable));
                        continue;
                    }
                    var zones = Enumerable.Range(0, 5).Where(z => state.GetPlayer(controller).MonsterZones[z] is null);
                    return (state, new PendingDefinitionSummon(checked(State.Revision + 1), controller, definition.EffectId,
                        link.Number, i, options, zones, summon.Position, canFinish: summon.AnyNumber && summon.MinCount == 0));
                }
                state = ApplySpecialSummons(state, link, summon, FinalSummonPicks(state, choice), definition.EffectId, events);
                choice = null;
            }
            else
                state = step switch
                {
                    GainLifePointsStep gain => GainForPlayers(state, controller, gain, definition.EffectId, events),
                    DealDamageStep damage => Scaled(state, controller, damage.Amount, damage.PerCount, damage.CountFilter) is int amount and > 0
                        ? DealEffectDamage(state, Other(controller), amount, definition.EffectId, events) : state,
                    DamageBothPlayersStep both => DamageBothPlayers(state, controller, both, definition.EffectId, events),
                    TakeDamageStep take => DealEffectDamage(state, controller, take.Amount, definition.EffectId, events),
                    DiscardRandomStep discard => DiscardRandom(state, controller, discard, definition.EffectId, events),
                    BanishSourceWhenItLeavesStep => BanishSourceWhenItLeaves(state, link),
                    WeakenEventMonsterStep weaken => WeakenEventMonster(state, link, weaken, definition.EffectId, events),
                    PayDownToGainStep pay => PayDownToGain(state, controller, link, pay, definition.EffectId, events),
                    SetDeclaredLevelStep level => SetDeclaredLevel(state, controller, link, level),
                    ReduceSourceScaleStep reduce => ReduceSourceScale(state, link, reduce, definition.EffectId, events),
                    DestroyAboveSourceScaleStep above => DestroyAboveSourceScale(state, controller, link, above, events),
                    BanishEventMonsterStep => BanishEventMonster(state, link, definition.EffectId, events),
                    BanishSourceCardStep => BanishSourceCard(state, link, definition.EffectId, events),
                    RedirectRespondedDamageStep => RedirectRespondedDamage(state, controller, link),
                    SummonTokensStep tokens => SummonTokens(state, link, tokens, events),
                    ShuffleDeckStep => ShuffleDeck(state, controller, definition.EffectId, events),
                    GrantNormalSummonStep => GrantNormalSummon(state, definition.EffectId, events),
                    // A declared name is fixed when the lock is applied; "until the End of your opponent's next turn" counts from whose turn it is.
                    ApplyRuleLockStep apply => ApplyTimedLock(state, controller, apply.Lock with { Cards = apply.Lock.Cards is { } locked ? ConcreteDeclared(locked) : null },
                        definition.EffectId, events, apply.ThroughOpponentsNextTurn ? state.TurnNumber + (state.ActivePlayer == controller ? 1 : 2) : null),
                    EachPlayerTossesCoinStep coins => EachPlayerTosses(state, controller, coins, definition.EffectId, events),
                    DiceDuelStep dice => DiceDuel(state, controller, dice, definition.EffectId, events),
                    ExcavateStep excavate => Excavate(state, controller, excavate.Count, definition.EffectId, events),
                    ReturnAttackerToHandStep => ReturnAttacker(state, link, definition.EffectId, events),
                    // "Draw ... and skip": if the draw was stopped, nothing is skipped either (Reckless Greed ruling).
                    SkipPhaseStep when definition.Steps.Any(s => s is DrawCardsStep) && RuleLocked(state, LockedAction.Draw, controller, null) => state,
                    SkipPhaseStep skip => AddPhaseSkips(state, controller, skip),
                    EndBattlePhaseStep => EndBattlePhase(state, events),
                    SendSourceToGraveyardStep => SendSourceToGraveyard(state, link, events),
                    OpponentAddsFromDeckStep add => OpponentAddsFromDeck(state, controller, add, definition.EffectId, events),
                    SendExcavatedToGraveyardStep => SendExcavatedToGraveyard(state, controller, definition.EffectId, events),
                    OpponentDiscardsMatchingStep discard => OpponentDiscardsMatching(state, controller, discard, definition.EffectId, events),
                    ApplyGraveyardRedirectStep redirect => AddTimedRedirect(state, controller, redirect.Rule),
                    RollDieStep => RollDie(state, controller, definition.EffectId, events),
                    TossCoinsStep coins => TossCoins(state, controller, coins.Count, definition.EffectId, events),
                    ModifyAllUntilEndStep all => ModifyAllUntilEnd(state, controller, all, definition.EffectId, events),
                    LookAtOpponentHandStep look => LookAtOpponentHand(state, controller, look.IncludeSetCards, definition.EffectId, events),
                    ApplyDamageRuleStep damageRule => AddTimedDamageRule(state, controller, damageRule),
                    ReturnSourceToDeckTopStep => ReturnSourceToDeckTop(state, link, definition.EffectId, events),
                    DestroyTargetStep => DestroyTarget(state, link, events),
                    DestroyOpponentFaceUpMonstersStep => DestroyOpponentFaceUpMonsters(state, Other(controller), events),
                    NegateActivationStep negate => NegateLink(state, links, link, ChainNegation.Activation, negate.DestroySource, events),
                    NegateEffectStep negate => NegateLink(state, links, link,
                        links[link.TargetLinkNumber!.Value - 1].ActivationNegated ? ChainNegation.Activation : ChainNegation.Effect, negate.DestroySource, events),
                    NegateSummonStep => link.TargetLinkNumber is not null
                        ? NegateLink(state, links, link, ChainNegation.Activation, true, events) : ResolveSummonNegation(state, link, events),
                    DrawCardsStep draw => DrawingPlayers(controller, draw.Players)
                        .Aggregate(state, (s, p) => s.Outcome is null ? DrawCards(s, p, draw.Count, events) : s),
                    MillTopOfDeckStep mill => MillAndRecord(state, controller, mill.Count, definition.EffectId, events),
                    DestroyAllStep all => FieldMatches(state, controller, all.Kind, all.Side, all.Filter, all.IncludeSelf ? null : link.Activation.Spell.InstanceId)
                        .Where(t => (all.Cards is null || MatchesFilter(t.Card, all.Cards)) && Affects(state, link, t)).ToArray().Aggregate(state, (s, t) => DestroyFieldCard(s, t, events)),
                    ReturnAllToHandStep all => FieldMatches(state, controller, all.Kind, all.Side, FieldCardFilter.Any, link.Activation.Spell.InstanceId)
                        .Where(t => Affects(state, link, t)).ToArray().Aggregate(state, (s, t) => ReturnFieldCardToHand(s, t, definition.EffectId, events)),
                    BanishAllStep all => FieldMatches(state, controller, FieldCardKind.Monster, all.Side, all.Filter, null)
                        .Where(t => Affects(state, link, t)).ToArray().Aggregate(state, (s, t) => BanishFieldCard(s, t, definition.EffectId, events)),
                    ChangeAllToDefenseStep all => FieldMatches(state, controller, FieldCardKind.Monster, all.Side, FieldCardFilter.AttackPosition, null)
                        .Where(t => Affects(state, link, t)).ToArray().Aggregate(state, (s, t) => ChangeToDefense(s, t, events)),
                    _ => throw new InvalidOperationException("Unsupported effect step.")
                };
            if (state.Outcome is not null) break;
        }
        return (state, null);
    }

    /// <summary>Matching field cards, the activating player's side first: monsters, then Spell/Trap and Pendulum zones.</summary>
    private IEnumerable<FieldCardTarget> FieldMatches(DuelState state, PlayerId controller, FieldCardKind kind, TargetSide side,
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
            if (kind is not (FieldCardKind.SpellTrap or FieldCardKind.PendulumScale or FieldCardKind.Spell))
                for (int zone = 0; zone < 5; zone++)
                    if (player.MonsterZones[zone] is { } monster && monster.Card.InstanceId != excludeInstanceId && filter switch
                        {
                            FieldCardFilter.Any => true,
                            FieldCardFilter.FaceUp => monster.Position != BattlePosition.FaceDownDefense,
                            FieldCardFilter.AttackPosition or FieldCardFilter.FaceUpAttack => monster.Position == BattlePosition.FaceUpAttack,
                            FieldCardFilter.DefensePosition => monster.Position != BattlePosition.FaceUpAttack,
                            FieldCardFilter.FaceDown => monster.Position == BattlePosition.FaceDownDefense,
                            _ => false
                        })
                        yield return new(id, FieldLocation.MonsterZone, zone, monster.Card, monster.PlacementId);
            if (kind == FieldCardKind.Monster || filter is not (FieldCardFilter.Any or FieldCardFilter.FaceUp or FieldCardFilter.FaceDown)) continue;
            // Set Spells and Traps are face-down; Pendulum Scales are always face-up.
            bool Shown(bool faceUp) => filter == FieldCardFilter.Any || faceUp == (filter == FieldCardFilter.FaceUp);
            // A Pendulum Scale target is only a card in a Pendulum Zone.
            if (kind != FieldCardKind.PendulumScale)
                for (int zone = 0; zone < 5; zone++)
                if (player.SpellTrapFields[zone] is { } field && field.Card.InstanceId != excludeInstanceId && Shown(field.IsFaceUp) &&
                    (kind != FieldCardKind.Spell || field.EquippedTo is not null ||
                     rules.TryGetValue(field.Card.GameCardId, out var fieldRules) && fieldRules.Category is CardCategory.Spell or CardCategory.Monster))
                    yield return new(id, FieldLocation.SpellTrapZone, zone, field.Card, field.PlacementId);
            for (int zone = 0; zone < 2; zone++)
                if (player.PendulumZones[zone] is { } scale && scale.Card.InstanceId != excludeInstanceId && filter != FieldCardFilter.FaceDown)
                    yield return new(id, FieldLocation.PendulumZone, zone, scale.Card, scale.PlacementId);
            if (kind != FieldCardKind.PendulumScale && player.FieldSpell is { } fieldSpell && fieldSpell.Card.InstanceId != excludeInstanceId && Shown(fieldSpell.IsFaceUp))
                yield return new(id, FieldLocation.SpellTrapZone, DuelPlayerState.FieldZoneIndex, fieldSpell.Card, fieldSpell.PlacementId);
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
                FieldTarget f => FieldMatches(state, link.Activation.Player, f.Kind, f.Side, f.Filter, null).Where(t => TargetPasses(state, f, t)),
                ZoneTarget z => ZoneMatches(state, link.Activation.Player, z),
                AttackingMonsterTarget or DefendingMonsterTarget or SummonedMonsterTarget => FieldMatches(state, link.Activation.Player, FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.Any, null),
                TriggerSourceMonster => FieldMatches(state, link.Activation.Player, FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.Any, null),
                TriggerTargetedMonster => FieldMatches(state, link.Activation.Player, FieldCardKind.Monster, TargetSide.Either,
                    link.Trigger!.Profile.Target!.RequireFaceUpAtResolution ? FieldCardFilter.FaceUp : FieldCardFilter.Any, null),
                _ => []
            };
            if (target.IsStillPresent(state) && matches.Any(m => m.Card == target.Card) && Affects(state, link, target)) yield return target;
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
                if (MatchesFilter(card, spec.Filter, spec.Zone == CardZone.Graveyard) && (!spec.MustBeSummonable || Enumerable.Range(0, 5).Any(z =>
                        CheckSpecialSummon(state, controller, card, z, BattlePosition.FaceUpAttack, DecisionCardSource.Graveyard,
                            sourcePlayer: id) == DuelError.None)))
                    yield return new(id, spec.Zone == CardZone.Graveyard ? FieldLocation.Graveyard : FieldLocation.Banished, -1, card, 0);
        }
    }

    private bool MatchesFilter(CardInstance card, CardFilter filter, bool inGraveyard = false) => rules.TryGetValue(card.GameCardId, out var rule) && filter switch
    {
        CardIdFilter ids => ids.CardIds.Contains(card.GameCardId),
        ArchetypeFilter archetype => MatchesArchetype(card, archetype),
        AnyMonsterFilter => rule.Category == CardCategory.Monster,
        AnyCardFilter => true,
        DeclaredFilter => resolvingDeclaration is not { } declared || DeclaredMatches(card, declared.Kind, declared.Value),
        FaceUpAttackFilter => FieldMonsters(IdentityState).Any(e => e.Monster.Card == card && e.Monster.Position == BattlePosition.FaceUpAttack),
        // Whether the targets share the trait is checked as they are picked.
        SharedTraitFilter shared => rule.Category == CardCategory.Monster && (shared.Trait == IdentityTrait.Attribute ? AttributeCode(card) : TypeCode(card)) is not null,
        CardCategoryFilter category => rule.Category == category.Category,
        CardIconFilter icon => rule.Category == icon.Category && catalog[card.GameCardId].Codes?.Icon == icon.Icon,
        MonsterKindFilter kind => rule.Category == CardCategory.Monster && IsMonsterKind(card, rule, kind.Kind),
        MonsterTypeFilter types => rule.Category == CardCategory.Monster && TypeCode(card) is { } type && types.TypeCodes.Contains(type),
        NormalMonsterFilter normal => normal.CardIds.Contains(card.GameCardId) || inGraveyard && rule.IsGemini,
        MonsterAttributeFilter attribute => rule.Category == CardCategory.Monster && AttributeCode(card) == attribute.AttributeCode,
        MonsterLevelFilter limit => rule.Category == CardCategory.Monster && !IsXyzCard(card) && CurrentLevel(card) is > 0 and var level &&
            level <= limit.MaxLevel && level >= limit.MinLevel && (limit.TypeCode is not { } typeCode || TypeCode(card) == typeCode),
        AnyOfFilter any => any.Filters.Any(f => MatchesFilter(card, f, inGraveyard)),
        NotFilter not => !MatchesFilter(card, not.Filter, inGraveyard),
        XyzMonsterFilter xyz => rule.Category == CardCategory.Monster && IsXyzCard(card) &&
            catalog[card.GameCardId].Statistics.Level is var rank && rank >= xyz.MinRank && rank <= xyz.MaxRank,
        AllOfFilter all => all.Filters.All(f => MatchesFilter(card, f, inGraveyard)),
        MonsterTraitFilter trait => rule.Category == CardCategory.Monster && !trait.ExcludedCardIds.Contains(card.GameCardId) &&
            (trait.AttributeCode is not { } code || AttributeCode(card) == code) &&
            (trait.Stat == MonsterStat.Attack ? catalog[card.GameCardId].Statistics.Attack : catalog[card.GameCardId].Statistics.Defense) is int value &&
            value >= trait.Min && value <= trait.Max,
        _ => false
    };

    /// <summary>Whether the face-up monsters matching the condition's filter on its side number within its range.</summary>
    private bool ControlsMonsters(DuelState state, PlayerId player, ControlsMonstersCondition condition)
    {
        IEnumerable<DuelPlayerState> sides = condition.Side switch
        {
            TargetSide.Own => [state.GetPlayer(player)],
            TargetSide.Opponent => [state.GetPlayer(Other(player))],
            _ => state.Players
        };
        int count = sides.Sum(p => p.MonsterZones.Count(m => m is not null && (!condition.FaceUpOnly || m.Position != BattlePosition.FaceDownDefense) &&
            MatchesFilter(m.Card, condition.Filter)));
        return count >= condition.Min && count <= condition.Max;
    }

    /// <summary>Decoded getter kinds per monster kind; a card registered as a Tuner is one whatever its code.</summary>
    private bool IsMonsterKind(CardInstance card, DuelCardRules rule, MonsterKind kind) => catalog[card.GameCardId].Codes?.GetterKind is var code && kind switch
    {
        MonsterKind.Fusion => code is 2 or 3,
        MonsterKind.Ritual => code is 4 or 5,
        MonsterKind.Synchro => code is 17 or 18 or 19,
        _ => rule.IsTuner || code is 15 or 16 or 19 or 30 or 32
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
    private DuelState MoveZoneCard(DuelState state, FieldCardTarget target, bool toBanished, string effectId, List<DuelEvent> events)
    {
        if (toBanished && MoveLocked(state, LockedAction.BeBanished, target)) return state;
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
            if (MoveLocked(state, LockedAction.BeReturnedToDeck, target)) continue;
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
                return state.With(player: player.WithSpellTrapAt(target.Zone, null));
            default:
                var scales = player.PendulumZones.ToArray(); scales[target.Zone] = null;
                return state.With(player: player.With(pendulumZones: scales));
        }
    }

    /// <summary>Destroys a field card by a card effect, unless a replacement applies; rule-driven destruction cannot be replaced.</summary>
    private DuelState DestroyFieldCard(DuelState state, FieldCardTarget target, List<DuelEvent> events, bool replaceable = true) =>
        replaceable && ReplacedDestruction(state, target, events) is { } kept ? kept
        : target.Location == FieldLocation.MonsterZone
            ? SendFieldCardToGraveyard(RemovePlacement(state, target), target.Card, new MonsterDestroyed(target.Player, target.Zone, target.Card), events)
            : RemoveFieldSpell(state, new SpellTrapTarget(target.Player, target.Zone, target.Card, target.PlacementId,
                target.Location == FieldLocation.PendulumZone),
                new SpellTrapDestroyed(target.Player, target.Zone, target.Card, target.Location == FieldLocation.PendulumZone), events);

    /// <summary>Returns a field card to its owner's hand; Extra Deck monsters go to the Extra Deck and Tokens cease to exist.</summary>
    private DuelState ReturnFieldCardToHand(DuelState state, FieldCardTarget target, string effectId, List<DuelEvent> events)
    {
        var card = target.Card;
        // An Extra Deck monster returned to the hand goes to the Extra Deck, so a Deck lock stops it; a stopped return has no effect.
        if (!IsTokenId(card.GameCardId) && MoveLocked(state, rules.TryGetValue(card.GameCardId, out var moved) && moved.AllowsExtraDeck
            ? LockedAction.BeReturnedToDeck : LockedAction.BeReturnedToHand, target)) return state;
        state = RemovePlacement(state, target);
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

    /// <summary>The live combatant a battle target binds: the attacker, or the defender, still present and not destroyed by battle.</summary>
    private static MonsterBinding? LiveCombatant(DuelState state, EffectDefinition definition)
    {
        if (state.Battle?.Attack is not { Cancellation: null } attack) return null;
        var binding = definition.FieldTargets is [DefendingMonsterTarget] ? attack.Defender : attack.Attacker;
        return binding is not null && binding.IsStillPresent(state) && !state.GetPlayer(binding.Player).MonsterZones[binding.Zone]!.BattleDestroyed
            ? binding : null;
    }

    /// <summary>Adds a stat change to a combatant that expires when the current attack ends.</summary>
    private DuelState ModifyCombatant(DuelState state, ChainLink link, FieldCardTarget target, ModifyCombatantStatsStep stats,
        string effectId, List<DuelEvent> events)
    {
        if (state.Battle?.Attack is not { } attack) return state;
        var effects = state.EventEffects;
        var modifier = new StatModifier(effectId, null, new(target.Player, target.Zone, target.Card, target.PlacementId), stats.Attack, stats.Defense,
            checked(State.Revision + 1), State.TurnNumber, ExpiresAfterAttackId: attack.Id,
            SpellTrapSource: new(link.Activation.Player, link.Activation.SpellTrapZone, link.Activation.Spell, link.SourcePlacementId));
        events.Add(new MonsterModifierCreated(modifier));
        return state.WithEventEffects(effects.WithModifiers(effects.TimedModifiers.Append(modifier), effects.AppliedModifiers, effects.Statistics));
    }

    /// <summary>Destroys a field monster and banishes it instead of sending it to the Graveyard; a Token ceases to exist.</summary>
    private DuelState DestroyAndBanishFieldCard(DuelState state, FieldCardTarget target, string effectId, List<DuelEvent> events)
    {
        if (ReplacedDestruction(state, target, events) is { } kept) return kept;
        // A card that cannot be banished is still destroyed, and goes to the Graveyard.
        if (MoveLocked(state, LockedAction.BeBanished, target)) return DestroyFieldCard(state, target, events, replaceable: false);
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
    private IEnumerable<FieldCardTarget> SummonedMatches(DuelState state, PlayerId playerId, MonsterSummonedCondition condition, IEnumerable<EffectCondition> how)
    {
        if (state.Bookkeeping.RecentSummon is not { } summoned || condition.Single && summoned.Count != 1) yield break;
        foreach (var entry in summoned)
        {
            var (monster, kind) = entry;
            if ((condition.Kinds & kind) == 0 || condition.ByOpponent && monster.Player == playerId || !monster.IsStillPresent(state) || !SummonedHow(entry, how)) continue;
            var stats = GetMonsterStatistics(state, monster.Player, monster.Zone);
            if ((condition.Stat == MonsterStat.Attack ? stats?.Attack : stats?.Defense) is int value && value >= condition.Min && value <= condition.Max)
                yield return new(monster.Player, FieldLocation.MonsterZone, monster.Zone, monster.Card, monster.PlacementId);
        }
    }

    private bool? summonResponders;
    private bool SummonResponders => summonResponders ??= allEffects.Any(d => d.Conditions.Any(c => c is MonsterSummonedCondition));

    /// <summary>
    /// Records the latest Summon for summon conditions. In an open state it offers a summon-response window to the
    /// turn player, else the other player, when they have a set card answering it; otherwise the record lapses.
    /// </summary>
    private void TrackSummons(List<DuelEvent> emitted)
    {
        if (!SummonResponders) return;
        SummonedMonster[] summoned = [.. emitted.Select(e => e switch
        {
            MonsterNormalSummoned n => new SummonedMonster(new(n.Player, n.Zone, n.Monster.Card, n.Monster.PlacementId), SummonKinds.Normal) { Tributes = n.Tributes.Count },
            MonsterFlipSummoned f => new SummonedMonster(new(f.Player, f.Zone, f.Monster.Card, f.Monster.PlacementId), SummonKinds.Flip),
            // An Xyz Summon brings an Extra Deck Xyz Monster with materials already attached.
            MonsterSpecialSummoned s => new SummonedMonster(new(s.Player, s.Zone, s.Monster.Card, s.Monster.PlacementId), SummonKinds.Special)
                { Xyz = s.Source == DecisionCardSource.ExtraDeck && IsXyzCard(s.Monster.Card) && s.Monster.XyzMaterials.Count > 0 },
            _ => null
        }).OfType<SummonedMonster>()];
        bool open = State.Outcome is null && State.PendingChain is null && State.OpenWindow is null && State.PendingDecision is null &&
            State.PendingSummon is null && State.EventEffects.PendingTriggers is null && State.EventEffects.PendingCommitment is null;
        if (summoned.Length != 0)
        {
            var summoner = summoned[^1].Monster.Player;
            State = State.WithBookkeeping(State.Bookkeeping with { RecentSummon = [.. summoned.Where(s => s.Monster.Player == summoner)] });
        }
        // A Normal Summon nothing can respond to opens no response window.
        if (summoned.Any(s => s.Kind == SummonKinds.Normal && unanswerableSummons.Contains(s.Monster.Card.GameCardId)))
        {
            State = State.WithBookkeeping(State.Bookkeeping with { RecentSummon = null });
            return;
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
        effectsByCard.TryGetValue(source.Card.GameCardId, out var effects) && effects.Any(definition =>
            definition.Conditions.Any(c => c is MonsterSummonedCondition) &&
            DefinitionActivationError(definition, ActivationContext.OpenWindow, playerId, source.Card, source) == DuelError.None));

    /// <summary>Banishes a field monster to its owner's banished zone; a Token ceases to exist.</summary>
    private DuelState BanishFieldCard(DuelState state, FieldCardTarget target, string effectId, List<DuelEvent> events)
    {
        if (MoveLocked(state, LockedAction.BeBanished, target)) return state;
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
    private DuelState DrawCards(DuelState state, PlayerId playerId, int count, List<DuelEvent> events)
    {
        for (int i = 0; i < count; i++)
        {
            if (RuleLocked(state, LockedAction.Draw, playerId, null)) break;
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

    private DuelActionResult FinishDefinitionCards(PendingChain chain, FinishDefinitionCardChoice command)
    {
        var pending = chain.DefinitionCardChoice!;
        if (command.DecisionId != pending.DecisionId) return Reject(DuelError.StaleDecision);
        if (command.Player != pending.Player) return Reject(DuelError.NotActivePlayer);
        if (!pending.CanFinish) return Reject(DuelError.InvalidDecisionChoice);
        return ResolveChainLink(chain, definitionChoice: command);
    }

    private DuelActionResult AnswerDefinitionCardChoice(PendingChain chain, ChooseDefinitionCard command)
    {
        var pending = chain.DefinitionCardChoice!;
        if (command.DecisionId != pending.DecisionId) return Reject(DuelError.StaleDecision);
        if (command.Player != pending.Player) return Reject(DuelError.NotActivePlayer);
        if (!pending.Options.Any(c => c.InstanceId == command.CardInstanceId)) return Reject(DuelError.InvalidDecisionChoice);
        return ResolveChainLink(chain, definitionChoice: command);
    }

    /// <summary>Eligible hand, Deck, then Graveyard cards, each with at least one legal zone.</summary>
    private IEnumerable<DefinitionSummonOption> SpecialSummonOptions(DuelState state, PlayerId playerId, SpecialSummonStep step,
        CardInstance source)
    {
        var player = state.GetPlayer(playerId);
        foreach (var (flag, from) in new[] { (SpecialSummonSources.Hand, DecisionCardSource.Hand), (SpecialSummonSources.Deck, DecisionCardSource.Deck),
                     (SpecialSummonSources.Graveyard, DecisionCardSource.Graveyard) })
        {
            if (!step.Sources.HasFlag(flag)) continue;
            foreach (var card in SpecialSourceCards(player, from).Where(c => step.Filter is { } filter ? MatchesFilter(c, filter) : step.EligibleCardIds.Contains(c.GameCardId)))
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
        var definition = LinkDefinition(link);
        // A Rank-Up goes into the zone its material frees; its options were already checked against that zone.
        if (definition.Steps[pending.StepIndex] is not RankUpStep &&
            CheckSpecialSummon(State, pending.Player, option.Card, command.MonsterZone, command.Position, option.Source,
                lockExemptSource: link.Activation.Spell, sourcePlayer: option.From, allowFaceDown: true) is not DuelError.None and var error)
            return Reject(error);
        var remaining = pending.Options.Where(o => o != option).ToArray();
        var zones = pending.MonsterZoneOptions.Where(z => z != command.MonsterZone).ToArray();
        if (definition.Steps[pending.StepIndex] is not SpecialSummonStep { AnyNumber: true } || remaining.Length == 0 || zones.Length == 0)
            return ResolveChainLink(chain, definitionChoice: command);
        var next = new PendingDefinitionSummon(checked(State.Revision + 1), pending.Player, pending.EffectId, pending.LinkNumber, pending.StepIndex,
            remaining, zones, pending.PositionOptions.Count == 1 ? pending.PositionOptions[0] : null,
            pending.Picks.Append(new(option.Card, option.Source, command.MonsterZone, command.Position)), canFinish: true);
        return Accept(State.WithChain(new(next.DecisionId, chain.Links, chain.Responder, chain.ConsecutivePasses, chain.NextLink, definitionChoice: next)),
            new DefinitionSummonRequested(next));
    }

    private DuelActionResult FinishDefinitionSummons(PendingChain chain, FinishDefinitionSummon command)
    {
        var pending = chain.DefinitionSummon!;
        if (command.DecisionId != pending.DecisionId) return Reject(DuelError.StaleDecision);
        if (command.Player != pending.Player) return Reject(DuelError.NotActivePlayer);
        return pending.CanFinish ? ResolveChainLink(chain, definitionChoice: command) : Reject(DuelError.InvalidDecisionChoice);
    }

    /// <summary>The monsters chosen across a pending Summon, including the answer that ended it.</summary>
    private static ChosenDefinitionSummon[] FinalSummonPicks(DuelState state, DuelCommand? answer)
    {
        var pending = state.PendingChain!.DefinitionSummon!;
        if (answer is not ChooseDefinitionSummon last) return [.. pending.Picks];
        var option = pending.Options.Single(o => o.Card.InstanceId == last.CardInstanceId);
        return [.. pending.Picks, new(option.Card, option.Source, last.MonsterZone, last.Position)];
    }

    /// <summary>Places the chosen monsters together, schedules any End Phase banishment, then shuffles once after a Deck summon.</summary>
    private DuelState ApplySpecialSummons(DuelState state, ChainLink link, SpecialSummonStep step, ChosenDefinitionSummon[] picks,
        string effectId, List<DuelEvent> events)
    {
        var controller = link.Activation.Player;
        foreach (var pick in picks)
        {
            state = PlaceSpecialSummon(state, controller, pick.Card, pick.MonsterZone, pick.Position, pick.Source, events);
            if (!step.BanishAtEndPhase) continue;
            var placed = state.GetPlayer(controller).MonsterZones[pick.MonsterZone]!;
            var delayed = new PendingEndPhaseBanishment(controller, effectId, link.Activation.Spell, pick.Card, pick.MonsterZone,
                state.TurnNumber, placed.PlacementId);
            state = state.WithEndPhaseBanishments(state.PendingEndPhaseBanishments.Append(delayed));
            events.Add(new EndPhaseBanishmentScheduled(delayed));
        }
        return picks.Any(p => p.Source == DecisionCardSource.Deck) ? ShuffleDeck(state, controller, effectId, events) : state;
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
        var bound = new FieldCardTarget(target.Player, target.IsPendulumZone ? FieldLocation.PendulumZone : FieldLocation.SpellTrapZone, target.Zone, target.Card, target.PlacementId);
        if (ReplacedDestruction(state, bound, events) is { } kept) return kept;
        return RemoveFieldSpell(state, target, new SpellTrapDestroyed(target.Player, target.Zone, target.Card, target.IsPendulumZone), events);
    }

    private DuelState DestroyOpponentFaceUpMonsters(DuelState state, PlayerId opponent, List<DuelEvent> events)
    {
        for (int zone = 0; zone < 5; zone++)
            if (state.GetPlayer(opponent).MonsterZones[zone] is { Position: not BattlePosition.FaceDownDefense } monster)
                state = DestroyFieldCard(state, new(opponent, FieldLocation.MonsterZone, zone, monster.Card, monster.PlacementId), events);
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
    private int CountFor(DuelState state, PlayerId controller, EffectCount count, CardFilter? filter = null)
    {
        var own = state.GetPlayer(controller);
        var opponent = state.GetPlayer(Other(controller));
        int Counted(IEnumerable<CardInstance> cards, bool inGraveyard = false) => filter is null ? cards.Count() : cards.Count(c => MatchesFilter(c, filter, inGraveyard));
        IEnumerable<CardInstance> Monsters(DuelPlayerState p, bool faceUpOnly = false) =>
            p.MonsterZones.Where(m => m is not null && (!faceUpOnly || m.Position != BattlePosition.FaceDownDefense)).Select(m => m!.Card);
        return count switch
        {
            EffectCount.None => 1,
            EffectCount.OpponentMonsters => Counted(Monsters(opponent)),
            EffectCount.AllMonsters => Counted(Monsters(own).Concat(Monsters(opponent))),
            EffectCount.OwnMonsters => Counted(Monsters(own)),
            EffectCount.OwnFaceUpMonsters => Counted(Monsters(own, faceUpOnly: true)),
            EffectCount.OpponentHand => Counted(opponent.Hand),
            EffectCount.OpponentHandAndField => opponent.Hand.Count + Monsters(opponent).Count() +
                opponent.SpellTrapFields.Count(f => f is not null) + opponent.PendulumZones.Count(f => f is not null) + (opponent.FieldSpell is null ? 0 : 1),
            EffectCount.OpponentGraveyard => Counted(opponent.Graveyard, inGraveyard: true),
            EffectCount.OpponentBanished => Counted(opponent.Banished),
            EffectCount.DieRoll => state.Bookkeeping.LastRandom?.Die ?? 0,
            EffectCount.Heads => state.Bookkeeping.LastRandom?.Heads ?? 0,
            EffectCount.MilledLevel => state.Bookkeeping.LastRandom?.MilledLevel ?? 0,
            _ => throw new ArgumentOutOfRangeException(nameof(count))
        };
    }

    /// <summary>Amount times the count, saturated to the integer range; zero when nothing is counted.</summary>
    private int Scaled(DuelState state, PlayerId controller, int amount, EffectCount count, CardFilter? filter = null) =>
        (int)Math.Min(int.MaxValue, (long)amount * CountFor(state, controller, count, filter));

    private DuelState GainForPlayers(DuelState state, PlayerId controller, GainLifePointsStep step, string effectId, List<DuelEvent> events)
    {
        int amount = Scaled(state, controller, step.Amount, step.PerCount, step.CountFilter);
        if (amount == 0) return state;
        if (step.Players is EffectPlayers.Self or EffectPlayers.Both) state = GainLifePoints(state, controller, amount, effectId, events);
        if (step.Players is EffectPlayers.Opponent or EffectPlayers.Both) state = GainLifePoints(state, Other(controller), amount, effectId, events);
        return state;
    }

    /// <summary>Both players take damage at once (opponent's first), after damage rules; both at 0 LP is a draw.</summary>
    private DuelState DamageBothPlayers(DuelState state, PlayerId controller, DamageBothPlayersStep step, string effectId,
        List<DuelEvent> events)
    {
        var life = new Dictionary<PlayerId, int> { [PlayerId.One] = state.GetPlayer(PlayerId.One).LifePoints, [PlayerId.Two] = state.GetPlayer(PlayerId.Two).LifePoints };
        foreach (var (target, amount) in new[] { (Other(controller), step.ToOpponent), (controller, step.ToSelf) })
        {
            var recipient = DamageRecipient(state, target, DamageKinds.Effect);
            int before = life[recipient];
            if (HasDamageRule(state, recipient, DamageRuleKind.BecomesGain, DamageKinds.Effect))
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

    private DuelState GainLifePoints(DuelState state, PlayerId recipient, int amount, string effectId, List<DuelEvent> events, bool converted = false)
    {
        // An effect's LP gain can become damage; a gain that replaced damage does not turn back.
        if (!converted && HasDamageRule(state, recipient, DamageRuleKind.GainBecomesDamage, DamageKinds.Effect))
            return DealEffectDamage(state, recipient, amount, effectId, events, converted: true);
        var player = state.GetPlayer(recipient);
        // Same bounded integer policy as existing trigger LP recovery.
        int after = (int)Math.Min(int.MaxValue, (long)player.LifePoints + amount);
        events.Add(new LifePointsGained(recipient, after - player.LifePoints, player.LifePoints, after, effectId));
        return state.With(player: player.With(lifePoints: after));
    }

    private DuelState DealEffectDamage(DuelState state, PlayerId recipient, int amount, string effectId, List<DuelEvent> events, bool converted = false)
    {
        recipient = DamageRecipient(state, recipient, DamageKinds.Effect);
        if (!converted && HasDamageRule(state, recipient, DamageRuleKind.BecomesGain, DamageKinds.Effect))
            return GainLifePoints(state, recipient, amount, effectId, events, converted: true);
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
