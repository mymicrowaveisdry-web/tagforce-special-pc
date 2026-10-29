using System.Collections.Frozen;
using TagForceSpecial.Cards;

namespace TagForceSpecial.Core;

/// <summary>
/// Deterministic two-player rules subset: opening hands, draws, phases, basic normal summons,
/// sets, position changes, effect-free battles, hand-limit discards and defeat.
/// No RNG, general effects, attack replays or AI. Chains, event effects and staged battle require explicit opt-ins. Atomic battle remains the compatibility default.
/// Explicitly configured selection-only workflows pause the command loop for immutable decisions.
/// </summary>
public sealed partial class DuelEngine
{
    private readonly CardCatalog catalog;
    private readonly FrozenDictionary<uint, DuelCardRules> rules;
    private readonly FrozenDictionary<string, DecisionSequence> decisionSequences;
    private readonly FrozenDictionary<uint, DeckSpecialSummonSpell> spellEffects;
    private readonly FrozenDictionary<uint, ActivationContract> activationContracts;
    private readonly FrozenDictionary<uint, ChainEffectProfile> chainEffects;
    private readonly FrozenDictionary<uint, ChainResponseProfile> chainResponses;
    private readonly TriggerEffectProfile[] triggerEffects;
    private readonly ContinuousEffectProfile[] continuousEffects;
    private readonly ContinuousNegationProfile[] continuousNegations;
    private readonly uint[] negationOrder;
    private readonly bool experimentalBattleFlow;
    private readonly FrozenDictionary<uint, BattleResponseProfile> battleResponses;
    public DuelState State { get; private set; }

