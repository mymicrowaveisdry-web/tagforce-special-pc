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

    private static bool CanActivateTrigger(DuelState state, TriggerActivation trigger) =>
        trigger.Profile.SourceLocation == TriggerSourceLocation.DestroyedSelf
            ? state.GetPlayer(trigger.Player).Graveyard.Contains(trigger.Source.Card)
            : trigger.Source.IsStillPresent(state) &&
                state.GetPlayer(trigger.Player).MonsterZones[trigger.Source.Zone]!.Position != BattlePosition.FaceDownDefense;

    private IEnumerable<TriggerActivation> CollectTriggers(DuelState before, DuelState after, IReadOnlyList<DuelEvent> events,
        int firstId)
    {
        int id = firstId;
        // Destruction in one atomic transition is one simultaneous event batch.
        // Battle reveals precede destruction; a revealed source is face-up at that event.
        var destructionSources = FieldMonsters(before).Select(entry =>
            events.OfType<MonsterRevealed>().Any(e => e.Card == entry.Monster.Card)
                ? (entry.Binding, entry.Monster with { Position = BattlePosition.FaceUpDefense }) : entry).ToArray();
        foreach (var emitted in events)
        {
            MonsterEventOccurrence? occurrence = emitted switch
            {
                MonsterNormalSummoned e => new(MonsterEventKind.NormalSummoned,
                    new(e.Player, e.Zone, e.Monster.Card, e.Monster.PlacementId)),
                MonsterSpecialSummoned e => new(MonsterEventKind.SpecialSummoned,
                    new(e.Player, e.Zone, e.Monster.Card, e.Monster.PlacementId)),
                TokenSpecialSummoned e => new(MonsterEventKind.SpecialSummoned,
                    new(e.Player, e.Zone, e.Monster.Card, e.Monster.PlacementId)),
                MonsterFlipSummoned e => new(MonsterEventKind.FlipSummoned,
                    new(e.Player, e.Zone, e.Monster.Card, e.Monster.PlacementId)),
                MonsterDestroyed e when before.GetPlayer(e.Player).MonsterZones[e.Zone] is { } monster =>
                    new(MonsterEventKind.Destroyed, new(e.Player, e.Zone, e.Card, monster.PlacementId)),
                _ => null
            };
            if (occurrence is null) continue;
            var sources = occurrence.Kind == MonsterEventKind.Destroyed ? destructionSources : FieldMonsters(after);
            foreach (var profile in triggerEffects.Where(p => p.EventKind == occurrence.Kind).OrderBy(p => p.EffectId, StringComparer.Ordinal))
            {
                if (profile.SourceLocation == TriggerSourceLocation.DestroyedSelf)
                {
                    if (profile.CardId == occurrence.Subject.Card.GameCardId)
                        yield return new(id++, profile, occurrence.Subject, occurrence);
                    continue;
                }
                foreach (var (binding, monster) in sources)
                    if (monster.Position != BattlePosition.FaceDownDefense && monster.Card.GameCardId == profile.CardId &&
                        Relates(profile.Relation, binding, occurrence.Subject))
                        yield return new(id++, profile, binding, occurrence);
            }
        }
    }

    private DuelState PrepareEventEffects(DuelState before, DuelState next, List<DuelEvent> events)
    {
        if (triggerEffects.Length == 0 && continuousEffects.Length == 0) return next;
        var current = next.EventEffects;
        var timed = current.TimedModifiers.Where(m => m.Target.IsStillPresent(next) &&
            next.GetPlayer(m.Target.Player).MonsterZones[m.Target.Zone]!.Position != BattlePosition.FaceDownDefense &&
            m.ExpiresOnTurn == next.TurnNumber && next.Phase != DuelPhase.End).ToArray();
        var applied = CalculateModifiers(next, timed);
        foreach (var old in before.EventEffects.AppliedModifiers.Except(applied))
            events.Add(new StatModifierRemoved(old, old.ExpiresOnTurn is not null &&
                (next.Phase == DuelPhase.End || old.ExpiresOnTurn != next.TurnNumber)
                    ? ModifierRemovalReason.EndPhaseReached : ModifierRemovalReason.ConditionNoLongerMet));
        foreach (var added in applied.Except(before.EventEffects.AppliedModifiers)) events.Add(new StatModifierApplied(added));
        var statistics = FieldMonsters(next).Select(entry => CalculateStatistics(entry.Binding, applied)).ToArray();
        var deferred = current.DeferredTriggers.ToList();
        if (next.Outcome is null)
            deferred.AddRange(CollectTriggers(before, next, events.ToArray(), deferred.Count + 1));
        else deferred.Clear(); // terminal damage keeps existing defeat-before-destruction semantics
        var pending = current.PendingTriggers;
        if (next.Outcome is null && next.PendingChain is null && next.PendingDecision is null && pending is null && deferred.Count != 0)
        {
            var eligible = deferred.Where(t => CanActivateTrigger(next, t)).ToArray();
            foreach (var unavailable in deferred.Except(eligible)) events.Add(new TriggerUnavailable(unavailable));
            deferred.Clear();
            if (eligible.Length != 0)
            {
                pending = new(checked(State.Revision + 1), next.ActivePlayer, eligible);
                events.Add(new TriggerOrderRequested(pending));
            }
        }
        return next.WithEventEffects(new(pending, deferred, timed, applied, statistics));
    }

    private DuelActionResult OrderTriggers(ChooseTriggerOrder command)
    {
        var batch = State.EventEffects.PendingTriggers!;
        if (command.DecisionId != batch.DecisionId) return Reject(DuelError.StaleTriggerDecision);
        if (command.Player != batch.Player) return Reject(DuelError.NotTriggerPlayer);
        var ids = command.TriggerIds?.ToArray();
        if (ids is null || ids.Distinct().Count() != ids.Length || ids.Any(id => !batch.Options.Any(t => t.Id == id)) ||
            batch.Mandatory && ids.Length != batch.Options.Count)
            return Reject(DuelError.InvalidTriggerOrder);
        var chosen = ids.Select(id => batch.Options.Single(t => t.Id == id)).ToArray();
        var ordered = batch.Ordered.Concat(chosen).ToArray();
        var remaining = batch.Remaining.Except(batch.Options).ToArray();
        List<DuelEvent> events = [new TriggerOrderChosen(command.Player, Array.AsReadOnly(ids))];
        if (remaining.Length != 0)
        {
            var pending = new PendingTriggerBatch(checked(State.Revision + 1), State.ActivePlayer, remaining, ordered);
            events.Add(new TriggerOrderRequested(pending));
            return Accept(State.WithEventEffects(new(pending, State.EventEffects.DeferredTriggers,
                State.EventEffects.TimedModifiers, State.EventEffects.AppliedModifiers, State.EventEffects.Statistics)), events.ToArray());
        }
        var next = State.WithEventEffects(new(null, State.EventEffects.DeferredTriggers,
            State.EventEffects.TimedModifiers, State.EventEffects.AppliedModifiers, State.EventEffects.Statistics));
        if (ordered.Length == 0) return Accept(next, events.ToArray());
        var links = ordered.Select((t, index) => new ChainLink(index + 1,
            new(t.Player, t.Profile.EffectId, t.Source.Card, t.Source.Zone, null, null),
            t.Source.PlacementId, ChainOperation.TriggerEffect, Trigger: t)).ToArray();
        var chain = new PendingChain(checked(State.Revision + 1), links, Other(ordered[^1].Player),
            freezeOnCompletion: State.ResearchSliceComplete);
        foreach (var link in links)
        {
            events.Add(new TriggerActivated(link.Trigger!, link.Number));
            events.Add(new ChainLinkAdded(link));
        }
        events.Add(new ChainResponseRequested(chain.Responder, chain.DecisionId));
        return Accept(next.WithChain(chain), events.ToArray());
    }

    private DuelState ResolveTrigger(DuelState next, TriggerActivation trigger, List<DuelEvent> events)
    {
        var profile = trigger.Profile;
        if (profile.Operation == TriggerOperation.GainLifePoints)
        {
            var player = next.GetPlayer(trigger.Player);
            int after = (int)Math.Min(int.MaxValue, (long)player.LifePoints + profile.LifePointGain);
            events.Add(new LifePointsGained(player.Id, after - player.LifePoints, player.LifePoints, after, profile.EffectId));
            return next.With(player: player.With(lifePoints: after));
        }
        var subject = trigger.Occurrence.Subject;
        if (!subject.IsStillPresent(next) || next.GetPlayer(subject.Player).MonsterZones[subject.Zone]!.Position == BattlePosition.FaceDownDefense)
        {
            events.Add(new TriggerSubjectUnavailable(trigger));
            return next;
        }
        var modifier = new StatModifier(profile.EffectId, trigger.Source, subject, profile.AttackDelta,
            profile.DefenseDelta, checked(State.Revision + 1), State.TurnNumber);
        var effects = next.EventEffects;
        return next.WithEventEffects(new(effects.PendingTriggers, effects.DeferredTriggers,
            effects.TimedModifiers.Append(modifier), effects.AppliedModifiers, effects.Statistics));
    }

    private StatModifier[] CalculateModifiers(DuelState state, IEnumerable<StatModifier> timed)
    {
        var monsters = FieldMonsters(state).Where(e => e.Monster.Position != BattlePosition.FaceDownDefense).ToArray();
        var result = timed.ToList();
        foreach (var (source, monster) in monsters)
            foreach (var profile in continuousEffects.Where(p => p.CardId == monster.Card.GameCardId).OrderBy(p => p.EffectId, StringComparer.Ordinal))
                foreach (var (target, _) in monsters)
                    if (Relates(profile.Affects, source, target))
                        result.Add(new(profile.EffectId, source, target, profile.AttackDelta, profile.DefenseDelta));
        return result.ToArray();
    }

    private EffectiveMonsterStatistics CalculateStatistics(MonsterBinding monster, IEnumerable<StatModifier> modifiers)
    {
        var printed = catalog[monster.Card.GameCardId].Statistics;
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
        return CalculateStatistics(new(player, zone, monster.Card, monster.PlacementId), modifiers);
    }
}
