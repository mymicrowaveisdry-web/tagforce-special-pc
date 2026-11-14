namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private static TriggerUse? UseKey(DuelState state, TriggerActivation trigger) => trigger.Profile.TurnLimit switch
    {
        TriggerTurnLimit.PerPlacement => new(trigger.Player, state.TurnNumber, trigger.Profile.LimitKey,
            trigger.Source.Card.InstanceId, trigger.Source.PlacementId),
        TriggerTurnLimit.SharedPerPlayer => new(trigger.Player, state.TurnNumber, trigger.Profile.LimitKey, null, null),
        // Turn 0 marks a use that lasts the whole Duel.
        TriggerTurnLimit.SharedPerDuel => new(trigger.Player, 0, trigger.Profile.LimitKey, null, null),
        // Kept by the card's owner so a change of control does not reset it; a new placement does.
        TriggerTurnLimit.WhileFaceUp => new(trigger.Source.Card.Owner, 0, trigger.Profile.LimitKey, trigger.Source.Card.InstanceId, trigger.Source.PlacementId),
        _ => null
    };

    private static bool IsTriggerUsed(DuelState state, TriggerActivation trigger) =>
        UseKey(state, trigger) is { } key && state.EventEffects.UsedTriggers.Contains(key);

    private IEnumerable<MonsterBinding> TriggerTargets(DuelState state, TriggerActivation trigger)
    {
        var policy = trigger.Profile.Target!;
        return FieldMonsters(state).Where(e => (policy.AllowFaceDown || e.Monster.Position != BattlePosition.FaceDownDefense) && !e.Monster.BattleDestroyed &&
            Relates(policy.Relation, trigger.Source, e.Binding) &&
            (!policy.ExcludeSource || e.Binding != trigger.Source) &&
            (policy.AllowedCardIds.Count == 0 || policy.AllowedCardIds.Contains(e.Monster.Card.GameCardId)) &&
            !RuleLocked(state, LockedAction.BeTargeted, e.Binding.Player, e.Monster.Card, e.Monster, targeter: trigger.Player, byCategory: rules[trigger.Source.Card.GameCardId].Category))
            .Select(e => e.Binding);
    }

    /// <summary>Field cards a trigger's own target can choose, excluding cards that cannot be targeted.</summary>
    private IEnumerable<FieldCardTarget> TriggerFieldTargets(DuelState state, TriggerActivation trigger, FieldTarget spec) =>
        FieldMatches(state, trigger.Player, spec.Kind, spec.Side, spec.Filter, null).Where(t => TargetPasses(state, spec, t) &&
            !RuleLocked(state, LockedAction.BeTargeted, t.Player, t.Card, t.Location == FieldLocation.MonsterZone ? state.GetPlayer(t.Player).MonsterZones[t.Zone] : null,
                targeter: trigger.Player, byCategory: rules[trigger.Source.Card.GameCardId].Category));

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
        var remaining = batch.Remaining.Except(batch.Options).ToArray();
        List<DuelEvent> events = [new TriggerOrderChosen(command.Player, Array.AsReadOnly(ids))];
        return AdvanceTriggerCommitments(State, remaining, batch.Ordered, chosen, events);
    }

    private DuelActionResult AdvanceTriggerCommitments(DuelState next, IEnumerable<TriggerActivation> remainingItems,
        IEnumerable<TriggerActivation> committedItems, IEnumerable<TriggerActivation> queueItems, List<DuelEvent> events)
    {
        var remaining = remainingItems.ToArray();
        var committed = committedItems.ToList();
        var queue = queueItems.ToList();
        while (queue.Count != 0)
        {
            var trigger = queue[0];
            if (!CanActivateTrigger(next, trigger))
            {
                events.Add(new TriggerUnavailable(trigger)); queue.RemoveAt(0); continue;
            }
            var player = next.GetPlayer(trigger.Player);
            if (trigger.Profile.DiscardCardIds.Count != 0 && trigger.DiscardedCost is null)
                return RequestTriggerCommitment(next, remaining, committed, queue,
                    new(checked(State.Revision + 1), trigger, TriggerCommitmentKind.DiscardCost,
                        cards: player.Hand.Where(c => trigger.Profile.DiscardCardIds.Contains(c.GameCardId))), events);
            if (trigger.PaidLifePoints == 0 && trigger.Profile.LifePointCost != 0)
            {
                (next, trigger) = PayTriggerCost(next, trigger, null, events);
                queue[0] = trigger;
            }
            if (trigger.Profile.TributeCost && trigger.TributedCost is null)
                return RequestTriggerCommitment(next, remaining, committed, queue,
                    new(checked(State.Revision + 1), trigger, TriggerCommitmentKind.TributeCost,
                        targets: FieldMonsters(next).Where(e => e.Binding.Player == trigger.Player && !e.Monster.BattleDestroyed && !RuleLocked(next, LockedAction.BeTributed, e.Binding.Player, e.Monster.Card, e.Monster)).Select(e => e.Binding)), events);
            if (trigger.Profile.Target is not null && trigger.Target is null)
                return RequestTriggerCommitment(next, remaining, committed, queue,
                    new(checked(State.Revision + 1), trigger, TriggerCommitmentKind.MonsterTarget,
                        targets: TriggerTargets(next, trigger)), events);
            if (trigger is { TargetCard: null, Profile.Definition.FieldTargets: [ZoneTarget zone] })
                return RequestTriggerCommitment(next, remaining, committed, queue,
                    new(checked(State.Revision + 1), trigger, TriggerCommitmentKind.ZoneTarget, zones: ZoneMatches(next, trigger.Player, zone)), events);
            if (trigger is { TargetCard: null, Profile.Definition.FieldTargets: [FieldTarget field] })
                return RequestTriggerCommitment(next, remaining, committed, queue,
                    new(checked(State.Revision + 1), trigger, TriggerCommitmentKind.ZoneTarget, zones: TriggerFieldTargets(next, trigger, field)), events);
            if (trigger.Profile.Definition?.Declares is { } declares && trigger.Declared is null)
                return RequestTriggerCommitment(next, remaining, committed, queue,
                    new(checked(State.Revision + 1), trigger, TriggerCommitmentKind.Declaration, declaration: declares,
                        declarationOptions: DeclarationOptions(declares)), events);
            if (trigger.Profile.DetachMaterialCount != 0 && trigger.DetachedCost.Count == 0)
                return RequestTriggerCommitment(next, remaining, committed, queue,
                    new(checked(State.Revision + 1), trigger, TriggerCommitmentKind.DetachCost,
                        cards: next.GetPlayer(trigger.Player).MonsterZones[trigger.Source.Zone]!.XyzMaterials), events);
            if (UseKey(next, trigger) is { } use)
            {
                next = next.WithEventEffects(next.EventEffects.WithUses(next.EventEffects.UsedTriggers.Append(use)));
                events.Add(new TriggerLimitConsumed(use));
            }
            committed.Add(trigger);
            var link = TriggerLink(committed.Count, trigger);
            events.Add(new TriggerActivated(trigger, link.Number));
            events.Add(new ChainLinkAdded(link));
            queue.RemoveAt(0);
        }
        var eligible = remaining.Where(t => CanActivateTrigger(next, t)).ToArray();
        foreach (var blocked in remaining.Except(eligible)) events.Add(new TriggerUnavailable(blocked));
        if (eligible.Length != 0)
        {
            var pending = new PendingTriggerBatch(checked(State.Revision + 1), next.ActivePlayer, eligible, committed);
            events.Add(new TriggerOrderRequested(pending));
            return Accept(next.WithEventEffects(next.EventEffects.WithTriggers(pending)), events.ToArray());
        }
        next = next.WithEventEffects(next.EventEffects.WithTriggers(null));
        if (committed.Count == 0) return Accept(next, events.ToArray());
        var links = committed.Select((t, i) => TriggerLink(i + 1, t)).ToArray();
        var chain = new PendingChain(checked(State.Revision + 1), links, Other(committed[^1].Player));
        events.Add(new ChainResponseRequested(chain.Responder, chain.DecisionId));
        return Accept(next.WithChain(chain), events.ToArray());
    }

    private static ChainLink TriggerLink(int number, TriggerActivation trigger)
    {
        IReadOnlyList<FieldCardTarget>? bound = trigger.Profile.Definition?.FieldTargets switch
        {
            [TriggerTargetedMonster] when trigger.Target is { } target => [new(target.Player, FieldLocation.MonsterZone, target.Zone, target.Card, target.PlacementId)],
            [TriggerSourceMonster] => [new(trigger.Source.Player, FieldLocation.MonsterZone, trigger.Source.Zone, trigger.Source.Card, trigger.Source.PlacementId)],
            [ZoneTarget or FieldTarget] when trigger.TargetCard is { } card => [card],
            _ => null
        };
        return new(number, new(trigger.Player, trigger.Profile.EffectId, trigger.Source.Card, trigger.Source.Zone, trigger.DiscardedCost, null, bound),
            trigger.Source.PlacementId, ChainOperation.TriggerEffect, Trigger: trigger, Declared: trigger.Declared);
    }

    private DuelActionResult RequestTriggerCommitment(DuelState next, TriggerActivation[] remaining,
        IEnumerable<TriggerActivation> committed, IEnumerable<TriggerActivation> queue,
        PendingTriggerCommitment commitment, List<DuelEvent> events)
    {
        var batch = new PendingTriggerBatch(checked(State.Revision + 1), next.ActivePlayer, remaining, committed, queue);
        events.Add(new TriggerCommitmentRequested(commitment));
        return Accept(next.WithEventEffects(next.EventEffects.WithTriggers(batch, commitment)), events.ToArray());
    }

    private (DuelState State, TriggerActivation Trigger) PayTriggerCost(DuelState next, TriggerActivation trigger,
        CardInstance? discard, List<DuelEvent> events)
    {
        var player = next.GetPlayer(trigger.Player);
        int cost = trigger.Profile.LifePointCost;
        var paid = trigger with { DiscardedCost = discard, PaidLifePoints = cost };
        events.Add(new TriggerCostPaid(paid, discard, cost));
        return (next.With(player: player.With(lifePoints: player.LifePoints - cost,
            hand: discard is null ? player.Hand : player.Hand.Where(c => c != discard),
            graveyard: discard is null ? player.Graveyard : player.Graveyard.Append(discard))), paid);
    }

    private DuelActionResult AnswerTriggerCommitment(DuelCommand command)
    {
        var pending = State.EventEffects.PendingCommitment!;
        var batch = State.EventEffects.PendingTriggers!;
        var id = command switch { ChooseTriggerCostCard c => c.DecisionId, ChooseTriggerMonsterTarget t => t.DecisionId,
            ChooseTriggerXyzMaterials x => x.DecisionId, ChooseTriggerZoneTarget z => z.DecisionId, DeclareValue d => d.DecisionId, _ => -1 };
        if (id == -1) return Reject(DuelError.TriggerCommitmentPending);
        if (id != pending.DecisionId) return Reject(DuelError.StaleTriggerDecision);
        if (command.Player != pending.Player) return Reject(DuelError.NotTriggerPlayer);
        var trigger = pending.Trigger;
        var next = State;
        List<DuelEvent> events = [];
        switch (command)
        {
            case ChooseTriggerXyzMaterials detach:
            {
                if (pending.Kind != TriggerCommitmentKind.DetachCost) return Reject(DuelError.WrongTriggerCommitment);
                if (!trigger.Source.IsStillPresent(State)) return Reject(DuelError.InvalidTriggerCommitment);
                var player = State.GetPlayer(trigger.Player);
                var monster = player.MonsterZones[trigger.Source.Zone]!;
                var ids = detach.MaterialInstanceIds;
                if (ids.Count != trigger.Profile.DetachMaterialCount || ids.Distinct().Count() != ids.Count ||
                    ids.Any(id => !pending.CardOptions.Any(c => c.InstanceId == id) || !monster.XyzMaterials.Any(c => c.InstanceId == id)))
                    return Reject(DuelError.InvalidTriggerCommitment);
                var cards = monster.XyzMaterials.Where(c => ids.Contains(c.InstanceId)).ToArray();
                var zones = player.MonsterZones.ToArray();
                zones[trigger.Source.Zone] = monster with { XyzMaterials = Array.AsReadOnly(monster.XyzMaterials.Except(cards).ToArray()) };
                next = next.With(player: player.With(monsters: zones));
                next = SendXyzMaterials(next, monster.Card, cards, true, events);
                trigger = trigger with { DetachedCost = Array.AsReadOnly(cards) };
                break;
            }
            case ChooseTriggerCostCard cost:
            {
                if (pending.Kind != TriggerCommitmentKind.DiscardCost) return Reject(DuelError.WrongTriggerCommitment);
                var card = pending.CardOptions.SingleOrDefault(c => c.InstanceId == cost.CardInstanceId);
                var player = State.GetPlayer(command.Player);
                if (card is null || !player.Hand.Contains(card) || player.LifePoints <= trigger.Profile.LifePointCost)
                    return Reject(DuelError.InvalidTriggerCommitment);
                (next, trigger) = PayTriggerCost(next, trigger, card, events);
                break;
            }
            case DeclareValue declared:
            {
                if (pending.Kind != TriggerCommitmentKind.Declaration) return Reject(DuelError.WrongTriggerCommitment);
                if (!DeclarationValid(pending.Declaration!.Value, declared.Value)) return Reject(DuelError.InvalidTriggerCommitment);
                trigger = trigger with { Declared = declared.Value };
                events.Add(new DeclarationMade(command.Player, pending.Declaration.Value, declared.Value, trigger.Profile.EffectId));
                break;
            }
            case ChooseTriggerZoneTarget zone:
            {
                if (pending.Kind != TriggerCommitmentKind.ZoneTarget) return Reject(DuelError.WrongTriggerCommitment);
                var chosen = pending.ZoneOptions.SingleOrDefault(o => o.Player == zone.TargetPlayer && o.Card.InstanceId == zone.CardInstanceId);
                if (chosen is null || !chosen.IsStillPresent(State)) return Reject(DuelError.InvalidTriggerCommitment);
                trigger = trigger with { TargetCard = chosen };
                events.Add(new TriggerZoneTargetChosen(trigger, chosen));
                break;
            }
            case ChooseTriggerMonsterTarget tribute when pending.Kind == TriggerCommitmentKind.TributeCost:
            {
                var paid = pending.TargetOptions.SingleOrDefault(t => t.Player == tribute.TargetPlayer && t.Zone == tribute.MonsterZone && t.Card.InstanceId == tribute.CardInstanceId);
                if (paid is null || !paid.IsStillPresent(State)) return Reject(DuelError.InvalidTriggerCommitment);
                next = SendFieldCardToGraveyard(RemovePlacement(next, new(paid.Player, FieldLocation.MonsterZone, paid.Zone, paid.Card, paid.PlacementId)),
                    paid.Card, new MonsterTributed(paid), events);
                trigger = trigger with { TributedCost = paid };
                break;
            }
            case ChooseTriggerMonsterTarget target:
            {
                if (pending.Kind != TriggerCommitmentKind.MonsterTarget) return Reject(DuelError.WrongTriggerCommitment);
                var selected = pending.TargetOptions.SingleOrDefault(t => t.Player == target.TargetPlayer && t.Zone == target.MonsterZone &&
                    t.Card.InstanceId == target.CardInstanceId);
                if (selected is null || !selected.IsStillPresent(State) || !TriggerTargets(State, trigger).Contains(selected))
                    return Reject(DuelError.InvalidTriggerCommitment);
                trigger = trigger with { Target = selected };
                events.Add(new TriggerTargetChosen(trigger, selected));
                break;
            }
        }
        return AdvanceTriggerCommitments(next, batch.Remaining, batch.Ordered,
            new[] { trigger }.Concat(batch.CommitQueue.Skip(1)), events);
    }
}
