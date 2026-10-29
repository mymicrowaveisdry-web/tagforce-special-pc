namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private static TriggerUse? UseKey(DuelState state, TriggerActivation trigger) => trigger.Profile.TurnLimit switch
    {
        TriggerTurnLimit.PerPlacement => new(trigger.Player, state.TurnNumber, trigger.Profile.LimitKey,
            trigger.Source.Card.InstanceId, trigger.Source.PlacementId),
        TriggerTurnLimit.SharedPerPlayer => new(trigger.Player, state.TurnNumber, trigger.Profile.LimitKey, null, null),
        _ => null
    };

    private static bool IsTriggerUsed(DuelState state, TriggerActivation trigger) =>
        UseKey(state, trigger) is { } key && state.EventEffects.UsedTriggers.Contains(key);

    private static IEnumerable<MonsterBinding> TriggerTargets(DuelState state, TriggerActivation trigger)
    {
        var policy = trigger.Profile.Target!;
        return FieldMonsters(state).Where(e => e.Monster.Position != BattlePosition.FaceDownDefense && !e.Monster.BattleDestroyed &&
            Relates(policy.Relation, trigger.Source, e.Binding) &&
            (!policy.ExcludeSource || e.Binding != trigger.Source) &&
            (policy.AllowedCardIds.Count == 0 || policy.AllowedCardIds.Contains(e.Monster.Card.GameCardId)))
            .Select(e => e.Binding);
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
            if (trigger.Profile.Target is not null && trigger.Target is null)
                return RequestTriggerCommitment(next, remaining, committed, queue,
                    new(checked(State.Revision + 1), trigger, TriggerCommitmentKind.MonsterTarget,
                        targets: TriggerTargets(next, trigger)), events);
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
        var chain = new PendingChain(checked(State.Revision + 1), links, Other(committed[^1].Player),
            freezeOnCompletion: State.ResearchSliceComplete);
        events.Add(new ChainResponseRequested(chain.Responder, chain.DecisionId));
        return Accept(next.WithChain(chain), events.ToArray());
    }

    private static ChainLink TriggerLink(int number, TriggerActivation trigger) => new(number,
        new(trigger.Player, trigger.Profile.EffectId, trigger.Source.Card, trigger.Source.Zone, trigger.DiscardedCost, null),
        trigger.Source.PlacementId, ChainOperation.TriggerEffect, Trigger: trigger);

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
        var id = command switch { ChooseTriggerCostCard c => c.DecisionId, ChooseTriggerMonsterTarget t => t.DecisionId, _ => -1 };
        if (id == -1) return Reject(DuelError.TriggerCommitmentPending);
        if (id != pending.DecisionId) return Reject(DuelError.StaleTriggerDecision);
        if (command.Player != pending.Player) return Reject(DuelError.NotTriggerPlayer);
        var trigger = pending.Trigger;
        var next = State;
        List<DuelEvent> events = [];
        switch (command)
        {
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
