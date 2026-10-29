namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult BeginChainResponse(PendingChain chain, int zone, FieldSpellTrap source,
        ChainResponseProfile profile)
    {
        var player = State.GetPlayer(chain.Responder);
        var preceding = chain.Links[^1];
        if (source.IsFaceUp || source.SetOnTurn >= State.TurnNumber ||
            profile.RespondsToCategory is { } category && rules[preceding.Activation.Spell.GameCardId].Category != category)
            return Reject(DuelError.UnsupportedChainResponse);
        if ((int)profile.SpellSpeed < (int)preceding.SpellSpeed)
            return Reject(DuelError.ChainResponseSpeedNotAllowed);
        var sequence = profile.Decisions;
        if (sequence is not null && Enumerable.Range(0, sequence.Steps.Count)
            .Any(i => CreateDecision(sequence, i, [], source.Card.InstanceId, player.Id) is null))
            return Reject(DuelError.DecisionUnavailable);
        var slots = player.SpellTrapFields.ToArray();
        slots[zone] = source with { IsFaceUp = true };
        var next = State.With(player: player.With(spellTraps: slots));
        List<DuelEvent> events = [new ChainResponseActivated(player.Id, profile.EffectId, source.Card, zone)];
        if (sequence is not null)
        {
            var decision = CreateDecision(sequence, 0, [], source.Card.InstanceId, player.Id)!;
            var activation = new PendingSpellActivation(player.Id, profile.EffectId, source.Card, zone,
                ResponseToLink: preceding.Number, SourcePlacementId: source.PlacementId);
            events.Add(new DecisionRequested(decision));
            return Accept(next.WithActivation(activation).WithDecision(decision), events.ToArray());
        }
        return AppendChainResponse(next, new(player.Id, profile.EffectId, source.Card, zone, null, null),
            source.PlacementId, preceding.Number, profile, events);
    }

    private DuelActionResult AnswerChainResponse(PendingDecision pending, DecisionAnswer answer, DecisionAnswer[] answers)
    {
        var chain = State.PendingChain!;
        var activation = State.PendingSpellActivation!;
        var player = State.GetPlayer(activation.Player);
        if (!chainResponses.TryGetValue(activation.Spell.GameCardId, out var profile) ||
            profile.Decisions is not { } sequence || profile.EffectId != activation.EffectId ||
            player.Id != chain.Responder || pending.Player != player.Id ||
            activation.ResponseToLink != chain.Links[^1].Number ||
            player.SpellTrapFields[activation.SpellTrapZone] is not { IsFaceUp: true } source ||
            source.Card != activation.Spell || source.PlacementId != activation.SourcePlacementId)
            return Reject(DuelError.InvalidEffectState);
        var next = State;
        List<DuelEvent> events = [new DecisionAnswered(player.Id, pending.SequenceId, pending.DecisionId, answer)];
        if (pending.Kind == DecisionKind.Card)
        {
            var cost = player.Hand.SingleOrDefault(c => c.InstanceId == answer.CardInstanceId);
            if (cost is null || cost.Owner != player.Id || activation.DiscardedCost is not null ||
                cost.InstanceId == source.Card.InstanceId || !profile.DiscardCardIds.Contains(cost.GameCardId))
                return Reject(DuelError.InvalidEffectState);
            activation = activation with { DiscardedCost = cost };
            next = next.With(player: player.With(hand: player.Hand.Where(c => c != cost),
                graveyard: player.Graveyard.Append(cost))).WithActivation(activation);
            events.Add(new ActivationCostPaid(player.Id, profile.EffectId, cost));
        }
        if (pending.StepIndex + 1 < sequence.Steps.Count)
        {
            var decision = CreateDecision(sequence, pending.StepIndex + 1, answers);
            if (decision is null) return Reject(DuelError.DecisionUnavailable);
            events.Add(new DecisionRequested(decision));
            return Accept(next.WithDecision(decision), events.ToArray());
        }
        var result = new DecisionSequenceResult(pending, answers);
        var target = answers.SingleOrDefault(a => a.Kind == DecisionKind.SpellTrapTarget)?.Target;
        var receipt = new CommittedActivation(player.Id, profile.EffectId, source.Card,
            activation.SpellTrapZone, activation.DiscardedCost, target);
        events.Add(new DecisionSequenceCompleted(result));
        return AppendChainResponse(next.WithDecision(null, result).WithActivation(null), receipt,
            source.PlacementId, activation.ResponseToLink, profile, events);
    }

    private DuelActionResult AppendChainResponse(DuelState next, CommittedActivation receipt, long placement,
        int precedingNumber, ChainResponseProfile profile, List<DuelEvent> events)
    {
        var chain = State.PendingChain!;
        var link = new ChainLink(chain.Links.Count + 1, receipt, placement, profile.Operation, profile.SpellSpeed,
            profile.Operation is ChainOperation.NegateActivation or ChainOperation.NegateEffect ? precedingNumber : null,
            DestroyNegatedSource: profile.DestroyNegatedSource);
        var pending = new PendingChain(checked(State.Revision + 1), chain.Links.Append(link), Other(receipt.Player));
        events.Add(new ActivationCommitted(receipt));
        events.Add(new ChainLinkAdded(link));
        events.Add(new ChainResponseRequested(pending.Responder, pending.DecisionId));
        return Accept(next.WithChain(pending), events.ToArray());
    }
}
