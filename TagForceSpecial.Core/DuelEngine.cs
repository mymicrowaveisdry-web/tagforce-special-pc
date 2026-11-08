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
    private readonly FrozenDictionary<uint, ChainResponseProfile> chainResponses;
    private readonly TriggerEffectProfile[] triggerEffects;
    private readonly ContinuousEffectProfile[] continuousEffects;
    private readonly ContinuousNegationProfile[] continuousNegations;
    private readonly uint[] negationOrder;
    private readonly bool experimentalBattleFlow;
    private readonly FrozenDictionary<uint, BattleResponseProfile> battleResponses;
    private readonly SummonLevelModifierProfile[] summonLevelModifiers;
    private readonly FrozenDictionary<string, TributeProcedureProfile> tributeProcedures;
    private readonly FrozenSet<uint> summonWindows;
    private readonly FrozenDictionary<uint, SummonNegationProfile> summonNegations;
    private readonly FrozenDictionary<uint, SpecialSummonCardProfile> specialSummonCards;
    private readonly FrozenDictionary<string, SpecialSummonProcedureProfile> specialSummonProcedures;
    private readonly FrozenDictionary<string, ExtraDeckMaterialProcedureProfile> materialProcedures;
    private readonly FrozenDictionary<uint, SummonEffectProfile> summonEffects;
    private readonly FrozenDictionary<uint, PendulumCardProfile> pendulumCards;
    /// <summary>Definition-driven card effects, keyed by source card ID.</summary>
    private readonly FrozenDictionary<uint, EffectDefinition> definitions;
    /// <summary>Activation-time cost/target decisions of definitions that have any, keyed by source card ID.</summary>
    private readonly FrozenDictionary<uint, DecisionSequence> definitionDecisions;
    public DuelState State { get; private set; }

    /// <summary>
    /// Decks are supplied top-first and checked only for size and catalog membership. Missing rule classifications fail
    /// closed when summoning.
    /// </summary>
    public DuelEngine(CardCatalog catalog, IReadOnlyDictionary<uint, DuelCardRules> rules,
        IEnumerable<uint> playerOneDeck, IEnumerable<uint> playerTwoDeck, PlayerId startingPlayer = PlayerId.One,
        IEnumerable<DecisionSequence>? decisionSequences = null,
        IEnumerable<ChainResponseProfile>? experimentalChainResponses = null,
        IEnumerable<TriggerEffectProfile>? experimentalTriggerEffects = null,
        IEnumerable<ContinuousEffectProfile>? experimentalContinuousEffects = null,
        IEnumerable<ContinuousNegationProfile>? experimentalContinuousNegations = null,
        bool experimentalBattleFlow = false, IEnumerable<BattleResponseProfile>? experimentalBattleResponses = null,
        IEnumerable<SummonLevelModifierProfile>? experimentalSummonLevelModifiers = null,
        IEnumerable<TributeProcedureProfile>? experimentalTributeProcedures = null,
        IEnumerable<SummonWindowProfile>? experimentalSummonWindows = null,
        IEnumerable<SummonNegationProfile>? experimentalSummonNegations = null,
        IEnumerable<SpecialSummonCardProfile>? experimentalSpecialSummonCards = null,
        IEnumerable<SpecialSummonProcedureProfile>? experimentalSpecialSummonProcedures = null,
        IEnumerable<uint>? playerOneExtraDeck = null, IEnumerable<uint>? playerTwoExtraDeck = null,
        IEnumerable<ExtraDeckMaterialProcedureProfile>? experimentalMaterialProcedures = null,
        IEnumerable<SummonEffectProfile>? experimentalSummonEffects = null,
        IEnumerable<PendulumCardProfile>? experimentalPendulumCards = null,
        IEnumerable<EffectDefinition>? effectDefinitions = null, ulong? shuffleSeed = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(playerOneDeck);
        ArgumentNullException.ThrowIfNull(playerTwoDeck);
        if (startingPlayer is not (PlayerId.One or PlayerId.Two))
            throw new ArgumentOutOfRangeException(nameof(startingPlayer));
        this.catalog = catalog;
        var knownIds = catalog.Cards.Select(c => c.GameCardId).ToHashSet();
        foreach (var (id, profile) in rules)
            if (!knownIds.Contains(id) || profile is null || !Enum.IsDefined(profile.Category) ||
                (profile.AllowsNormalSummon || profile.AllowsBasicBattle) && profile.Category != CardCategory.Monster ||
                (profile.AllowsTributeSummon || profile.AllowsTributeForSummon) && profile.Category != CardCategory.Monster ||
                (profile.AllowsExtraDeck || profile.AllowsSummonMaterial || profile.AllowsPendulumSummon) && profile.Category != CardCategory.Monster ||
                profile.AllowsTributeSummon && !profile.AllowsNormalSummon ||
                profile.RequiresRegisteredTributeProcedure && !profile.AllowsTributeSummon ||
                profile.AllowsSpellTrapSet && profile.Category is not (CardCategory.Spell or CardCategory.Trap) ||
                profile.IsToken && (profile.Category != CardCategory.Monster || profile.AllowsNormalSummon ||
                    profile.AllowsTributeSummon || profile.AllowsExtraDeck || profile.AllowsPendulumSummon))
                throw new ArgumentException("Invalid explicit card rules.", nameof(rules));
        this.rules = rules.ToFrozenDictionary();
        var sequences = (decisionSequences ?? []).ToArray();
        if (sequences.Any(s => s is null || s.Steps.Any(step => step.AllowedCardIds.Any(id => !knownIds.Contains(id)))))
            throw new ArgumentException("Decision sequences must reference catalog IDs.", nameof(decisionSequences));
        this.decisionSequences = sequences.ToFrozenDictionary(s => s.Id, StringComparer.Ordinal);
        var responses = (experimentalChainResponses ?? []).ToArray();
        if (responses.Any(p => p is null || !this.rules.TryGetValue(p.CardId, out var rule) ||
                rule.Category is not (CardCategory.Spell or CardCategory.Trap) || !rule.AllowsSpellTrapSet ||
                p.DiscardCardIds.Any(id => !knownIds.Contains(id)) ||
                p.Decisions is { } decisions && this.decisionSequences.ContainsKey(decisions.Id)) ||
            responses.Select(p => p.EffectId).Distinct(StringComparer.Ordinal).Count() != responses.Length)
            throw new ArgumentException("Response profiles need explicit set rules, distinct IDs and catalog costs.", nameof(experimentalChainResponses));
        chainResponses = responses.ToFrozenDictionary(p => p.CardId);
        triggerEffects = (experimentalTriggerEffects ?? []).ToArray();
        continuousEffects = (experimentalContinuousEffects ?? []).ToArray();
        continuousNegations = (experimentalContinuousNegations ?? []).ToArray();
        if (triggerEffects.Any(p => p is null || !this.rules.TryGetValue(p.CardId, out var rule) || rule.Category != CardCategory.Monster ||
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
        var eventIds = triggerEffects.Select(p => p.EffectId).Concat(continuousEffects.Select(p => p.EffectId))
            .Concat(continuousNegations.Select(p => p.EffectId)).ToArray();
        var reservedIds = responses.Select(p => p.EffectId).ToHashSet(StringComparer.Ordinal);
        if (eventIds.Distinct(StringComparer.Ordinal).Count() != eventIds.Length || eventIds.Any(reservedIds.Contains))
            throw new ArgumentException("Event effect IDs must be distinct and unreserved.");
        this.experimentalBattleFlow = experimentalBattleFlow;
        var battleProfiles = (experimentalBattleResponses ?? []).ToArray();
        if (battleProfiles.Length != 0 && !experimentalBattleFlow || battleProfiles.Any(p => p is null ||
            !this.rules.TryGetValue(p.CardId, out var sourceRule) || sourceRule.Category is not (CardCategory.Spell or CardCategory.Trap) ||
            !sourceRule.AllowsSpellTrapSet || p.SpellSpeed == ChainSpellSpeed.Three && sourceRule.Category != CardCategory.Trap ||
            chainResponses.ContainsKey(p.CardId) ||
            p.SummonableCardIds.Any(id => !this.rules.TryGetValue(id, out var targetRule) || targetRule.Category != CardCategory.Monster)) ||
            battleProfiles.Select(p => p.EffectId).Distinct(StringComparer.Ordinal).Count() != battleProfiles.Length ||
            battleProfiles.Any(p => reservedIds.Contains(p.EffectId) || eventIds.Contains(p.EffectId)))
            throw new ArgumentException("Battle profiles need the staged flow, distinct source/effect IDs and explicit rules.", nameof(experimentalBattleResponses));
        battleResponses = battleProfiles.ToFrozenDictionary(p => p.CardId);
        summonLevelModifiers = (experimentalSummonLevelModifiers ?? []).ToArray();
        var procedures = (experimentalTributeProcedures ?? []).ToArray();
        var windows = (experimentalSummonWindows ?? []).ToArray();
        var negators = (experimentalSummonNegations ?? []).ToArray();
        var specialCards = (experimentalSpecialSummonCards ?? []).ToArray();
        var specialProcedures = (experimentalSpecialSummonProcedures ?? []).ToArray();
        if (specialCards.Any(p => p is null || !IsMonster(p.CardId)) ||
            specialCards.Select(p => p.CardId).Distinct().Count() != specialCards.Length ||
            specialProcedures.Any(p => p is null || p.CardIds.Any(id => !specialCards.Any(c => c.CardId == id))) ||
            specialProcedures.Select(p => p.ProcedureId).Distinct(StringComparer.Ordinal).Count() != specialProcedures.Length)
            throw new ArgumentException("Special Summon procedures require distinct IDs and explicitly profiled catalog monsters.");
        specialSummonCards = specialCards.ToFrozenDictionary(p => p.CardId);
        specialSummonProcedures = specialProcedures.ToFrozenDictionary(p => p.ProcedureId, StringComparer.Ordinal);
        var materialProfiles = (experimentalMaterialProcedures ?? []).ToArray();
        if (materialProfiles.Any(p => p is null || p.TargetCardIds.Any(id => !specialSummonCards.ContainsKey(id) ||
                !this.rules.TryGetValue(id, out var rule) || !rule.AllowsExtraDeck) ||
                p.MaterialCardIds.Any(id => !this.rules.TryGetValue(id, out var rule) || !rule.AllowsSummonMaterial)) ||
            materialProfiles.Select(p => p.ProcedureId).Distinct(StringComparer.Ordinal).Count() != materialProfiles.Length ||
            materialProfiles.Any(p => specialSummonProcedures.ContainsKey(p.ProcedureId)) ||
            materialProfiles.Any(p => p.Xyz is not null && p.MaterialCardIds.Any(IsTokenId)))
            throw new ArgumentException("Material procedures require distinct IDs and explicit Extra Deck, target and material eligibility.");
        materialProcedures = materialProfiles.ToFrozenDictionary(p => p.ProcedureId, StringComparer.Ordinal);
        if (summonLevelModifiers.Any(p => p is null || !IsMonster(p.SourceCardId) || p.TargetCardIds.Any(id => !IsMonster(id))) ||
            procedures.Any(p => p is null || !this.rules.TryGetValue(p.CardId, out var rule) || !rule.AllowsTributeSummon ||
                p.AllowedTributeCardIds.Any(id => !this.rules.TryGetValue(id, out var donor) || !donor.AllowsTributeForSummon)) ||
            windows.Any(p => p is null || !this.rules.TryGetValue(p.CardId, out var rule) ||
                !rule.AllowsNormalSummon && !specialSummonCards.ContainsKey(p.CardId)) ||
            negators.Any(p => p is null || !this.rules.TryGetValue(p.CardId, out var rule) || rule.Category != CardCategory.Trap ||
                !rule.AllowsSpellTrapSet || chainResponses.ContainsKey(p.CardId) || battleResponses.ContainsKey(p.CardId)) ||
            procedures.Select(p => p.ProcedureId).Distinct(StringComparer.Ordinal).Count() != procedures.Length ||
            windows.Select(p => p.CardId).Distinct().Count() != windows.Length ||
            negators.Select(p => p.CardId).Distinct().Count() != negators.Length ||
            this.rules.Any(entry => entry.Value.RequiresRegisteredTributeProcedure && !procedures.Any(p => p.CardId == entry.Key)))
            throw new ArgumentException("Summon profiles require explicit source/target eligibility, distinct registrations and procedures for restricted targets.");
        var summonIds = summonLevelModifiers.Select(p => p.EffectId).Concat(negators.Select(p => p.EffectId)).ToArray();
        if (summonIds.Distinct(StringComparer.Ordinal).Count() != summonIds.Length ||
            summonIds.Any(id => reservedIds.Contains(id) || eventIds.Contains(id) || battleProfiles.Any(p => p.EffectId == id)))
            throw new ArgumentException("Summon effect IDs must be distinct and unreserved.");
        tributeProcedures = procedures.ToFrozenDictionary(p => p.ProcedureId, StringComparer.Ordinal);
        summonWindows = windows.Select(p => p.CardId).ToFrozenSet();
        summonNegations = negators.ToFrozenDictionary(p => p.CardId);
        summonEffects = ValidateSummonEffects(experimentalSummonEffects ?? [],
            reservedIds.Concat(eventIds).Concat(summonIds).Concat(battleProfiles.Select(p => p.EffectId)));
        var pendulums = (experimentalPendulumCards ?? []).ToArray();
        if (pendulums.Any(p => p is null || p.CardId == 0 || !IsMonster(p.CardId) || IsTokenId(p.CardId) ||
                materialProcedures.Values.Any(m => (m.Xyz is not null || m.Synchro is not null) && m.TargetCardIds.Contains(p.CardId)) ||
                summonEffects.Values.Any(s => s.FusionRecipes.Any(r => r.TargetCardId == p.CardId) || s.RitualRecipes.Any(r => r.TargetCardId == p.CardId))) ||
            pendulums.Select(p => p.CardId).Distinct().Count() != pendulums.Length)
            throw new ArgumentException("Pendulum identities require distinct explicitly classified ordinary monsters; hybrid families are unsupported.");
        pendulumCards = pendulums.ToFrozenDictionary(p => p.CardId);
        var definitionList = (effectDefinitions ?? []).ToArray();
        var usedEffectIds = reservedIds.Concat(eventIds).Concat(summonIds)
            .Concat(battleProfiles.Select(p => p.EffectId)).Concat(summonEffects.Values.Select(p => p.EffectId)).ToHashSet(StringComparer.Ordinal);
        // Definitions need matching settable card rules, Token rules for Token steps and a seed for shuffles.
        if (definitionList.Any(d => d is null ||
                d.Steps.OfType<SummonTokensStep>().Any(s => s.TokenCardIds.Any(id => !IsTokenId(id))) ||
                shuffleSeed is null && d.Steps.Any(s => s is ShuffleDeckStep or AddFromDeckToHandStep or SendFromDeckToGraveyardStep or ShuffleTargetsIntoDeckStep ||
                    s is SpecialSummonStep summon && summon.Sources.HasFlag(SpecialSummonSources.Deck)) ||
                !this.rules.TryGetValue(d.CardId, out var rule) || !rule.AllowsSpellTrapSet ||
                rule.Category != (d.Kind is EffectKind.NormalSpell or EffectKind.QuickPlaySpell ? CardCategory.Spell : CardCategory.Trap) ||
                chainResponses.ContainsKey(d.CardId) || battleResponses.ContainsKey(d.CardId) ||
                summonEffects.ContainsKey(d.CardId) || summonNegations.ContainsKey(d.CardId) || usedEffectIds.Contains(d.EffectId) ||
                this.decisionSequences.ContainsKey(DefinitionSequenceId(d))) ||
            definitionList.Select(d => d.CardId).Distinct().Count() != definitionList.Length ||
            definitionList.Select(d => d.EffectId).Distinct(StringComparer.Ordinal).Count() != definitionList.Length)
            throw new ArgumentException("Effect definitions require distinct, unreserved IDs, matching settable card rules, Token rules for Token steps and a seed for shuffle steps.", nameof(effectDefinitions));
        definitions = definitionList.ToFrozenDictionary(d => d.CardId);
        definitionDecisions = definitionList.Where(d => d.Cost is DiscardCost || d.Target is not null || d.FieldTargets.Any(t => !EffectDefinition.IsAutomatic(t)))
            .ToFrozenDictionary(d => d.CardId, d => new DecisionSequence(DefinitionSequenceId(d),
                [.. d.Cost is DiscardCost ? [DecisionStep.AnyCard(DecisionCardSource.Hand)] : Array.Empty<DecisionStep>(),
                 .. d.Target is SpellTrapOnFieldTarget ? [DecisionStep.SpellTrapTarget()] : Array.Empty<DecisionStep>(),
                 .. d.FieldTargets.Where(t => !EffectDefinition.IsAutomatic(t)).SelectMany(spec => Enumerable.Range(0, EffectDefinition.MaxCount(spec))
                     .Select(i => DecisionStep.FieldTargetChoice(spec, optional: i >= EffectDefinition.MinCount(spec))))],
                stopAfterCompletion: false));
        bool IsMonster(uint id) => this.rules.TryGetValue(id, out var rule) && rule.Category == CardCategory.Monster;
        int nextInstance = 1;
        DuelPlayerState CreatePlayer(PlayerId id, IEnumerable<uint> ids)
        {
            // Limit enumeration too: malformed/infinite sequences cannot allocate indefinitely.
            var deck = ids.Take(61).ToArray();
            if (deck.Length is < 40 or > 60 || deck.Any(cardId => !knownIds.Contains(cardId)))
                throw new ArgumentException("Each main deck needs 40–60 catalog IDs.");
            if (deck.Any(IsTokenId)) throw new ArgumentException("Tokens are created by effects and cannot be in a Deck.");
            var instances = deck.Select(cardId => new CardInstance(nextInstance++, cardId, id)).ToArray();
            return new(id, instances.Skip(5), instances.Take(5));
        }
        var first = CreatePlayer(PlayerId.One, playerOneDeck);
        var second = CreatePlayer(PlayerId.Two, playerTwoDeck);
        CardInstance[] CreateExtraDeck(PlayerId owner, IEnumerable<uint>? cards)
        {
            var ids = (cards ?? []).Take(16).ToArray();
            if (ids.Length > 15 || ids.Any(id => !this.rules.TryGetValue(id, out var rule) || !rule.AllowsExtraDeck))
                throw new ArgumentException("Each Extra Deck supports up to 15 explicitly eligible catalog monsters.");
            return ids.Select(id => new CardInstance(nextInstance++, id, owner)).ToArray();
        }
        var firstExtra = CreateExtraDeck(PlayerId.One, playerOneExtraDeck);
        var secondExtra = CreateExtraDeck(PlayerId.Two, playerTwoExtraDeck);
        State = new([first.With(extraDeck: firstExtra), second.With(extraDeck: secondExtra)], startingPlayer,
            bookkeeping: new(nextInstance, shuffleSeed is { } seed ? DuelRandomState.FromSeed(seed) : null));
    }

    private bool IsTokenId(uint id) => rules.TryGetValue(id, out var rule) && rule.IsToken;
    private static string DefinitionSequenceId(EffectDefinition definition) => $"activation:{definition.EffectId}";

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
        if (command is ChooseTriggerCostCard or ChooseTriggerMonsterTarget or ChooseTriggerXyzMaterials) return Reject(DuelError.NoPendingTriggerCommitment);
        if (State.EventEffects.PendingTriggers is not null)
            return command is ChooseTriggerOrder order ? OrderTriggers(order) : Reject(DuelError.TriggerOrderPending);
        if (command is ChooseTriggerOrder) return Reject(DuelError.NoPendingTriggers);
        if (State.OpenWindow is not null) return ExecuteOpenWindow(command);
        if (State.PendingChain is not null) return ExecuteChain(command);
        if (State.PendingSummon is not null) return ExecuteSummonWindow(command);
        if (command is PassSummonResponse or ActivateSummonNegation) return Reject(DuelError.NoPendingSummon);
        if (command is PassChainResponse or ActivateChainResponse or ActivateHandChainResponse or ResolveNextChainLink or
            ChooseEffectSummon or ChooseDefinitionSummon or ChooseDefinitionCard) return Reject(DuelError.NoPendingChain);
        // A definition answering its cost/target decisions before its first link exists (hand, set or open-window
        // activation); the decision may belong to the non-turn player.
        if (State.PendingDecision is not null && State.PendingSpellActivation is { } activating &&
            definitions.ContainsKey(activating.Spell.GameCardId))
            return command is DecisionCommand decision ? AnswerDecision(decision) : Reject(DuelError.DecisionPending);
        if (command is AdvanceBattle or PassBattleResponse or ChooseAttackReplay or ActivateBattleResponse or ActivateHandBattleResponse)
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
            ActivateSpell activate => Activate(activate.CardInstanceId, activate.SpellTrapZone),
            ActivateSetSpell activate => ActivateSetDefinition(activate.SpellTrapZone),
            ActivatePendulumScale scale => ActivateScale(scale),
            PendulumSummon pendulum => ExecutePendulumSummon(pendulum),
            ResolveDrawStep => Draw(),
            ChangePhase phase => RequestPhase(phase.Target),
            NormalSummon summon => Summon(summon.CardInstanceId, summon.MonsterZone, false),
            SetMonster set => Summon(set.CardInstanceId, set.MonsterZone, true),
            TributeSummon tribute => SummonWithTributes(tribute),
            SpecialSummonByProcedure special => ExecuteSpecialProcedure(special),
            SpecialSummonWithMaterials material => ExecuteMaterialProcedure(material),
            SetSpellTrap set => SetBackRow(set.CardInstanceId, set.SpellTrapZone),
            ChangeMonsterPosition change => ChangePosition(change.MonsterZone, change.Target),
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
        if (State.TurnNumber == 1)
            return Accept(State.With(phase: DuelPhase.Standby), new FirstDrawSkipped(player.Id),
                new PhaseChanged(DuelPhase.Draw, DuelPhase.Standby));
        if (player.Deck.Count == 0)
        {
            var outcome = new DuelOutcome(Other(player.Id), player.Id, DuelEndReason.UnableToDraw);
            return Accept(State.With(outcome: outcome), new DuelEnded(outcome));
        }
        var card = player.Deck[0];
        var next = player.With(deck: player.Deck.Skip(1), hand: player.Hand.Append(card));
        return Accept(State.With(player: next, phase: DuelPhase.Standby), new CardDrawn(player.Id, card),
            new PhaseChanged(DuelPhase.Draw, DuelPhase.Standby));
    }

    private DuelError PhaseChangeError(DuelPhase target)
    {
        if (State.Phase == DuelPhase.Main1 && target == DuelPhase.Battle && State.TurnNumber == 1)
            return DuelError.FirstTurnBattleNotAllowed;
        bool valid = (State.Phase, target) is
            (DuelPhase.Standby, DuelPhase.Main1) or
            (DuelPhase.Main1, DuelPhase.Battle) or (DuelPhase.Main1, DuelPhase.End) or
            (DuelPhase.Battle, DuelPhase.Main2) or (DuelPhase.Main2, DuelPhase.End);
        return valid ? DuelError.None : DuelError.InvalidPhaseTransition;
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
        if (target == DuelPhase.End) state = ResolveEndPhaseBanishments(state, events);
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
        if (State.NormalSummonUsed) return Reject(DuelError.NormalSummonLimitReached);
        if (!faceDown && IsSummonLocked(State, player.Id)) return Reject(DuelError.SummonLocked);
        var next = State.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != instanceId)), normalSummonUsed: true);
        return CommitSummon(next, player.Id, card, zone, faceDown, []);
    }

    private DuelActionResult SetBackRow(int instanceId, int zone)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var player = State.GetPlayer(State.ActivePlayer);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == instanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (!rules.TryGetValue(card.GameCardId, out var rule) || rule.Category == CardCategory.Unknown)
            return Reject(DuelError.UnknownCardRules);
        if (rule.Category is not (CardCategory.Spell or CardCategory.Trap)) return Reject(DuelError.NotSpellOrTrap);
        if (!rule.AllowsSpellTrapSet) return Reject(DuelError.SpellTrapSetNotAllowed);
        if (player.SpellTrapZones[zone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        var zones = player.SpellTrapFields.ToArray();
        zones[zone] = new(card, IsFaceUp: false, PlacementId: checked(State.Revision + 1), SetOnTurn: State.TurnNumber);
        return Accept(State.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != instanceId),
            spellTraps: zones)), new SpellTrapSet(player.Id, card, zone));
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
        bool flip = monster.Position == BattlePosition.FaceDownDefense;
        if (flip && IsSummonLocked(State, player.Id)) return Reject(DuelError.SummonLocked);
        var changed = monster with { Position = target, PositionChangedOnTurn = State.TurnNumber,
            SummonedOnTurn = flip ? State.TurnNumber : monster.SummonedOnTurn };
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
        if (attacker.LastAttackTurn == State.TurnNumber) return Reject(DuelError.AlreadyAttacked);
        FieldMonster? defender = targetZone is int zone ? defenderPlayer.MonsterZones[zone] : null;
        if (targetZone is not null && defender is null) return Reject(DuelError.MonsterZoneEmpty);
        if (targetZone is null && defenderPlayer.MonsterZones.Any(m => m is not null))
            return Reject(DuelError.DirectAttackBlocked);
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

        var events = new List<DuelEvent> { new AttackDeclared(attackerPlayer.Id, attackerZone, targetZone) };
        var attackZones = attackerPlayer.MonsterZones.ToArray();
        var defenseZones = defenderPlayer.MonsterZones.ToArray();
        attackZones[attackerZone] = attacker with { LastAttackTurn = State.TurnNumber };
        if (defender?.Position == BattlePosition.FaceDownDefense)
        {
            defenseZones[targetZone!.Value] = defender with { Position = BattlePosition.FaceUpDefense };
            events.Add(new MonsterRevealed(defenderPlayer.Id, targetZone.Value, defender.Card));
        }
        var calculation = CalculateBasicBattle(attack, defendingValue, defender?.Position, attackerPlayer.Id, defenderPlayer.Id);
        bool destroyAttacker = calculation.DestroyAttacker, destroyDefender = calculation.DestroyDefender;
        int damage = calculation.Damage;
        PlayerId damagedPlayer = calculation.DamagedPlayer;
        attackerPlayer = attackerPlayer.With(monsters: attackZones);
        defenderPlayer = defenderPlayer.With(monsters: defenseZones);
        DuelState next = State.With(player: attackerPlayer).With(player: defenderPlayer);
        if (damage > 0)
        {
            var damaged = next.GetPlayer(damagedPlayer);
            int remaining = Math.Max(0, damaged.LifePoints - damage);
            next = next.With(player: damaged.With(lifePoints: remaining));
            events.Add(new BattleDamageDealt(damagedPlayer, damage, damaged.LifePoints, remaining));
            // LP defeat occurs during damage calculation, before sending battle-destroyed cards to the graveyard.
            if (remaining == 0)
            {
                var outcome = new DuelOutcome(Other(damagedPlayer), damagedPlayer, DuelEndReason.LifePointsDepleted);
                events.Add(new DuelEnded(outcome));
                return Accept(next.With(outcome: outcome), events.ToArray());
            }
        }
        void Destroy(PlayerId playerId, int zone)
        {
            var player = next.GetPlayer(playerId);
            var zones = player.MonsterZones.ToArray();
            var card = zones[zone]!.Card;
            zones[zone] = null;
            next = next.With(player: player.With(monsters: zones));
            next = SendFieldCardToGraveyard(next, card, new MonsterDestroyed(playerId, zone, card), events);
        }
        if (destroyAttacker) Destroy(attackerPlayer.Id, attackerZone);
        if (destroyDefender) Destroy(defenderPlayer.Id, targetZone!.Value);
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
        state = PrepareSummonHistory(State, state, emitted);
        state = PrepareEventEffects(State, state, emitted);
        State = state.With(revision: checked(State.Revision + 1));
        TrackSummons(emitted);
        return new(true, DuelError.None, State, emitted.AsReadOnly());
    }

    private DuelActionResult Reject(DuelError error) => new(false, error, State, Array.Empty<DuelEvent>());
    private static PlayerId Other(PlayerId player) => player == PlayerId.One ? PlayerId.Two : PlayerId.One;
}
