namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private PendingDecision? CreateDecision(DecisionSequence sequence, int index, IEnumerable<DecisionAnswer> answers)
    {
        var player = State.GetPlayer(State.ActivePlayer);
        var step = sequence.Steps[index];
        CardInstance[] cards = step.Kind == DecisionKind.Card
            ? (step.CardSource == DecisionCardSource.Deck ? player.Deck : player.Hand)
                .Where(c => step.AllowedCardIds.Contains(c.GameCardId)).ToArray()
            : [];
        int[] zones = step.Kind == DecisionKind.MonsterZone
            ? Enumerable.Range(0, 5).Where(z => player.MonsterZones[z] is null).ToArray()
            : [];
        if (step.Kind == DecisionKind.Card && cards.Length == 0 ||
            step.Kind == DecisionKind.MonsterZone && zones.Length == 0)
            return null;
        return new(checked(State.Revision + 1), sequence, player.Id, index, cards, zones, answers);
    }

    private DuelActionResult BeginDecisions(string sequenceId)
    {
        if (sequenceId is null || !decisionSequences.TryGetValue(sequenceId, out var sequence))
            return Reject(DuelError.UnsupportedDecisionSequence);
        if (State.Phase != DuelPhase.Main1) return Reject(DuelError.WrongPhase);
        // No card movement occurs during this selection-only workflow. Preflight
        // every step so a later empty choice cannot strand the active sequence.
        if (Enumerable.Range(0, sequence.Steps.Count).Any(i => CreateDecision(sequence, i, []) is null))
            return Reject(DuelError.DecisionUnavailable);
        var pending = CreateDecision(sequence, 0, [])!;
        return Accept(State.WithDecision(pending), new DecisionRequested(pending));
    }

    private DuelActionResult AnswerDecision(DecisionCommand command)
    {
        var pending = State.PendingDecision;
        if (pending is null) return Reject(DuelError.NoPendingDecision);
        if (command.Player != pending.Player) return Reject(DuelError.NotActivePlayer);
        if (command.DecisionId != pending.DecisionId) return Reject(DuelError.StaleDecision);
        var kind = command switch
        {
            ChooseDecisionCard => DecisionKind.Card,
            ChooseDecisionPosition => DecisionKind.Position,
            ChooseDecisionZone => DecisionKind.MonsterZone,
            _ => (DecisionKind)(-1)
        };
        if (kind != pending.Kind) return Reject(DuelError.WrongDecisionKind);
        DecisionAnswer? answer = command switch
        {
            ChooseDecisionCard c when pending.CardOptions.Any(card => card.InstanceId == c.CardInstanceId) =>
                new(kind, CardInstanceId: c.CardInstanceId),
            ChooseDecisionPosition c when pending.PositionOptions.Contains(c.Position) => new(kind, Position: c.Position),
            ChooseDecisionZone c when pending.MonsterZoneOptions.Contains(c.MonsterZone) => new(kind, MonsterZone: c.MonsterZone),
            _ => null
        };
        if (answer is null) return Reject(DuelError.InvalidDecisionChoice);
        var answers = pending.Answers.Append(answer).ToArray();
        var sequence = State.PendingSpellActivation is { } activation
            ? spellEffects[activation.Spell.GameCardId].Decisions
            : decisionSequences[pending.SequenceId];
        var answered = new DecisionAnswered(command.Player, sequence.Id, pending.DecisionId, answer);
        if (pending.StepIndex + 1 < sequence.Steps.Count)
        {
            var next = CreateDecision(sequence, pending.StepIndex + 1, answers);
            if (next is null) return Reject(DuelError.DecisionUnavailable);
            return Accept(State.WithDecision(next), answered, new DecisionRequested(next));
        }
        var result = new DecisionSequenceResult(pending, answers);
        if (State.PendingSpellActivation is not null) return ResolveSpell(result);
        return Accept(State.WithDecision(null, result, sequence.StopAfterCompletion), answered,
            new DecisionSequenceCompleted(result));
    }
}
