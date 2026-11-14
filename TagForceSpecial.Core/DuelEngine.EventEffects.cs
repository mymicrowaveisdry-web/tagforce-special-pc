namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private static bool Relates(EffectRelation relation, MonsterBinding source, MonsterBinding subject) => relation switch
    {
        EffectRelation.Self => source.Card == subject.Card && source.PlacementId == subject.PlacementId,
        EffectRelation.Controller => source.Player == subject.Player,
        EffectRelation.Opponent => source.Player != subject.Player,
        EffectRelation.Any => true,
        _ => false
    };

    private static IEnumerable<(MonsterBinding Binding, FieldMonster Monster)> FieldMonsters(DuelState state)
    {
        foreach (var player in state.Players)
            for (int zone = 0; zone < player.MonsterZones.Count; zone++)
                if (player.MonsterZones[zone] is { } monster)
                    yield return (new(player.Id, zone, monster.Card, monster.PlacementId), monster);
    }

    private bool CanActivateTrigger(DuelState state, TriggerActivation trigger)
    {
        if (rules[trigger.Source.Card.GameCardId].Category == CardCategory.Monster && trigger.Profile.SourceLocation != TriggerSourceLocation.PendulumZone &&
            RuleLocked(state, LockedAction.ActivateMonsterEffect, trigger.Player, trigger.Source.Card, kind: EffectKind.MonsterTrigger)) return false;
        bool location = trigger.Profile.SourceLocation == TriggerSourceLocation.Hand ? state.GetPlayer(trigger.Player).Hand.Contains(trigger.Source.Card) :
            trigger.Profile.SourceLocation == TriggerSourceLocation.FaceUpSpellTrap ? SpellTrapSourcePresent(state, trigger.Source) :
            trigger.Profile.SourceLocation == TriggerSourceLocation.PendulumZone ? ScaleSourcePresent(state, trigger.Source) :
            trigger.Profile.SourceLocation != TriggerSourceLocation.FaceUpMonster
            ? state.GetPlayer(trigger.Player).Graveyard.Contains(trigger.Source.Card) ||
                trigger.Profile.SourceLocation == TriggerSourceLocation.DestroyedSelf && state.GetPlayer(trigger.Player).FaceUpExtraDeck.Contains(trigger.Source.Card) ||
                trigger.Profile.EventKind == MonsterEventKind.Tributed && state.GetPlayer(trigger.Player).FaceUpExtraDeck.Contains(trigger.Source.Card)
            : trigger.Source.IsStillPresent(state) && state.GetPlayer(trigger.Player).MonsterZones[trigger.Source.Zone]!.Position != BattlePosition.FaceDownDefense;
        if (!location || IsTriggerUsed(state, trigger)) return false;
        if (state.Battle?.Step == BattleStep.Damage && !(trigger.Profile.DamageStepAllowed ??
            (trigger.Profile.Requirement == TriggerRequirement.Mandatory || trigger.Profile.SourceLocation != TriggerSourceLocation.FaceUpMonster ||
             trigger.Profile.EventKind == MonsterEventKind.FlippedFaceUp ||
             trigger.Profile.EventKind == MonsterEventKind.SpecialSummoned && trigger.Profile.Relation == EffectRelation.Self))) return false;
        if (trigger.Profile.SourceLocation == TriggerSourceLocation.FaceUpMonster &&
            state.GetPlayer(trigger.Player).MonsterZones[trigger.Source.Zone]!.BattleDestroyed &&
            trigger.Profile.EventKind != MonsterEventKind.FlippedFaceUp) return false;
        if (trigger.Profile.DetachMaterialCount > 0 && trigger.DetachedCost.Count == 0 &&
            state.GetPlayer(trigger.Player).MonsterZones[trigger.Source.Zone]!.XyzMaterials.Count < trigger.Profile.DetachMaterialCount) return false;
        if (trigger.Profile is { Requirement: TriggerRequirement.Optional, Definition: { } definition } && !DefinitionTriggerAvailable(state, trigger, definition)) return false;
        if (trigger is { TargetCard: null, Profile.Definition.FieldTargets: [ZoneTarget zone] } && !ZoneMatches(state, trigger.Player, zone).Any()) return false;
        if (trigger is { TargetCard: null, Profile.Definition.FieldTargets: [FieldTarget field] } && !TriggerFieldTargets(state, trigger, field).Any()) return false;
        // A leading hand check is also the trigger's activation condition.
        if (trigger.Profile.Definition?.Steps is [StopUnlessHandHoldsStep holds, ..] && !HandHolds(state, trigger.Player, holds)) return false;
        if (trigger.Profile.Definition?.Steps is [OpponentCallsRandomHandCardStep, ..] && state.GetPlayer(trigger.Player).Hand.Count == 0) return false;
        if (trigger.Profile.Definition?.Steps is [StopUnlessSpecialSummonedStep, ..] && !WasSpecialSummoned(state, trigger.Source)) return false;
        if (trigger.Profile.Definition?.Steps is [StopUnlessOwnTurnStep, ..] && state.ActivePlayer != trigger.Player) return false;
        if (trigger.Profile.Definition?.Steps is [StopUnlessActivatedThisTurnStep, ..] &&
            !ScaleActivatedThisTurn(state, trigger.Player, trigger.Source.Zone, trigger.Source.Card, trigger.Source.PlacementId)) return false;
        if (trigger.Profile.TributeCost && trigger.TributedCost is null &&
            !FieldMonsters(state).Any(e => e.Binding.Player == trigger.Player && !e.Monster.BattleDestroyed && !RuleLocked(state, LockedAction.BeTributed, e.Binding.Player, e.Monster.Card, e.Monster))) return false;
        if (trigger.DiscardedCost is not null || trigger.PaidLifePoints > 0 || trigger.DetachedCost.Count > 0) return true;
        var player = state.GetPlayer(trigger.Player);
        return player.LifePoints > trigger.Profile.LifePointCost &&
            (trigger.Profile.DiscardCardIds.Count == 0 || player.Hand.Any(c => trigger.Profile.DiscardCardIds.Contains(c.GameCardId))) &&
            (trigger.Profile.Target is null || TriggerTargets(state, trigger).Any());
    }

    private IEnumerable<MonsterEventOccurrence> Occurrences(DuelState before, DuelEvent emitted)
    {
        MonsterBinding? subject = emitted switch
        {
            MonsterNormalSummoned e => new(e.Player, e.Zone, e.Monster.Card, e.Monster.PlacementId),
            MonsterSpecialSummoned e => new(e.Player, e.Zone, e.Monster.Card, e.Monster.PlacementId),
            TokenSpecialSummoned e => new(e.Player, e.Zone, e.Monster.Card, e.Monster.PlacementId),
            MonsterFlipSummoned e => new(e.Player, e.Zone, e.Monster.Card, e.Monster.PlacementId),
            BattleFlipEffectWindowOpened e => e.Monster,
            MonsterFlippedByEffect e => e.Monster,
            MonsterDestroyed e when before.GetPlayer(e.Player).MonsterZones[e.Zone] is { } m => new(e.Player, e.Zone, e.Card, m.PlacementId),
            MonsterTributed e => e.Monster,
            SummonMaterialSentToGraveyard e => e.Material,
            FusionMaterialSentToGraveyard e => e.Material.Field ?? new(e.Material.Card.Owner, -1, e.Material.Card, 0),
            RitualMaterialTributed e => e.Material.Field ?? new(e.Material.Card.Owner, -1, e.Material.Card, 0),
            XyzMaterialSentToGraveyard e => new(e.Material.Owner, -1, e.Material, 0),
            SummonNegated e => new(e.Attempt.Card.Owner, -1, e.Attempt.Card, 0),
            CardDiscarded e => new(e.Player, -1, e.Card, 0),
            MonsterSentToGraveyardByRule e => new(e.Player, e.Zone, e.Card, e.PlacementId),
            ActivationCostPaid e => new(e.Player, -1, e.Card, 0),
            TriggerCostPaid { DiscardedCard: { } card } e => new(e.Trigger.Player, -1, card, 0),
            // A negated activation or a Pendulum scale is not a Spell/Trap sent from the field.
            SpellTrapDestroyed { IsPendulumZone: false, ActivationNegated: false } e => new(e.Player, e.Zone, e.Card, 0),
            SpellSentToGraveyard { IsPendulumZone: false, ActivationNegated: false } e => new(e.Player, e.Zone, e.Card, 0),
            ChainTrapSentToGraveyard { ActivationNegated: false } e => new(e.Player, e.Zone, e.Card, 0),
            FieldSpellReplaced e => new(e.Player, DuelPlayerState.FieldZoneIndex, e.Card, 0),
            _ => null
        };
        bool spellTrapLeaving = emitted is SpellTrapDestroyed or SpellSentToGraveyard or ChainTrapSentToGraveyard or FieldSpellReplaced;
        if (subject is null || !rules.TryGetValue(subject.Card.GameCardId, out var rule) || !spellTrapLeaving && rule.Category != CardCategory.Monster) yield break;
        bool cost = emitted is ActivationCostPaid or TriggerCostPaid or XyzMaterialSentToGraveyard { IsCost: true };
        var point = new EffectTimingPoint(checked(State.Revision + 1), cost ? -1 : emitted.TimingGroup ?? 0);
        var kind = emitted switch
        {
            MonsterNormalSummoned => MonsterEventKind.NormalSummoned,
            MonsterSpecialSummoned or TokenSpecialSummoned => MonsterEventKind.SpecialSummoned,
            MonsterFlipSummoned => MonsterEventKind.FlipSummoned,
            BattleFlipEffectWindowOpened or MonsterFlippedByEffect => MonsterEventKind.FlippedFaceUp,
            MonsterDestroyed or SummonNegated or SpellTrapDestroyed => MonsterEventKind.Destroyed,
            _ => MonsterEventKind.SentToGraveyard
        };
        // Neither the face-up Extra Deck nor a removed Token is "sent to the Graveyard"; destruction still counts.
        bool notToGraveyard = emitted.FieldDestination is FieldCardDestination.FaceUpExtraDeck or FieldCardDestination.Removed or FieldCardDestination.Banished or FieldCardDestination.Deck;
        // A Tributed Pendulum Monster goes to the face-up Extra Deck; its "If this card is Tributed" effect still activates there.
        if (emitted is MonsterTributed && emitted.FieldDestination is not (FieldCardDestination.Removed or FieldCardDestination.Banished or FieldCardDestination.Deck))
            yield return new(MonsterEventKind.Tributed, subject, point, cost);
        if (kind == MonsterEventKind.SentToGraveyard && notToGraveyard) yield break;
        bool byBattle = emitted is MonsterDestroyed { ByBattle: true };
        yield return new(kind, subject, point, cost, byBattle) { FromSpellTrapZone = spellTrapLeaving, ProcedureId = (emitted as MonsterSpecialSummoned)?.ProcedureId, DiscardEffectController = (emitted as CardDiscarded)?.EffectController };
        if (kind == MonsterEventKind.Destroyed && !notToGraveyard)
            yield return new(MonsterEventKind.SentToGraveyard, subject, point, ByBattle: byBattle) { FromSpellTrapZone = spellTrapLeaving };
        if (emitted is MonsterDestroyed { DestroyedBy: { } destroyer } && !notToGraveyard)
            yield return new(MonsterEventKind.DestroyedOpponentByBattle, destroyer, point, ByBattle: true);
        if (emitted is MonsterNormalSummoned summoned)
        {
            if (summoned.Tributes.Count != 0) yield return new(MonsterEventKind.TributeSummoned, subject, point);
            foreach (var tribute in summoned.Tributes) yield return new(MonsterEventKind.TributedForSummon, tribute, point);
        }
        if (kind == MonsterEventKind.FlipSummoned) yield return new(MonsterEventKind.FlippedFaceUp, subject, point);
    }

    /// <summary>
    /// An attack declaration, with the attacker as subject; battle damage, with the monster that inflicted it; a change of control, with
    /// the monster in its new place; and on entering the Standby Phase, one per face-up Spell or Trap.
    /// </summary>
    private IEnumerable<MonsterEventOccurrence> AttackAndStandbyOccurrences(DuelState before, DuelState after, DuelEvent emitted)
    {
        var point = new EffectTimingPoint(checked(State.Revision + 1), emitted.TimingGroup ?? 0);
        if (emitted is AttackDeclared declared && before.GetPlayer(declared.Player).MonsterZones[declared.AttackerZone] is { } attacker)
            yield return new(MonsterEventKind.AttackDeclared, new(declared.Player, declared.AttackerZone, attacker.Card, attacker.PlacementId), point) { DirectAttack = declared.TargetZone is null };
        // The monster on the other side of the battle from the damaged player inflicted the damage.
        if (emitted is BattleDamageDealt { Damage: > 0 } damage && before.Battle?.Attack is { } attack &&
            (damage.Player != attack.Attacker.Player ? attack.Attacker : attack.Defender) is { } dealer && dealer.Player != damage.Player)
            yield return new(MonsterEventKind.InflictedBattleDamage, dealer, point);
        if (emitted is ControlChanged moved && after.GetPlayer(moved.To).MonsterZones[moved.ToZone] is { } taken && taken.Card == moved.Card)
            yield return new(MonsterEventKind.ControlChanged, new(moved.To, moved.ToZone, taken.Card, taken.PlacementId), point);
        if (emitted is PhaseChanged { To: DuelPhase.End })
            foreach (var (binding, monster) in FieldMonsters(after))
                if (monster.Position != BattlePosition.FaceDownDefense) yield return new(MonsterEventKind.EndPhase, binding, point);
        // "During your Standby Phase": one occurrence per face-up monster of the turn player.
        if (emitted is PhaseChanged { To: DuelPhase.Standby })
            foreach (var (binding, monster) in FieldMonsters(after))
                if (binding.Player == after.ActivePlayer && monster.Position != BattlePosition.FaceDownDefense)
                    yield return new(MonsterEventKind.StandbyPhase, binding, point);
        if (emitted is PhaseChanged { To: DuelPhase.Standby or DuelPhase.End } phase)
            foreach (var (_, _, field) in ActiveCards(after).Concat(ActiveScales(after)))
                yield return new(phase.To == DuelPhase.Standby ? MonsterEventKind.StandbyPhase : MonsterEventKind.EndPhase,
                    new(after.ActivePlayer, -1, field.Card, field.PlacementId), point);
    }

    /// <summary>On entering the End Phase, one occurrence per face-up monster Summoned or flipped face-up this turn.</summary>
    private IEnumerable<MonsterEventOccurrence> EndPhaseOccurrences(DuelState after, DuelEvent emitted)
    {
        if (emitted is not PhaseChanged { To: DuelPhase.End }) yield break;
        var point = new EffectTimingPoint(checked(State.Revision + 1), 0);
        foreach (var (binding, monster) in FieldMonsters(after))
            if (monster.Position != BattlePosition.FaceDownDefense && (monster.SummonedOnTurn == after.TurnNumber || monster.FlippedOnTurn == after.TurnNumber))
                yield return new(MonsterEventKind.EndPhaseAfterSummonOrFlip, binding, point);
    }

    private static bool CauseMatches(TriggerEffectProfile profile, MonsterEventOccurrence occurrence) => profile.Cause switch
    {
        TriggerEventCause.FromField => occurrence.Subject.Zone >= 0,
        TriggerEventCause.FromOwnField => occurrence.Subject.Zone >= 0 && occurrence.Subject.Player == occurrence.Subject.Card.Owner,
        TriggerEventCause.ByBattle => occurrence.ByBattle,
        TriggerEventCause.ByDiscardEffect => occurrence.DiscardEffectController is not null,
        TriggerEventCause.ByOpponentDiscardEffect => occurrence.DiscardEffectController is { } controller && controller != occurrence.Subject.Card.Owner,
        _ => true
    };

    /// <summary>An optional definition trigger needs its Deck operations to have something to act on.</summary>
    private bool DefinitionTriggerAvailable(DuelState state, TriggerActivation trigger, EffectDefinition definition) =>
        definition.Steps.All(step => step switch
        {
            SpecialSummonStep summon => SpecialSummonOptions(state, trigger.Player, summon, trigger.Source.Card).Any(),
            PayDownToGainStep pay => state.GetPlayer(trigger.Player).LifePoints > pay.Remaining,
            AddFromFaceUpExtraDeckStep extra => state.GetPlayer(trigger.Player).FaceUpExtraDeck.Any(c => MatchesFilter(c, extra.Filter)),
            AddFromGraveyardToHandStep add => state.GetPlayer(trigger.Player).Graveyard.Any(c => MatchesFilter(c, add.Filter, inGraveyard: true)),
            AddFromDeckOrGraveyardStep either => DeckMatches(state, trigger.Player, either.Filter).Any() ||
                state.GetPlayer(trigger.Player).Graveyard.Any(c => MatchesFilter(c, either.Filter, inGraveyard: true)),
            SpecialSummonSelfStep self => trigger.Profile.SourceLocation != TriggerSourceLocation.Hand || Enumerable.Range(0, 5).Any(zone =>
                CheckSpecialSummon(state, trigger.Player, trigger.Source.Card, zone, self.Position ?? BattlePosition.FaceUpAttack, DecisionCardSource.Hand) == DuelError.None),
            AddFromDeckToHandStep search => DeckMatches(state, trigger.Player, search.Filter).Any(),
            SendFromDeckToGraveyardStep send => DeckMatches(state, trigger.Player, send.Filter).Any(),
            _ => true
        });

    private IEnumerable<TriggerActivation> CollectTriggers(DuelState before, DuelState after, IReadOnlyList<DuelEvent> events, int firstId)
    {
        // "When Normal Summoned, (other) cards and effects cannot be activated": only that monster's own effects trigger at that time.
        var unanswerable = events.OfType<MonsterNormalSummoned>().Where(e => unanswerableSummons.Contains(e.Monster.Card.GameCardId)).Select(e => e.Monster.Card).ToHashSet();
        int id = firstId;
        foreach (var trigger in CollectAllTriggers(before, after, events))
            if (unanswerable.Count == 0 || unanswerable.Contains(trigger.Source.Card))
                yield return trigger with { Id = id++ };
    }

    private IEnumerable<TriggerActivation> CollectAllTriggers(DuelState before, DuelState after, IReadOnlyList<DuelEvent> events)
    {
        int id = 0;
        var destructionSources = FieldMonsters(before).Select(entry =>
            events.OfType<MonsterRevealed>().Any(e => e.Card == entry.Monster.Card)
                ? (entry.Binding, entry.Monster with { Position = BattlePosition.FaceUpDefense }) : entry).ToArray();
        foreach (var emitted in events)
            foreach (var occurrence in Occurrences(before, emitted).Concat(EndPhaseOccurrences(after, emitted)).Concat(AttackAndStandbyOccurrences(before, after, emitted)))
            {
                var sources = emitted is MonsterDestroyed or MonsterTributed or SummonMaterialSentToGraveyard or
                    FusionMaterialSentToGraveyard or RitualMaterialTributed or XyzMaterialSentToGraveyard { IsCost: false }
                    ? destructionSources : FieldMonsters(after);
                foreach (var profile in triggerEffects.Where(p => p.EventKind == occurrence.Kind && CauseMatches(p, occurrence)).OrderBy(p => p.EffectId, StringComparer.Ordinal))
                {
                    if (occurrence.FromSpellTrapZone && profile.SourceLocation is TriggerSourceLocation.FaceUpMonster or TriggerSourceLocation.FaceUpSpellTrap or TriggerSourceLocation.PendulumZone) continue;
                    if (profile.RequiredProcedureId is { } procedure && occurrence.ProcedureId != procedure) continue;
                    if (profile.DirectAttackOnly && !occurrence.DirectAttack ||
                        profile.SourceLocation != TriggerSourceLocation.FaceUpSpellTrap && !SubjectMatches(profile, occurrence.Subject.Card, null)) continue;
                    if (profile.SourceLocation == TriggerSourceLocation.Hand)
                    {
                        foreach (var holder in after.Players)
                            foreach (var held in holder.Hand.Where(c => c.GameCardId == profile.CardId))
                            {
                                MonsterBinding source = new(holder.Id, -1, held, 0);
                                if (Relates(profile.Relation, source, occurrence.Subject)) yield return new(id++, profile, source, occurrence);
                            }
                        continue;
                    }
                    if (profile.SourceLocation is TriggerSourceLocation.FaceUpSpellTrap or TriggerSourceLocation.PendulumZone)
                    {
                        var faceUp = profile.SourceLocation == TriggerSourceLocation.PendulumZone ? ActiveScales(after) : ActiveCards(after);
                        foreach (var (owner, zone, field) in faceUp.Where(e => e.Field.Card.GameCardId == profile.CardId))
                        {
                            MonsterBinding source = new(owner, zone, field.Card, field.PlacementId);
                            if (Relates(profile.Relation, source, occurrence.Subject) && SubjectMatches(profile, occurrence.Subject.Card, field) &&
                                (occurrence.Kind is not (MonsterEventKind.StandbyPhase or MonsterEventKind.EndPhase) || occurrence.Subject.Card == field.Card))
                                yield return new(id++, profile, source, occurrence);
                        }
                        continue;
                    }
                    if (profile.SourceLocation != TriggerSourceLocation.FaceUpMonster)
                    {
                        // A definition effect in the Graveyard belongs to the card's owner, who may not have controlled it.
                        if (profile.CardId == occurrence.Subject.Card.GameCardId)
                            yield return new(id++, profile, profile.Definition is null ? occurrence.Subject : occurrence.Subject with { Player = occurrence.Subject.Card.Owner }, occurrence);
                        continue;
                    }
                    foreach (var (binding, monster) in sources)
                        if (monster.Position != BattlePosition.FaceDownDefense && monster.Card.GameCardId == profile.CardId && (!profile.RequiresGeminiActive || monster.GeminiActive) &&
                            Relates(profile.Relation, binding, occurrence.Subject))
                            yield return new(id++, profile, binding, occurrence);
                }
            }
    }

    private EffectTimingPoint? LastTimingPoint(DuelState before, IReadOnlyList<DuelEvent> events)
    {
        var point = before.EventEffects.LastTimingPoint;
        // A negated activation does not resolve; an effect-negated/empty link still has a resolution boundary.
        if (events.OfType<ChainLinkResolved>().Any() && !events.OfType<ChainLinkSkipped>().Any(e => e.Reason == ChainSkipReason.ActivationNegated))
            point = new(checked(State.Revision + 1), 0);
        foreach (var emitted in events)
            if (emitted is MonsterNormalSummoned or MonsterSpecialSummoned or TokenSpecialSummoned or MonsterFlipSummoned or
                MonsterDestroyed or MonsterTributed or SummonMaterialSentToGraveyard or SummonMaterialSentToFaceUpExtraDeck or FusionMaterialSentToGraveyard or FusionMaterialSentToFaceUpExtraDeck or RitualMaterialTributed or CardSentToFaceUpExtraDeck or PendulumScaleActivated or SummonMaterialBanished or XyzMaterialAttached or XyzMaterialSentToGraveyard or SummonAttempted or SummonNegated or CardDiscarded or ActivationCostPaid or TriggerCostPaid or MonsterRevealed or SpellTrapDestroyed or
                MonsterBanished or LifePointsGained or BattleDamageDealt or EffectDamageDealt or MonsterModifierCreated or MonsterEffectsNegated or
                SpellActivated or ChainResponseActivated or MonsterPositionChanged or MonsterSet or SpellTrapSet or CardDrawn or CardSearched or CardSentFromDeckToGraveyard or CardReturnedToHand or LifePointsPaid or CardMovedToHand or CardBanishedFromGraveyard or CardReturnedToGraveyard or CardShuffledIntoDeck or
                PhaseChanged or TurnStarted or AttackDeclared or BattleFlipEffectWindowOpened or ControlChanged)
                point = new(checked(State.Revision + 1), emitted.TimingGroup ?? 0);
        return point;
    }

    private static bool HasTriggerTiming(TriggerActivation trigger, EffectTimingPoint? last) =>
        trigger.Profile.Requirement != TriggerRequirement.Optional || trigger.Profile.Timing != TriggerTiming.When ||
        !trigger.Occurrence.IsActivationCost && trigger.Occurrence.TimingPoint == last;

    private DuelState PrepareEventEffects(DuelState before, DuelState next, List<DuelEvent> events)
    {
        if (triggerEffects.Length == 0 && continuousEffects.Length == 0 && continuousNegations.Length == 0 && !HasTimedCombatModifiers && !HasLastingModifiers && !HasMonsterNegators &&
            !HasEffectLocks && statProfiles.Count == 0) return next;
        var current = next.EventEffects;
        var timed = current.TimedModifiers.Where(m => m.Target.IsStillPresent(next) &&
            next.GetPlayer(m.Target.Player).MonsterZones[m.Target.Zone]!.Position != BattlePosition.FaceDownDefense &&
            (m.ExpiresOnTurn is null || m.ExpiresOnTurn == next.TurnNumber && next.Phase != DuelPhase.End) &&
            (m.ExpiresAfterAttackId is null || m.ExpiresAfterAttackId == next.Battle?.Attack?.Id)).ToArray();
        var negations = current.TimedNegations.Where(n => n.ExpiresOnTurn == next.TurnNumber && next.Phase != DuelPhase.End &&
            n.Target.IsStillPresent(next) && next.GetPlayer(n.Target.Player).MonsterZones[n.Target.Zone]!.Position != BattlePosition.FaceDownDefense).ToArray();
        foreach (var ended in before.EventEffects.TimedNegations.Except(negations)) events.Add(new MonsterEffectNegationEnded(ended));
        var negated = CalculateNegatedMonsters(next, negations);
        var applied = CalculateModifiers(next, timed, negated);
        foreach (var old in before.EventEffects.AppliedModifiers.Except(applied))
            events.Add(new StatModifierRemoved(old, old.ExpiresOnTurn is not null &&
                (next.Phase == DuelPhase.End || old.ExpiresOnTurn != next.TurnNumber)
                    ? ModifierRemovalReason.EndPhaseReached : old.ExpiresAfterAttackId is not null && old.ExpiresAfterAttackId != next.Battle?.Attack?.Id
                        ? ModifierRemovalReason.DamageStepEnded : ModifierRemovalReason.ConditionNoLongerMet));
        foreach (var added in applied.Except(before.EventEffects.AppliedModifiers)) events.Add(new StatModifierApplied(added));
        var statistics = FieldMonsters(next).Select(entry => CalculateStatistics(next, entry.Binding, applied)).ToArray();
        var last = LastTimingPoint(before, events);
        var deferred = current.DeferredTriggers.ToList();
        if (next.Outcome is null) deferred.AddRange(CollectTriggers(before, next, events.ToArray(), deferred.Count + 1));
        else deferred.Clear();
        var pending = current.PendingTriggers;
        if (next.Outcome is null && next.PendingChain is null && next.PendingSummon is null && next.PendingDecision is null && pending is null && deferred.Count != 0)
        {
            var timedEligible = deferred.Where(t => HasTriggerTiming(t, last)).ToArray();
            foreach (var missed in deferred.Except(timedEligible)) events.Add(new TriggerMissedTiming(missed));
            var eligible = timedEligible.Where(t => CanActivateTrigger(next, t)).ToArray();
            foreach (var unavailable in timedEligible.Except(eligible)) events.Add(new TriggerUnavailable(unavailable));
            deferred.Clear();
            if (eligible.Length != 0)
            {
                pending = new(checked(State.Revision + 1), next.ActivePlayer, eligible);
                events.Add(new TriggerOrderRequested(pending));
            }
        }
        return next.WithEventEffects(current.WithTriggers(pending, current.PendingCommitment).WithDeferred(deferred, last)
            .WithModifiers(timed, applied, statistics).WithNegations(negations, negated)
            .WithUses(current.UsedTriggers.Where(u => u.Turn == next.TurnNumber || u.Turn == 0)));
    }

    private DuelState ResolveTrigger(DuelState next, TriggerActivation trigger, List<DuelEvent> events)
    {
        bool previousSucceeded = true;
        int actionOrdinal = 0;
        for (int index = 0; index < trigger.Profile.Steps.Count; index++)
        {
            var step = trigger.Profile.Steps[index];
            if (step.RequiresPreviousStepSuccess && !previousSucceeded) continue;
            int start = events.Count;
            bool succeeded = true, previousActionSucceeded = true;
            foreach (var action in step.Actions)
            {
                if (action.RequiresPreviousActionSuccess && !previousActionSucceeded) { succeeded = false; continue; }
                var beforeAction = next;
                next = ResolveTriggerAction(next, trigger, action, actionOrdinal++, events);
                previousActionSucceeded = !ReferenceEquals(beforeAction, next);
                succeeded &= previousActionSucceeded;
            }
            previousSucceeded = succeeded;
            for (int i = start; i < events.Count; i++) events[i] = events[i] with { TimingGroup = index };
        }
        return next;
    }

    private DuelState ResolveTriggerAction(DuelState next, TriggerActivation trigger, TriggerEffectAction action, int actionOrdinal, List<DuelEvent> events)
    {
        var profile = trigger.Profile;
        if (action.Operation == TriggerOperation.GainLifePoints)
        {
            return GainLifePoints(next, trigger.Player, action.LifePointGain, profile.EffectId, events);
        }
        var subject = action.Operation == TriggerOperation.ModifyEventMonsterUntilEndPhase ? trigger.Occurrence.Subject : trigger.Target!;
        if (!subject.IsStillPresent(next) ||
            (action.Operation != TriggerOperation.DestroyTargetMonster || profile.Target!.RequireFaceUpAtResolution) &&
                next.GetPlayer(subject.Player).MonsterZones[subject.Zone]!.Position == BattlePosition.FaceDownDefense)
        {
            events.Add(new TriggerSubjectUnavailable(trigger));
            return next;
        }
        if (action.Operation == TriggerOperation.DestroyTargetMonster)
        {
            return DestroyFieldCard(next, new(subject.Player, FieldLocation.MonsterZone, subject.Zone, subject.Card, subject.PlacementId), events);
        }
        var effects = next.EventEffects;
        if (action.Operation == TriggerOperation.NegateTargetMonsterUntilEndPhase)
        {
            var negation = new MonsterEffectNegation(profile.EffectId, trigger.Source, subject, checked(State.Revision + 1), State.TurnNumber, actionOrdinal);
            events.Add(new MonsterEffectsNegated(negation));
            return next.WithEventEffects(effects.WithNegations(effects.TimedNegations.Append(negation), effects.NegatedMonsters));
        }
        var modifier = new StatModifier(profile.EffectId, trigger.Source, subject, action.AttackDelta,
            action.DefenseDelta, checked(State.Revision + 1), State.TurnNumber, actionOrdinal);
        events.Add(new MonsterModifierCreated(modifier));
        return next.WithEventEffects(effects.WithModifiers(effects.TimedModifiers.Append(modifier), effects.AppliedModifiers, effects.Statistics));
    }

    private StatModifier[] CalculateModifiers(DuelState state, IEnumerable<StatModifier> timed, IReadOnlyList<MonsterBinding>? negated = null)
    {
        var monsters = FieldMonsters(state).Where(e => e.Monster.Position != BattlePosition.FaceDownDefense && !e.Monster.BattleDestroyed).ToArray();
        negated ??= CalculateNegatedMonsters(state, state.EventEffects.TimedNegations);
        var result = timed.ToList();
        foreach (var (source, monster) in monsters)
            foreach (var profile in continuousEffects.Where(p => p.CardId == monster.Card.GameCardId && !negated.Contains(source)).OrderBy(p => p.EffectId, StringComparer.Ordinal))
                foreach (var (target, _) in monsters)
                    if (Relates(profile.Affects, source, target) && (profile.Filter is null || MatchesFilter(target.Card, profile.Filter)))
                        result.Add(new(profile.EffectId, source, target, profile.AttackDelta, profile.DefenseDelta));
        if (HasLastingModifiers)
        {
            AddLastingModifiers(state, monsters, negated, result);
            AddPendulumModifiers(state, monsters, result);
        }
        return result.ToArray();
    }


    private EffectiveMonsterStatistics CalculateStatistics(DuelState state, MonsterBinding monster, IEnumerable<StatModifier> modifiers)
    {
        var printed = BaseStatistics(state, monster);
        var applied = modifiers.Where(m => m.Target == monster).ToArray();
        int? Value(int? value, Func<StatModifier, int> delta) => value is null ? null
            : (int)Math.Clamp((long)value.Value + applied.Sum(m => (long)delta(m)), 0, int.MaxValue);
        return new(monster, Value(printed.Attack, m => m.AttackDelta), Value(printed.Defense, m => m.DefenseDelta));
    }

    /// <summary>Effective stats for the current placement, including registered auras and resolved modifiers.</summary>
    public EffectiveMonsterStatistics? GetMonsterStatistics(PlayerId player, int zone)
    {
        if (player is not (PlayerId.One or PlayerId.Two)) throw new ArgumentOutOfRangeException(nameof(player));
        if (zone is < 0 or >= 5) throw new ArgumentOutOfRangeException(nameof(zone));
        return GetMonsterStatistics(State, player, zone);
    }

    private EffectiveMonsterStatistics? GetMonsterStatistics(DuelState state, PlayerId player, int zone)
    {
        var monster = state.GetPlayer(player).MonsterZones[zone];
        if (monster is null) return null;
        var modifiers = CalculateModifiers(state, state.EventEffects.TimedModifiers);
        return CalculateStatistics(state, new(player, zone, monster.Card, monster.PlacementId), modifiers);
    }
}