    /// <summary>
    /// Decks are supplied top-first, after any external shuffle. Only size and catalog membership
    /// are checked here; banlists, copy limits, extra decks and complete deck legality are later work.
    /// Rule metadata is explicit; missing classifications fail closed when attempting to summon.
    /// </summary>
    public DuelEngine(CardCatalog catalog, IReadOnlyDictionary<uint, DuelCardRules> rules,
        IEnumerable<uint> playerOneDeck, IEnumerable<uint> playerTwoDeck, PlayerId startingPlayer = PlayerId.One,
        IEnumerable<DecisionSequence>? decisionSequences = null,
        IEnumerable<DeckSpecialSummonSpell>? experimentalSpellEffects = null,
        IEnumerable<ActivationContract>? experimentalActivationContracts = null,
        IEnumerable<ChainEffectProfile>? experimentalChainEffects = null,
        IEnumerable<ChainResponseProfile>? experimentalChainResponses = null,
        IEnumerable<TriggerEffectProfile>? experimentalTriggerEffects = null,
        IEnumerable<ContinuousEffectProfile>? experimentalContinuousEffects = null,
        IEnumerable<ContinuousNegationProfile>? experimentalContinuousNegations = null,
        bool experimentalBattleFlow = false, IEnumerable<BattleResponseProfile>? experimentalBattleResponses = null)
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
                profile.AllowsSpellTrapSet && profile.Category is not (CardCategory.Spell or CardCategory.Trap))
                throw new ArgumentException("Invalid explicit card rules.", nameof(rules));
        this.rules = rules.ToFrozenDictionary();
        var sequences = (decisionSequences ?? []).ToArray();
        if (sequences.Any(s => s is null || s.Steps.Any(step => step.AllowedCardIds.Any(id => !knownIds.Contains(id)))))
            throw new ArgumentException("Decision sequences must reference catalog IDs.", nameof(decisionSequences));
        this.decisionSequences = sequences.ToFrozenDictionary(s => s.Id, StringComparer.Ordinal);
        var effects = (experimentalSpellEffects ?? []).ToArray();
        if (effects.Any(e => e is null || !this.rules.TryGetValue(e.SpellCardId, out var spellRule) ||
                spellRule.Category != CardCategory.Spell || this.decisionSequences.ContainsKey(e.Decisions.Id) ||
                e.EligibleMonsterIds.Any(id => !knownIds.Contains(id) || !this.rules.TryGetValue(id, out var monsterRule) ||
                    monsterRule.Category != CardCategory.Monster)) ||
            effects.Select(e => e.EffectId).Distinct(StringComparer.Ordinal).Count() != effects.Length)
            throw new ArgumentException("Effect profiles need distinct IDs, explicit Spell/Monster rules and unreserved sequence IDs.", nameof(experimentalSpellEffects));
        spellEffects = effects.ToFrozenDictionary(e => e.SpellCardId);
        var contracts = (experimentalActivationContracts ?? []).ToArray();
        if (contracts.Any(c => c is null || !this.rules.TryGetValue(c.SpellCardId, out var rule) ||
                rule.Category != CardCategory.Spell || spellEffects.ContainsKey(c.SpellCardId) ||
                effects.Any(e => e.EffectId == c.EffectId) ||
                this.decisionSequences.ContainsKey(c.Decisions.Id) || c.DiscardCardIds.Any(id => !knownIds.Contains(id))) ||
            contracts.Select(c => c.EffectId).Distinct(StringComparer.Ordinal).Count() != contracts.Length)
            throw new ArgumentException("Activation contracts need distinct IDs, explicit Spell rules and catalog cost choices.", nameof(experimentalActivationContracts));
        activationContracts = contracts.ToFrozenDictionary(c => c.SpellCardId);
        var chainProfiles = (experimentalChainEffects ?? []).ToArray();
        if (chainProfiles.Any(p => p is null || !this.rules.TryGetValue(p.CardId, out var rule) ||
                rule.Category != CardCategory.Spell ||
                p.Operation == ChainOperation.SummonFourTokens &&
                    (activationContracts.ContainsKey(p.CardId) || spellEffects.ContainsKey(p.CardId) ||
                        !rule.AllowsSpellTrapSet || p.TokenCardIds.Any(id => !knownIds.Contains(id) ||
                        !this.rules.TryGetValue(id, out var tokenRule) || tokenRule.Category != CardCategory.Monster)) ||
                p.Operation != ChainOperation.SummonFourTokens &&
                    (!activationContracts.TryGetValue(p.CardId, out var c) || c.EffectId != p.EffectId ||
                        p.Operation == ChainOperation.DestroyBoundSpellTrap && (!c.RequiresSpellTrapTarget || c.DiscardCardIds.Count != 0) ||
                        p.Operation == ChainOperation.DestroyOpposingFaceUpMonsters && (c.RequiresSpellTrapTarget || c.DiscardCardIds.Count == 0))) ||
            chainProfiles.Select(p => p.EffectId).Distinct(StringComparer.Ordinal).Count() != chainProfiles.Length)
            throw new ArgumentException("Chain profiles require matching activation contracts or explicit token rules.", nameof(experimentalChainEffects));
        chainEffects = chainProfiles.ToFrozenDictionary(p => p.CardId);
        var responses = (experimentalChainResponses ?? []).ToArray();
        if (responses.Any(p => p is null || !this.rules.TryGetValue(p.CardId, out var rule) ||
                rule.Category is not (CardCategory.Spell or CardCategory.Trap) || !rule.AllowsSpellTrapSet ||
                spellEffects.ContainsKey(p.CardId) ||
                p.DiscardCardIds.Any(id => !knownIds.Contains(id)) ||
                chainEffects.TryGetValue(p.CardId, out var existing) &&
                    (existing.Operation != p.Operation || existing.EffectId != p.EffectId) ||
                contracts.Any(c => c.EffectId == p.EffectId && c.SpellCardId != p.CardId) ||
                chainProfiles.Any(c => c.EffectId == p.EffectId && c.CardId != p.CardId) ||
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
        var reservedIds = effects.Select(p => p.EffectId).Concat(contracts.Select(p => p.EffectId))
            .Concat(chainProfiles.Select(p => p.EffectId)).Concat(responses.Select(p => p.EffectId)).ToHashSet(StringComparer.Ordinal);
        if (eventIds.Distinct(StringComparer.Ordinal).Count() != eventIds.Length || eventIds.Any(reservedIds.Contains))
            throw new ArgumentException("Event effect IDs must be distinct and unreserved.");
        this.experimentalBattleFlow = experimentalBattleFlow;
        var battleProfiles = (experimentalBattleResponses ?? []).ToArray();
        if (battleProfiles.Length != 0 && !experimentalBattleFlow || battleProfiles.Any(p => p is null ||
            !this.rules.TryGetValue(p.CardId, out var sourceRule) || sourceRule.Category is not (CardCategory.Spell or CardCategory.Trap) ||
            !sourceRule.AllowsSpellTrapSet || p.SpellSpeed == ChainSpellSpeed.Three && sourceRule.Category != CardCategory.Trap ||
            spellEffects.ContainsKey(p.CardId) || activationContracts.ContainsKey(p.CardId) || chainEffects.ContainsKey(p.CardId) || chainResponses.ContainsKey(p.CardId) ||
            p.SummonableCardIds.Any(id => !this.rules.TryGetValue(id, out var targetRule) || targetRule.Category != CardCategory.Monster)) ||
            battleProfiles.Select(p => p.EffectId).Distinct(StringComparer.Ordinal).Count() != battleProfiles.Length ||
            battleProfiles.Any(p => reservedIds.Contains(p.EffectId) || eventIds.Contains(p.EffectId)))
            throw new ArgumentException("Battle profiles need the staged flow, distinct source/effect IDs and explicit rules.", nameof(experimentalBattleResponses));
        battleResponses = battleProfiles.ToFrozenDictionary(p => p.CardId);
        int nextInstance = 1;
        DuelPlayerState CreatePlayer(PlayerId id, IEnumerable<uint> ids)
        {
            // Limit enumeration too: malformed/infinite sequences cannot allocate indefinitely.
            var deck = ids.Take(61).ToArray();
            if (deck.Length is < 40 or > 60 || deck.Any(cardId => !knownIds.Contains(cardId)))
                throw new ArgumentException("Each main deck needs 40–60 catalog IDs.");
            var instances = deck.Select(cardId => new CardInstance(nextInstance++, cardId, id)).ToArray();
            return new(id, instances.Skip(5), instances.Take(5));
        }
        State = new([CreatePlayer(PlayerId.One, playerOneDeck), CreatePlayer(PlayerId.Two, playerTwoDeck)], startingPlayer);
    }

    public DuelActionResult Execute(DuelCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (State.Outcome is not null) return Reject(DuelError.DuelFinished);
        if (command.Player is not (PlayerId.One or PlayerId.Two)) return Reject(DuelError.InvalidPlayer);
        if (State.EventEffects.PendingCommitment is not null) return AnswerTriggerCommitment(command);
        if (command is ChooseTriggerCostCard or ChooseTriggerMonsterTarget) return Reject(DuelError.NoPendingTriggerCommitment);
        if (State.EventEffects.PendingTriggers is not null)
            return command is ChooseTriggerOrder order ? OrderTriggers(order) : Reject(DuelError.TriggerOrderPending);
        if (command is ChooseTriggerOrder) return Reject(DuelError.NoPendingTriggers);
        if (State.PendingChain is not null) return ExecuteChain(command);
        if (command is PassChainResponse or ActivateChainResponse or ResolveNextChainLink) return Reject(DuelError.NoPendingChain);
        if (command is AdvanceBattle or PassBattleResponse or ChooseAttackReplay or ActivateBattleResponse)
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
        // A searched deck has no verified shuffle contract yet. Permit phase
        // progression and explicitly opted-in basic battle, never another draw.
        if (State.PendingEndPhaseBanishments.Count != 0)
            return command switch
            {
                ChangePhase { Target: DuelPhase.End } => ResolveEndPhaseBanishment(),
                ChangePhase { Target: DuelPhase.Battle or DuelPhase.Main2 } phase => Advance(phase.Target),
                DeclareAttack attack => Attack(attack.AttackerZone, attack.TargetZone),
                _ => Reject(DuelError.UnsupportedLifecycleContinuation)
            };
        return command switch
        {
            BeginDecisionSequence begin => BeginDecisions(begin.SequenceId),
            ActivateSpell activate => Activate(activate.CardInstanceId, activate.SpellTrapZone),
            ResolveDrawStep => Draw(),
            ChangePhase phase => Advance(phase.Target),
            NormalSummon summon => Summon(summon.CardInstanceId, summon.MonsterZone, false),
            SetMonster set => Summon(set.CardInstanceId, set.MonsterZone, true),
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

    private DuelActionResult Advance(DuelPhase target)
    {
        if (State.Phase == DuelPhase.Main1 && target == DuelPhase.Battle && State.TurnNumber == 1)
            return Reject(DuelError.FirstTurnBattleNotAllowed);
        bool valid = (State.Phase, target) is
            (DuelPhase.Standby, DuelPhase.Main1) or
            (DuelPhase.Main1, DuelPhase.Battle) or (DuelPhase.Main1, DuelPhase.End) or
            (DuelPhase.Battle, DuelPhase.Main2) or (DuelPhase.Main2, DuelPhase.End);
        if (!valid) return Reject(DuelError.InvalidPhaseTransition);
        if (target == DuelPhase.Battle && experimentalBattleFlow)
        {
            var battle = new BattleState(BattleStep.Start, null, NewBattleWindow(BattleTiming.Start, State.ActivePlayer));
            return Accept(State.With(phase: target).WithBattle(battle), new PhaseChanged(State.Phase, target),
                new BattleStepChanged(null, BattleStep.Start), new BattleResponseRequested(battle.Window));
        }
        return Accept(State.With(phase: target), new PhaseChanged(State.Phase, target));
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
        uint level = catalog[card.GameCardId].Statistics.Level;
        if (level == 0) return Reject(DuelError.UnsupportedMonsterLevel);
        if (level > 4) return Reject(DuelError.TributeSummonNotSupported);
        if (player.MonsterZones[zone] is not null) return Reject(DuelError.MonsterZoneOccupied);
        if (State.NormalSummonUsed) return Reject(DuelError.NormalSummonLimitReached);
        var monster = new FieldMonster(card, faceDown ? BattlePosition.FaceDownDefense : BattlePosition.FaceUpAttack,
            SummonedOnTurn: State.TurnNumber, PlacementId: checked(State.Revision + 1));
        var monsters = player.MonsterZones.ToArray();
        monsters[zone] = monster;
        var next = player.With(hand: player.Hand.Where(c => c.InstanceId != instanceId), monsters: monsters);
        return Accept(State.With(player: next, normalSummonUsed: true), faceDown
            ? new MonsterSet(player.Id, monster, zone) : new MonsterNormalSummoned(player.Id, monster, zone));
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
        // This is still an atomic battle; response/damage-step windows remain Stage 8 work.
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
            next = next.With(player: player.With(monsters: zones, graveyard: player.Graveyard.Append(card)));
            events.Add(new MonsterDestroyed(playerId, zone, card));
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
        state = PrepareEventEffects(State, state, emitted);
        State = state.With(revision: checked(State.Revision + 1));
        return new(true, DuelError.None, State, emitted.AsReadOnly());
    }

    private DuelActionResult Reject(DuelError error) => new(false, error, State, Array.Empty<DuelEvent>());
    private static PlayerId Other(PlayerId player) => player == PlayerId.One ? PlayerId.Two : PlayerId.One;
}
