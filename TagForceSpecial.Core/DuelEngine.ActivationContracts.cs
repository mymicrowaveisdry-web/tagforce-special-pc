namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult ActivateContract(CardInstance card, int zone, ActivationContract contract)
    {
        var player = State.GetPlayer(State.ActivePlayer);
        if (player.SpellTrapFields[zone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        if (Enumerable.Range(0, contract.Decisions.Steps.Count)
            .Any(i => CreateDecision(contract.Decisions, i, [], card.InstanceId) is null))
            return Reject(DuelError.DecisionUnavailable);
        var decision = CreateDecision(contract.Decisions, 0, [], card.InstanceId)!;
        var fields = player.SpellTrapFields.ToArray();
        fields[zone] = new(card, true, checked(State.Revision + 1));
        var next = State.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != card.InstanceId), spellTraps: fields))
            .WithActivation(new(player.Id, contract.EffectId, card, zone)).WithDecision(decision);
        return Accept(next, new SpellActivated(player.Id, contract.EffectId, card, zone), new DecisionRequested(decision));
    }

    private DuelActionResult AnswerActivationContract(PendingDecision pending, DecisionAnswer answer,
        DecisionAnswer[] answers, ActivationContract contract)
    {
        var activation = State.PendingSpellActivation!;
        var player = State.GetPlayer(State.ActivePlayer);
        var source = player.SpellTrapFields[activation.SpellTrapZone];
        if (activation.Player != player.Id || activation.EffectId != contract.EffectId ||
            source is null || !source.IsFaceUp || source.Card != activation.Spell)
            return Reject(DuelError.InvalidEffectState);
        var next = State;
        List<DuelEvent> events = [new DecisionAnswered(player.Id, pending.SequenceId, pending.DecisionId, answer)];
        if (pending.Kind == DecisionKind.Card)
        {
            var cost = player.Hand.SingleOrDefault(c => c.InstanceId == answer.CardInstanceId);
            if (cost is null || cost.Owner != player.Id || !contract.DiscardCardIds.Contains(cost.GameCardId) ||
                activation.DiscardedCost is not null || cost.InstanceId == activation.Spell.InstanceId)
                return Reject(DuelError.InvalidEffectState);
            activation = activation with { DiscardedCost = cost };
            next = next.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != cost.InstanceId),
                graveyard: player.Graveyard.Append(cost))).WithActivation(activation);
            events.Add(new ActivationCostPaid(player.Id, contract.EffectId, cost));
        }
        if (pending.StepIndex + 1 < contract.Decisions.Steps.Count)
        {
            var decision = CreateDecision(contract.Decisions, pending.StepIndex + 1, answers);
            if (decision is null) return Reject(DuelError.DecisionUnavailable);
            events.Add(new DecisionRequested(decision));
            return Accept(next.WithDecision(decision), events.ToArray());
        }
        var result = new DecisionSequenceResult(pending, answers);
        var target = answers.SingleOrDefault(a => a.Kind == DecisionKind.SpellTrapTarget)?.Target;
        var committed = new CommittedActivation(player.Id, contract.EffectId, activation.Spell,
            activation.SpellTrapZone, activation.DiscardedCost, target);
        events.Add(new DecisionSequenceCompleted(result));
        events.Add(new ActivationCommitted(committed));
        if (chainEffects.TryGetValue(committed.Spell.GameCardId, out var profile))
        {
            var link = new ChainLink(1, committed, source.PlacementId, profile.Operation);
            var chain = new PendingChain(checked(State.Revision + 1), [link], Other(player.Id));
            events.Add(new ChainLinkAdded(link));
            events.Add(new ChainResponseRequested(chain.Responder, chain.DecisionId));
            return Accept(next.WithDecision(null, result).WithActivation(null)
                .WithCommittedActivation(committed).WithChain(chain), events.ToArray());
        }
        // The original evidence reaches a response window here. A chain resolver
        // must consume this receipt later; do not silently resolve or refund costs.
        return Accept(next.WithDecision(null, result, researchSliceComplete: true).WithActivation(null)
            .WithCommittedActivation(committed), events.ToArray());
    }
}
