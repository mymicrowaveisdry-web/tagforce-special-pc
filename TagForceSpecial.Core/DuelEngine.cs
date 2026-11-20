using System.Collections.Frozen;
using TagForceSpecial.Cards;

namespace TagForceSpecial.Core;

/// <summary>
/// Deterministic two-player duel rules. Card effects, chains, event effects and staged battle are explicit opt-ins.
/// </summary>
public sealed partial class DuelEngine
{
    private readonly CardCatalog catalog;
    private readonly FrozenDictionary<uint, DuelCardRules> rules;
    private readonly FrozenDictionary<string, DecisionSequence> decisionSequences;
    private readonly TriggerEffectProfile[] triggerEffects;
    private readonly ContinuousEffectProfile[] continuousEffects;
    private readonly ContinuousNegationProfile[] continuousNegations;
    private readonly uint[] negationOrder;
    private readonly bool experimentalBattleFlow;
    private readonly SummonLevelModifierProfile[] summonLevelModifiers;
    private readonly FrozenDictionary<string, TributeProcedureProfile> tributeProcedures;
    private readonly FrozenSet<uint> summonWindows;
    private readonly FrozenDictionary<uint, SpecialSummonCardProfile> specialSummonCards;
    private readonly FrozenDictionary<string, SpecialSummonProcedureProfile> specialSummonProcedures;
    private readonly FrozenDictionary<string, ExtraDeckMaterialProcedureProfile> materialProcedures;
    private readonly FrozenDictionary<uint, SummonEffectProfile> summonEffects;
    private readonly FrozenDictionary<uint, PendulumCardProfile> pendulumCards;
    private readonly FrozenSet<uint> unnegatableSummons;
    /// <summary>Monsters whose Normal Summon nothing can respond to.</summary>
    private readonly FrozenSet<uint> unanswerableSummons;
    /// <summary>Definition-driven card effects, keyed by source card ID.</summary>
    private readonly FrozenDictionary<uint, EffectDefinition> definitions;
    /// <summary>Every effect of each card in registration order; <see cref="definitions"/> holds the first.</summary>
    private readonly FrozenDictionary<uint, EffectDefinition[]> effectsByCard;
    private readonly FrozenDictionary<string, EffectDefinition> effectsById;
    private readonly EffectDefinition[] allEffects;
    private readonly ActionEffectCatalog actionEffects;
    internal ActionEffectCatalog ActionEffects => actionEffects;
    private readonly FrozenDictionary<uint, RuleLockProfile[]> ruleLockProfiles;
    private readonly FrozenDictionary<uint, BattleAbilityProfile[]> battleAbilityProfiles;
    private readonly FrozenDictionary<uint, DestructionReplacementProfile[]> replacementProfiles;
    private readonly FrozenDictionary<uint, GraveyardRedirectProfile[]> redirectProfiles;
    private readonly FrozenDictionary<uint, DamageRuleProfile[]> damageRuleProfiles;
    /// <summary>Activation-time cost/target decisions of definitions that have any, keyed by source card ID.</summary>
    private readonly FrozenDictionary<string, DecisionSequence> definitionDecisions;
    public DuelState State { get; private set; }

