namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private PendingDecision? CreateDecision(DecisionSequence sequence, int index, IEnumerable<DecisionAnswer> answers,
        int? excludeInstanceId = null, PlayerId? decisionPlayer = null)
    {
        var player = State.GetPlayer(decisionPlayer ?? State.PendingSpellActivation?.Player ?? State.ActivePlayer);
        var step = sequence.Steps[index];
        CardInstance[] cards = step.Kind == DecisionKind.Card
            ? SpecialSourceCards(player, step.CardSource!.Value)
                .Where(c => c.InstanceId != excludeInstanceId && step.Allows(c.GameCardId)).ToArray()
            : [];
        int[] zones = step.Kind == DecisionKind.MonsterZone
            ? Enumerable.Range(0, 5).Where(z => player.MonsterZones[z] is null).ToArray()
            : [];
        var excluded = excludeInstanceId ?? State.PendingSpellActivation?.Spell.InstanceId;
        var targets = step.Kind == DecisionKind.SpellTrapTarget
            ? State.Players.SelectMany(p => p.SpellTrapFields.Select((field, zone) => (field, zone))
                .Where(s => s.field is not null && s.field.Card.InstanceId != excluded)
                .Select(s => new SpellTrapTarget(p.Id, s.zone, s.field!.Card, s.field.PlacementId))
                .Concat(p.PendulumZones.Select((field, zone) => (field, zone))
                    .Where(s => s.field is not null && s.field.Card.InstanceId != excluded)
                    .Select(s => new SpellTrapTarget(p.Id, s.zone, s.field!.Card, s.field.PlacementId, true)))
                .Concat(p.FieldSpell is { } field && field.Card.InstanceId != excluded
                    ? [new SpellTrapTarget(p.Id, DuelPlayerState.FieldZoneIndex, field.Card, field.PlacementId)] : Array.Empty<SpellTrapTarget>())).ToArray() : [];
        FieldCardTarget[]? fieldTargets = (step.FieldTargetSpec switch
            {
                FieldTarget spec => FieldMatches(State, player.Id, spec.Kind, spec.Side, spec.Filter, excluded).Where(t => TargetPasses(State, spec, t)),
                ZoneTarget zone => ZoneMatches(State, player.Id, zone),
                _ => null
            })?.Where(t => !answers.Any(a => a.FieldTarget is { } chosen && chosen.Card == t.Card)).ToArray();
        if (step.Kind == DecisionKind.Card && cards.Length == 0 ||
            step.Kind == DecisionKind.MonsterZone && zones.Length == 0 ||
            step.Kind == DecisionKind.SpellTrapTarget && targets.Length == 0 ||
            step.Kind == DecisionKind.FieldTarget && fieldTargets!.Length == 0)
            return null;
        return new(checked(State.Revision + 1), sequence, player.Id, index, cards, zones, answers, targets, fieldTargets);
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
        if (command is FinishDecision)
            return pending.CanFinish && State.PendingSpellActivation is { } finishing && definitions.ContainsKey(finishing.Spell.GameCardId)
                ? FinishDefinitionDecisions(pending) : Reject(DuelError.InvalidDecisionChoice);
        var kind = command switch
        {
            ChooseDecisionCard => DecisionKind.Card,
            ChooseDecisionPosition => DecisionKind.Position,
            ChooseDecisionZone => DecisionKind.MonsterZone,
            ChooseDecisionTarget => DecisionKind.SpellTrapTarget,
            ChooseDecisionFieldTarget => DecisionKind.FieldTarget,
            _ => (DecisionKind)(-1)
        };
        if (kind != pending.Kind) return Reject(DuelError.WrongDecisionKind);
        DecisionAnswer? answer = command switch
        {
            ChooseDecisionCard c when pending.CardOptions.Any(card => card.InstanceId == c.CardInstanceId) =>
                new(kind, CardInstanceId: c.CardInstanceId),
            ChooseDecisionPosition c when pending.PositionOptions.Contains(c.Position) => new(kind, Position: c.Position),
            ChooseDecisionZone c when pending.MonsterZoneOptions.Contains(c.MonsterZone) => new(kind, MonsterZone: c.MonsterZone),
            ChooseDecisionTarget c when pending.TargetOptions.Any(t => t.Player == c.TargetPlayer &&
                t.Zone == c.SpellTrapZone && t.Card.InstanceId == c.CardInstanceId && t.IsPendulumZone == c.IsPendulumZone) =>
                new(kind, Target: pending.TargetOptions.Single(t => t.Player == c.TargetPlayer &&
                    t.Zone == c.SpellTrapZone && t.Card.InstanceId == c.CardInstanceId && t.IsPendulumZone == c.IsPendulumZone)),
            ChooseDecisionFieldTarget c when pending.FieldTargetOptions?.FirstOrDefault(t => t.Player == c.TargetPlayer &&
                t.Location == c.Location && t.Zone == c.Zone && t.Card.InstanceId == c.CardInstanceId) is { } field =>
                new(kind, FieldTarget: field),
            _ => null
        };
        if (answer is null) return Reject(DuelError.InvalidDecisionChoice);
        if (answer.Target is { } binding && !binding.IsStillPresent(binding.Current(State)))
            return Reject(DuelError.InvalidDecisionChoice);
        if (answer.FieldTarget is { } bound && !bound.IsStillPresent(State)) return Reject(DuelError.InvalidDecisionChoice);
        var answers = pending.Answers.Append(answer).ToArray();
        if (State.PendingSpellActivation is { } activation && definitions.ContainsKey(activation.Spell.GameCardId))
            return AnswerDefinitionDecision(pending, answer, answers);
        if (State.PendingSpellActivation is not null) return Reject(DuelError.InvalidEffectState);
        var sequence = decisionSequences[pending.SequenceId];
        var answered = new DecisionAnswered(command.Player, sequence.Id, pending.DecisionId, answer);
        if (pending.StepIndex + 1 < sequence.Steps.Count)
        {
            var next = CreateDecision(sequence, pending.StepIndex + 1, answers);
            if (next is null) return Reject(DuelError.DecisionUnavailable);
            return Accept(State.WithDecision(next), answered, new DecisionRequested(next));
        }
        var result = new DecisionSequenceResult(pending, answers);
        return Accept(State.WithDecision(null, result, sequence.StopAfterCompletion), answered,
            new DecisionSequenceCompleted(result));
    }
}