    /// <summary>
    /// Decks are supplied top-first and checked only for size and catalog membership. Missing rule classifications fail
    /// closed when summoning.
    /// </summary>
    public DuelEngine(CardCatalog catalog, IReadOnlyDictionary<uint, DuelCardRules> rules,
        IEnumerable<uint> playerOneDeck, IEnumerable<uint> playerTwoDeck, PlayerId startingPlayer = PlayerId.One,
        IEnumerable<DecisionSequence>? decisionSequences = null,
        IEnumerable<TriggerEffectProfile>? experimentalTriggerEffects = null,
        IEnumerable<ContinuousEffectProfile>? experimentalContinuousEffects = null,
        IEnumerable<ContinuousNegationProfile>? experimentalContinuousNegations = null,
        bool experimentalBattleFlow = false,
        IEnumerable<SummonLevelModifierProfile>? experimentalSummonLevelModifiers = null,
        IEnumerable<TributeProcedureProfile>? experimentalTributeProcedures = null,
        IEnumerable<SummonWindowProfile>? experimentalSummonWindows = null,
        IEnumerable<SpecialSummonCardProfile>? experimentalSpecialSummonCards = null,
        IEnumerable<SpecialSummonProcedureProfile>? experimentalSpecialSummonProcedures = null,
        IEnumerable<uint>? playerOneExtraDeck = null, IEnumerable<uint>? playerTwoExtraDeck = null,
        IEnumerable<ExtraDeckMaterialProcedureProfile>? experimentalMaterialProcedures = null,
        IEnumerable<SummonEffectProfile>? experimentalSummonEffects = null,
        IEnumerable<PendulumCardProfile>? experimentalPendulumCards = null,
        IEnumerable<EffectDefinition>? effectDefinitions = null, ulong? shuffleSeed = null, IEnumerable<RuleLockProfile>? ruleLocks = null, IEnumerable<BattleAbilityProfile>? battleAbilities = null,
        IEnumerable<DestructionReplacementProfile>? destructionReplacements = null, IEnumerable<GraveyardRedirectProfile>? graveyardRedirects = null,
        IEnumerable<DamageRuleProfile>? damageRules = null, IEnumerable<MonsterStatProfile>? monsterStats = null,
        ICardArchetypeResolver? archetypeResolver = null, int startingLifePoints = DuelSetup.DefaultLifePoints)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(playerOneDeck);
        ArgumentNullException.ThrowIfNull(playerTwoDeck);
        if (startingPlayer is not (PlayerId.One or PlayerId.Two))
            throw new ArgumentOutOfRangeException(nameof(startingPlayer));
        this.catalog = catalog;
        this.archetypeResolver = archetypeResolver;
        var knownIds = catalog.Cards.Select(c => c.GameCardId).ToHashSet();
        this.rules = DuelSetup.Rules(rules, knownIds);
        this.decisionSequences = DuelSetup.Decisions(decisionSequences, knownIds);
        triggerEffects = (experimentalTriggerEffects ?? []).ToArray();
        continuousEffects = (experimentalContinuousEffects ?? []).ToArray();
        continuousNegations = (experimentalContinuousNegations ?? []).ToArray();
        // Spells and Traps may only have effects that activate once they are in the Graveyard.
        if (triggerEffects.Any(p => p is null || !this.rules.TryGetValue(p.CardId, out var rule) ||
                rule.Category != CardCategory.Monster && !(p.SourceLocation is TriggerSourceLocation.GraveyardSelf or TriggerSourceLocation.FaceUpSpellTrap && rule.Category is CardCategory.Spell or CardCategory.Trap) ||
                p.DiscardCardIds.Any(id => !knownIds.Contains(id)) ||
                p.Target is { } target && target.AllowedCardIds.Any(id => !knownIds.Contains(id) ||
                    !this.rules.TryGetValue(id, out var targetRule) || targetRule.Category != CardCategory.Monster)))
            throw new ArgumentException("Triggers require explicit catalog Monster rules.", nameof(experimentalTriggerEffects));
        if (continuousEffects.Any(p => p is null || !this.rules.TryGetValue(p.CardId, out var rule) || rule.Category != CardCategory.Monster))
            throw new ArgumentException("Continuous sources require explicit catalog Monster rules.", nameof(experimentalContinuousEffects));
        if (continuousNegations.Any(p => p is null || !this.rules.TryGetValue(p.CardId, out var rule) || rule.Category != CardCategory.Monster ||
            p.TargetCardIds.Any(id => !this.rules.TryGetValue(id, out var targetRule) || targetRule.Category != CardCategory.Monster)))
            throw new ArgumentException("Negation profiles need explicit Monster sources and targets.", nameof(experimentalContinuousNegations));
        negationOrder = ValidateNegationDependencies(continuousNegations);
        var eventIds = DuelProfileRegistration.EventIds(triggerEffects, continuousEffects, continuousNegations);
        // Battle flips activate after damage calculation, which only the staged flow has.
        this.experimentalBattleFlow = experimentalBattleFlow || triggerEffects.Any(p => p is { EventKind: MonsterEventKind.FlippedFaceUp or MonsterEventKind.AttackDeclared, Definition: not null });
        summonLevelModifiers = (experimentalSummonLevelModifiers ?? []).ToArray();
        var procedures = (experimentalTributeProcedures ?? []).ToArray();
        var windows = (experimentalSummonWindows ?? []).ToArray();
        (specialSummonCards, specialSummonProcedures) = DuelProfileRegistration.SpecialSummons(
            experimentalSpecialSummonCards, experimentalSpecialSummonProcedures, this.rules);
        materialProcedures = DuelProfileRegistration.Materials(experimentalMaterialProcedures, specialSummonCards, specialSummonProcedures, this.rules);
        string[] summonIds;
        (tributeProcedures, unnegatableSummons, unanswerableSummons, summonWindows, summonIds) =
            DuelProfileRegistration.OrdinarySummons(summonLevelModifiers, procedures, windows, this.rules, specialSummonCards, eventIds);
        summonEffects = ValidateSummonEffects(experimentalSummonEffects ?? [],
            eventIds.Concat(summonIds));
        var pendulums = (experimentalPendulumCards ?? []).ToArray();
        if (pendulums.Any(p => p is null || p.CardId == 0 || !IsMonster(p.CardId) || IsTokenId(p.CardId) ||
                materialProcedures.Values.Any(m => (m.Xyz is not null || m.Synchro is not null) && m.TargetCardIds.Contains(p.CardId)) ||
                summonEffects.Values.Any(s => s.FusionRecipes.Any(r => r.TargetCardId == p.CardId) || s.RitualRecipes.Any(r => r.TargetCardId == p.CardId))) ||
            pendulums.Select(p => p.CardId).Distinct().Count() != pendulums.Length)
            throw new ArgumentException("Pendulum identities require distinct explicitly classified ordinary monsters; hybrid families are unsupported.");
        pendulumCards = pendulums.ToFrozenDictionary(p => p.CardId);
        var definitionList = (effectDefinitions ?? []).ToArray();
        var usedEffectIds = eventIds.Concat(summonIds)
            .Concat(summonEffects.Values.Select(p => p.EffectId)).ToHashSet(StringComparer.Ordinal);
        EffectDefinitionRegistration.Validate(definitionList, knownIds, this.rules, usedEffectIds, summonEffects,
            this.decisionSequences, triggerEffects, shuffleSeed);
        // Attack-declaration and Damage Step responses only have a window in the staged flow.
        this.experimentalBattleFlow = this.experimentalBattleFlow || definitionList.Any(d => d.Kind is EffectKind.MonsterQuick or EffectKind.SpellTrapQuick || d.Conditions.Any(c => c is OpponentAttackDeclaredCondition or BattleTimingCondition));
        definitions = definitionList.GroupBy(d => d.CardId).ToFrozenDictionary(g => g.Key, g => g.First());
        effectsByCard = definitionList.GroupBy(d => d.CardId).ToFrozenDictionary(g => g.Key, g => g.ToArray());
        effectsById = definitionList.ToFrozenDictionary(d => d.EffectId, StringComparer.Ordinal);
        allEffects = definitionList;
        actionEffects = new(effectsByCard, this.rules);
        ruleLockProfiles = DuelProfileRegistration.Locks(ruleLocks, this.rules);
        battleAbilityProfiles = DuelProfileRegistration.Battle(battleAbilities, this.rules);
        replacementProfiles = DuelProfileRegistration.Replacements(destructionReplacements, this.rules);
        (redirectProfiles, damageRuleProfiles) = DuelProfileRegistration.RedirectsAndDamage(graveyardRedirects, damageRules, this.rules);
        statProfiles = DuelProfileRegistration.Statistics(monsterStats, this.rules);
        definitionDecisions = EffectDefinitionRegistration.Decisions(definitionList, triggerEffects, StepFilters);
        bool IsMonster(uint id) => this.rules.TryGetValue(id, out var rule) && rule.Category == CardCategory.Monster;
        State = DuelSetup.InitialState(knownIds, this.rules, playerOneDeck, playerTwoDeck, startingPlayer,
            playerOneExtraDeck, playerTwoExtraDeck, shuffleSeed, startingLifePoints);
    }

    private bool IsTokenId(uint id) => rules.TryGetValue(id, out var rule) && rule.IsToken;

    /// <summary>
    /// True when a lock from another source forbids this player's Summons this turn. A lock never restricts the
    /// steps of the activation that applied it (its "other monsters" exception) or Sets.
    /// </summary>
    private static bool IsSummonLocked(DuelState state, PlayerId player, CardInstance? exemptSource = null) =>
        state.SummonLocks.Any(l => l.Player == player && l.TurnNumber == state.TurnNumber && l.Source != exemptSource);

    /// <summary>Allocates a fresh, never-reused instance for a card created during the duel.</summary>
    private static CardInstance AllocateInstance(ref DuelState state, uint gameCardId, PlayerId owner)
    {
        var card = new CardInstance(state.NextInstanceId, gameCardId, owner);
        state = state.WithNextInstanceId(checked(state.NextInstanceId + 1));
        return card;
    }

    public DuelActionResult Execute(DuelCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (State.Outcome is not null) return Reject(DuelError.DuelFinished);
        if (command.Player is not (PlayerId.One or PlayerId.Two)) return Reject(DuelError.InvalidPlayer);
        if (State.EventEffects.PendingCommitment is not null) return AnswerTriggerCommitment(command);
        if (command is ChooseTriggerCostCard or ChooseTriggerMonsterTarget or ChooseTriggerXyzMaterials or ChooseTriggerZoneTarget) return Reject(DuelError.NoPendingTriggerCommitment);
        if (State.EventEffects.PendingTriggers is not null)
            return command is ChooseTriggerOrder order ? OrderTriggers(order) : Reject(DuelError.TriggerOrderPending);
        if (command is ChooseTriggerOrder) return Reject(DuelError.NoPendingTriggers);
        if (State.OpenWindow is not null) return ExecuteOpenWindow(command);
        if (State.PendingChain is not null) return ExecuteChain(command);
        if (State.PendingSummon is not null) return ExecuteSummonWindow(command);
        if (command is PassSummonResponse or ActivateSummonNegation) return Reject(DuelError.NoPendingSummon);
        if (command is PassChainResponse or ActivateChainResponse or ActivateHandChainResponse or ActivateMonsterChainResponse or ActivateZoneEffectResponse or ActivateFieldEffectResponse or ResolveNextChainLink or
            ChooseEffectSummon or ChooseDefinitionSummon or FinishDefinitionSummon or FinishDefinitionCardChoice or ChooseDefinitionCard or CallCardType or DeclareValue) return Reject(DuelError.NoPendingChain);
        // A definition answering its cost/target decisions before its first link exists (hand, set or open-window
        // activation); the decision may belong to the non-turn player.
        if (State.PendingDecision is not null && State.PendingSpellActivation is { } activating &&
            definitions.ContainsKey(activating.Spell.GameCardId))
            return command is DecisionCommand decision ? AnswerDecision(decision) : Reject(DuelError.DecisionPending);
        if (command is AdvanceBattle or PassBattleResponse or ChooseAttackReplay or ActivateBattleResponse or ActivateHandBattleResponse or ActivateMonsterBattleResponse or ActivateZoneEffectBattleResponse or ActivateFieldEffectBattleResponse)
        {
            if (!experimentalBattleFlow) return Reject(DuelError.BattleFlowNotEnabled);
            if (State.Battle is null) return Reject(DuelError.NoBattleInProgress);
        }
        if (State.Battle is not null)
            return State.ResearchSliceComplete ? Reject(DuelError.ResearchSliceComplete) : ExecuteBattleFlow(command);
        if (command.Player != State.ActivePlayer) return Reject(DuelError.NotActivePlayer);
        if (State.ResearchSliceComplete) return Reject(DuelError.ResearchSliceComplete);
        if (command is DecisionCommand answer) return AnswerDecision(answer);
        if (State.PendingDecision is not null) return Reject(DuelError.DecisionPending);
        return command switch
        {
            BeginDecisionSequence begin => BeginDecisions(begin.SequenceId),
            ActivateSpell activate => Activate(activate.CardInstanceId, activate.SpellTrapZone, activate.EffectIndex),
            ActivateSetSpell activate => ActivateSetDefinition(activate.SpellTrapZone, activate.EffectIndex),
            ActivateCardEffect effect => BeginFieldEffect(effect.Player, effect.SpellTrapZone, effect.EffectIndex),
            ActivateZoneEffect zoneEffect => BeginZoneEffect(zoneEffect.Player, zoneEffect.CardInstanceId, zoneEffect.EffectIndex, null),
            ActivatePendulumScale scale => ActivateScale(scale),
            PendulumSummon pendulum => ExecutePendulumSummon(pendulum),
            ResolveDrawStep => Draw(),
            ActivatePendulumEffect pendulum => BeginPendulumEffect(pendulum.Player, pendulum.PendulumZone, pendulum.EffectIndex),
            ChangePhase phase => RequestPhase(phase.Target),
            NormalSummon summon => Summon(summon.CardInstanceId, summon.MonsterZone, false),
            SetMonster set => Summon(set.CardInstanceId, set.MonsterZone, true),
            TributeSummon tribute => SummonWithTributes(tribute),
            SpecialSummonByProcedure special => ExecuteSpecialProcedure(special),
            SpecialSummonWithMaterials material => ExecuteMaterialProcedure(material),
            SetSpellTrap set => SetBackRow(set.CardInstanceId, set.SpellTrapZone),
            ChangeMonsterPosition change => ChangePosition(change.MonsterZone, change.Target),
            GeminiSummon gemini => ExecuteGeminiSummon(gemini.MonsterZone),
            ActivateMonsterEffect effect => BeginMonsterEffect(effect.Player, effect.MonsterZone, null, detach: effect.DetachInstanceIds, effectIndex: effect.EffectIndex),
            UnequipUnion unequip => ExecuteUnequip(unequip.Player, unequip.SpellTrapZone),
            ResolveUpkeep upkeep => ExecuteUpkeep(upkeep),
            ToonSummon toon => ExecuteToonSummon(toon),
            DeclareAttack attack => Attack(attack.AttackerZone, attack.TargetZone),
            DiscardForHandLimit discard => Discard(discard.CardInstanceId),
            EndTurn => FinishTurn(),
            _ => Reject(DuelError.UnsupportedCommand)
        };
    }

    private DuelActionResult Draw()
    {
        if (State.Phase != DuelPhase.Draw) return Reject(DuelError.WrongPhase);
        var player = State.GetPlayer(State.ActivePlayer);
        var next = State;
        List<DuelEvent> events = [];
        if (State.TurnNumber == 1) events.Add(new FirstDrawSkipped(player.Id));
        // A skipped Draw Phase draws nothing, so an empty Deck does not lose the duel.
        else if (PhaseSkipPending(next, player.Id, DuelPhase.Draw)) next = ConsumePhaseSkip(next, player.Id, DuelPhase.Draw, events);
        else if (player.Deck.Count == 0)
        {
            var outcome = new DuelOutcome(Other(player.Id), player.Id, DuelEndReason.UnableToDraw);
            return Accept(State.With(outcome: outcome), new DuelEnded(outcome));
        }
        else if (!RuleLocked(State, LockedAction.Draw, player.Id, null))
        {
            var card = player.Deck[0];
            next = next.With(player: player.With(deck: player.Deck.Skip(1), hand: player.Hand.Append(card)));
            events.Add(new CardDrawn(player.Id, card));
        }
        if (PhaseSkipPending(next, player.Id, DuelPhase.Standby))
        {
            next = ConsumePhaseSkip(next, player.Id, DuelPhase.Standby, events);
            events.Add(new PhaseChanged(DuelPhase.Draw, DuelPhase.Main1));
            return Accept(next.With(phase: DuelPhase.Main1), events.ToArray());
        }
        events.Add(new PhaseChanged(DuelPhase.Draw, DuelPhase.Standby));
        return Accept(next.With(phase: DuelPhase.Standby), events.ToArray());
    }

    private DuelError PhaseChangeError(DuelPhase target)
    {
        if (State.Phase == DuelPhase.Main1 && target == DuelPhase.Battle && State.TurnNumber == 1)
            return DuelError.FirstTurnBattleNotAllowed;
        if (State.Phase == DuelPhase.Main1 && target == DuelPhase.Battle && PhaseSkipPending(State, State.ActivePlayer, DuelPhase.Battle) ||
            State.Phase == DuelPhase.Battle && target == DuelPhase.Main2 && PhaseSkipPending(State, State.ActivePlayer, DuelPhase.Main2))
            return DuelError.PhaseSkipped;
        bool valid = (State.Phase, target) is
            (DuelPhase.Standby, DuelPhase.Main1) or
            (DuelPhase.Main1, DuelPhase.Battle) or (DuelPhase.Main1, DuelPhase.End) or
            (DuelPhase.Battle, DuelPhase.Main2) or (DuelPhase.Main2, DuelPhase.End) ||
            (State.Phase, target) is (DuelPhase.Battle, DuelPhase.End) && PhaseSkipPending(State, State.ActivePlayer, DuelPhase.Main2);
        if (!valid) return DuelError.InvalidPhaseTransition;
        if ((State.Phase == DuelPhase.Main1 && target == DuelPhase.End ||
                State.Phase == DuelPhase.Battle && target is DuelPhase.Main2 or DuelPhase.End) && RequiredAttackPending()) return DuelError.MustAttack;
        return DuelError.None;
    }

    /// <summary>
    /// Applies a legal phase change, optionally from a prepared state and after a leading event. Entering the End
    /// Phase carries out the banishments scheduled for this turn.
    /// </summary>
    private DuelActionResult Advance(DuelPhase target, DuelState? from = null, DuelEvent? lead = null)
    {
        if (PhaseChangeError(target) is not DuelError.None and var error) return Reject(error);
        var state = from ?? State;
        List<DuelEvent> events = lead is null ? [] : [lead];
        if (target == DuelPhase.Battle && experimentalBattleFlow)
        {
            var battle = new BattleState(BattleStep.Start, null, NewBattleWindow(BattleTiming.Start, State.ActivePlayer));
            return Accept(state.With(phase: target).WithBattle(battle), [.. events, new PhaseChanged(State.Phase, target),
                new BattleStepChanged(null, BattleStep.Start), new BattleResponseRequested(battle.Window)]);
        }
        events.Add(new PhaseChanged(State.Phase, target));
        state = state.With(phase: target);
        if (target == DuelPhase.End) state = SpendTurnSkips(ResolveEndPhaseBanishments(state, events), events);
        return Accept(state, events.ToArray());
    }

    private DuelActionResult Summon(int instanceId, int zone, bool faceDown)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        var player = State.GetPlayer(State.ActivePlayer);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == instanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (!rules.TryGetValue(card.GameCardId, out var rule) || rule.Category == CardCategory.Unknown)
            return Reject(DuelError.UnknownCardRules);
        if (rule.Category != CardCategory.Monster) return Reject(DuelError.NotMonster);
        if (!rule.AllowsNormalSummon) return Reject(DuelError.NormalSummonNotAllowed);
        if (rule.RequiresRegisteredTributeProcedure) return Reject(DuelError.UnsupportedTributeProcedure);
        uint level = ComputeHandSummonLevel(State, player.Id, card);
        if (level == 0) return Reject(DuelError.UnsupportedMonsterLevel);
        if (level > 4) return Reject(DuelError.TributeSummonNotSupported);
        if (player.MonsterZones[zone] is not null) return Reject(DuelError.MonsterZoneOccupied);
        if (!NormalSummonAvailable(State)) return Reject(DuelError.NormalSummonLimitReached);
        if (!faceDown && IsSummonLocked(State, player.Id)) return Reject(DuelError.SummonLocked);
        if (RuleLocked(State, faceDown ? LockedAction.SetMonster : LockedAction.NormalSummon, player.Id, card)) return Reject(DuelError.LockedByEffect);
        var next = SpendNormalSummon(State.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != instanceId))));
        return CommitSummon(next, player.Id, card, zone, faceDown, []);
    }

    private DuelActionResult SetBackRow(int instanceId, int zone)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or > DuelPlayerState.FieldZoneIndex) return Reject(DuelError.InvalidSpellTrapZone);
        var player = State.GetPlayer(State.ActivePlayer);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == instanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (!rules.TryGetValue(card.GameCardId, out var rule) || rule.Category == CardCategory.Unknown)
            return Reject(DuelError.UnknownCardRules);
        if (rule.Category is not (CardCategory.Spell or CardCategory.Trap)) return Reject(DuelError.NotSpellOrTrap);
        if (!rule.AllowsSpellTrapSet) return Reject(DuelError.SpellTrapSetNotAllowed);
        if (RuleLocked(State, LockedAction.SetSpellTrap, player.Id, card)) return Reject(DuelError.LockedByEffect);
        bool fieldSpell = definitions.TryGetValue(card.GameCardId, out var definition) && definition.Kind == EffectKind.FieldSpell;
        if (fieldSpell != (zone == DuelPlayerState.FieldZoneIndex)) return Reject(DuelError.InvalidSpellTrapZone);
        if (!fieldSpell && player.SpellTrapZones[zone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        var next = State;
        List<DuelEvent> events = [];
        if (fieldSpell && player.FieldSpell is { } old) next = ReplaceFieldSpell(next, player.Id, old, events);
        var current = next.GetPlayer(player.Id);
        events.Add(new SpellTrapSet(player.Id, card, zone));
        return Accept(next.With(player: current.With(hand: current.Hand.Where(c => c.InstanceId != instanceId))
            .WithSpellTrapAt(zone, new(card, IsFaceUp: false, PlacementId: checked(State.Revision + 1), SetOnTurn: State.TurnNumber))), events.ToArray());
    }

    private DuelActionResult ChangePosition(int zone, BattlePosition target)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        var player = State.GetPlayer(State.ActivePlayer);
        var monster = player.MonsterZones[zone];
        if (monster is null) return Reject(DuelError.MonsterZoneEmpty);
        if (target is not (BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense) || target == monster.Position ||
            monster.Position == BattlePosition.FaceDownDefense && target != BattlePosition.FaceUpAttack)
            return Reject(DuelError.InvalidBattlePosition);
        if (monster.SummonedOnTurn == State.TurnNumber || monster.PositionChangedOnTurn == State.TurnNumber ||
            monster.LastAttackTurn == State.TurnNumber) return Reject(DuelError.PositionChangeNotAllowed);
        // Registered flip-summon triggers are collected after this transition.
        // Unregistered flip effects and position-change restrictions remain unsupported.
        if (!rules[monster.Card.GameCardId].AllowsBasicBattle) return Reject(DuelError.UnsupportedBattleCard);
        if (RuleLocked(State, LockedAction.ChangePosition, player.Id, monster.Card, monster)) return Reject(DuelError.LockedByEffect);
        bool flip = monster.Position == BattlePosition.FaceDownDefense;
        if (flip && IsSummonLocked(State, player.Id)) return Reject(DuelError.SummonLocked);
        var changed = monster with { Position = target, PositionChangedOnTurn = State.TurnNumber,
            SummonedOnTurn = flip ? State.TurnNumber : monster.SummonedOnTurn };
        if (flip)
        {
            // A Flip Summon pauses only when the opponent has a set card that can negate it.
            var pending = new PendingSummon(checked(State.Revision + 1), checked(State.Revision + 1), player.Id, monster.Card, zone, Other(player.Id),
                Position: target) { IsFlipSummon = true };
            var attempted = State.WithSummon(pending);
            if (OpensSummonWindow(attempted, monster.Card.GameCardId, optedIn: false))
                return Accept(attempted, new SummonAttempted(pending) { TimingGroup = 1 }, new SummonResponseRequested(pending));
        }
        var zones = player.MonsterZones.ToArray();
        zones[zone] = changed;
        return Accept(State.With(player: player.With(monsters: zones)), flip
            ? new MonsterFlipSummoned(player.Id, changed, zone)
            : new MonsterPositionChanged(player.Id, zone, monster.Position, target));
    }

    private DuelActionResult Attack(int attackerZone, int? targetZone)
    {
        if (State.Phase != DuelPhase.Battle) return Reject(DuelError.WrongPhase);
        if (State.TurnNumber == 1) return Reject(DuelError.FirstTurnBattleNotAllowed);
        if (attackerZone is < 0 or >= 5 || targetZone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        var attackerPlayer = State.GetPlayer(State.ActivePlayer);
        var defenderPlayer = State.GetPlayer(Other(State.ActivePlayer));
        var attacker = attackerPlayer.MonsterZones[attackerZone];
        if (attacker is null) return Reject(DuelError.MonsterZoneEmpty);
        if (attacker.Position != BattlePosition.FaceUpAttack) return Reject(DuelError.AttackerNotInAttackPosition);
        if (AttackAllowanceError(attackerPlayer, attacker, targetZone) is not DuelError.None and var allowanceError) return Reject(allowanceError);
        FieldMonster? defender = targetZone is int zone ? defenderPlayer.MonsterZones[zone] : null;
        if (targetZone is not null && defender is null) return Reject(DuelError.MonsterZoneEmpty);
        if (targetZone is null && !CanAttackDirectly(defenderPlayer, attacker))
            return Reject(DuelError.DirectAttackBlocked);
        if (AttackRestrictionError(attackerPlayer, defenderPlayer, attacker, targetZone) is not DuelError.None and var toonError) return Reject(toonError);
        if (!rules[attacker.Card.GameCardId].AllowsBasicBattle ||
            defender is not null && !rules[defender.Card.GameCardId].AllowsBasicBattle)
            return Reject(DuelError.UnsupportedBattleCard);
        // A battle-revealed monster's continuous effects apply before damage calculation.
        var calculationState = State;
        if (defender?.Position == BattlePosition.FaceDownDefense)
        {
            var revealed = defenderPlayer.MonsterZones.ToArray();
            revealed[targetZone!.Value] = defender with { Position = BattlePosition.FaceUpDefense };
            calculationState = State.With(player: defenderPlayer.With(monsters: revealed));
        }
        var attackStats = GetMonsterStatistics(calculationState, attackerPlayer.Id, attackerZone)!;
        var defenseStats = defender is null ? null : GetMonsterStatistics(calculationState, defenderPlayer.Id, targetZone!.Value);
        int? defendingValue = defender?.Position == BattlePosition.FaceUpAttack ? defenseStats?.Attack : defenseStats?.Defense;
        if (attackStats.Attack is not int attack || defender is not null && defendingValue is null)
            return Reject(DuelError.UnsupportedBattleStatistics);

        List<DuelEvent> events = [];
        attackerPlayer = PayToonAttack(attackerPlayer, attacker, events);
        events.Add(new AttackDeclared(attackerPlayer.Id, attackerZone, targetZone));
        var attackZones = attackerPlayer.MonsterZones.ToArray();
        var defenseZones = defenderPlayer.MonsterZones.ToArray();
        attackZones[attackerZone] = attacker with { LastAttackTurn = State.TurnNumber };
        if (defender?.Position == BattlePosition.FaceDownDefense)
        {
            defenseZones[targetZone!.Value] = defender with { Position = BattlePosition.FaceUpDefense, FlippedOnTurn = State.TurnNumber };
            events.Add(new MonsterRevealed(defenderPlayer.Id, targetZone.Value, defender.Card));
        }
        var calculation = CalculateBasicBattle(attack, defendingValue, defender?.Position, attackerPlayer.Id, defenderPlayer.Id,
            GetBattleAbilities(calculationState, attackerPlayer.Id, attacker).Piercing);
        bool destroyAttacker = calculation.DestroyAttacker, destroyDefender = calculation.DestroyDefender;
        int damage = calculation.Damage;
        PlayerId damagedPlayer = calculation.DamagedPlayer;
        attackerPlayer = attackerPlayer.With(monsters: attackZones);
        defenderPlayer = defenderPlayer.With(monsters: defenseZones);
        DuelState next = State.With(player: attackerPlayer).With(player: defenderPlayer);
        next = RecordBattleAttack(next, checked(State.Revision + 1),
            new(attackerPlayer.Id, attackerZone, attacker.Card, attacker.PlacementId),
            defender is null ? null : new(defenderPlayer.Id, targetZone!.Value, defender.Card, defender.PlacementId));
        if (damage > 0)
        {
            // LP defeat occurs during damage calculation, before sending battle-destroyed cards to the graveyard.
            next = ApplyBattleDamage(next, damagedPlayer, damage, events);
            if (next.Outcome is not null) return Accept(next, events.ToArray());
        }
        MonsterBinding attackerBinding = new(attackerPlayer.Id, attackerZone, attacker.Card, attacker.PlacementId);
        MonsterBinding? defenderBinding = defender is null ? null : new(defenderPlayer.Id, targetZone!.Value, defender.Card, defender.PlacementId);
        void Destroy(PlayerId playerId, int zone, MonsterBinding? destroyedBy)
        {
            var player = next.GetPlayer(playerId);
            var zones = player.MonsterZones.ToArray();
            var card = zones[zone]!.Card;
            if (ReplaceDestruction(next, new(playerId, zone, card, zones[zone]!.PlacementId), DestructionCause.Battle, events, destroyedBy) is { } replaced)
            {
                next = replaced;
                return;
            }
            zones[zone] = null;
            next = next.With(player: player.With(monsters: zones));
            next = SendFieldCardToGraveyard(next, card, new MonsterDestroyed(playerId, zone, card) { ByBattle = true, DestroyedBy = destroyedBy }, events);
        }
        if (destroyAttacker) Destroy(attackerPlayer.Id, attackerZone, defenderBinding);
        if (destroyDefender) Destroy(defenderPlayer.Id, targetZone!.Value, attackerBinding);
        return Accept(next, events.ToArray());
    }

    private DuelActionResult Discard(int instanceId)
    {
        if (State.Phase != DuelPhase.End) return Reject(DuelError.WrongPhase);
        var player = State.GetPlayer(State.ActivePlayer);
        if (player.Hand.Count <= 6) return Reject(DuelError.DiscardNotRequired);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == instanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        var next = player.With(hand: player.Hand.Where(c => c.InstanceId != instanceId), graveyard: player.Graveyard.Append(card));
        return Accept(State.With(player: next), new CardDiscarded(player.Id, card));
    }

    private DuelActionResult FinishTurn()
    {
        if (State.Phase != DuelPhase.End) return Reject(DuelError.WrongPhase);
        if (AnyUpkeepDue(State.ActivePlayer)) return Reject(DuelError.UpkeepPending);
        if (State.GetPlayer(State.ActivePlayer).Hand.Count > 6) return Reject(DuelError.HandLimitExceeded);
        var player = Other(State.ActivePlayer);
        var next = State.With(activePlayer: player, turnNumber: State.TurnNumber + 1,
            phase: DuelPhase.Draw, normalSummonUsed: false);
        return Accept(next, new TurnStarted(player, next.TurnNumber), new PhaseChanged(DuelPhase.End, DuelPhase.Draw));
    }

    private DuelActionResult Accept(DuelState state, params DuelEvent[] events)
    {
        var emitted = events.ToList();
        state = PrepareBattleState(State, state, emitted);
        state = PrepareXyzMaterials(State, state, emitted);
        state = ApplyFieldLinks(State, state, emitted);
        int beforeReturns = emitted.Count;
        state = ApplyControlReturns(state, emitted);
        // A monster sent to the Graveyard because its control ended with no free zone leaves its Equip cards behind.
        if (emitted.Skip(beforeReturns).Any(e => e is MonsterSentToGraveyardByRule)) state = ApplyFieldLinks(State, state, emitted);
        state = DropLinkDamageRules(state);
        state = ApplyGraveyardRedirects(state, emitted);
        state = PrepareSummonHistory(State, state, emitted);
        state = PruneNameChanges(state);
        identityView = state;
        try { state = PrepareEventEffects(State, state, emitted); }
        finally { identityView = null; }
        State = state.With(revision: checked(State.Revision + 1));
        TrackSummons(emitted);
        return new(true, DuelError.None, State, emitted.AsReadOnly());
    }

    private DuelActionResult Reject(DuelError error) => new(false, error, State, Array.Empty<DuelEvent>());
    private static PlayerId Other(PlayerId player) => player == PlayerId.One ? PlayerId.Two : PlayerId.One;
}
