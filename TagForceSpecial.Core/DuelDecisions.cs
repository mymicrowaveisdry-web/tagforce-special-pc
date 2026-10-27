namespace TagForceSpecial.Core;

public enum DecisionKind { Card, Position, MonsterZone }
public enum DecisionCardSource { Hand, Deck }

/// <summary>Trusted, immutable selection policy. It does not activate or resolve a card effect.</summary>
public sealed class DecisionStep
{
    public DecisionKind Kind { get; }
    public DecisionCardSource? CardSource { get; }
    public IReadOnlyList<uint> AllowedCardIds { get; }
    public IReadOnlyList<BattlePosition> AllowedPositions { get; }

    private DecisionStep(DecisionKind kind, DecisionCardSource? source, uint[] ids, BattlePosition[] positions)
    {
        Kind = kind;
        CardSource = source;
        AllowedCardIds = Array.AsReadOnly(ids);
        AllowedPositions = Array.AsReadOnly(positions);
    }

    public static DecisionStep Card(DecisionCardSource source, IEnumerable<uint> allowedCardIds)
    {
        ArgumentNullException.ThrowIfNull(allowedCardIds);
        var ids = allowedCardIds.Take(0x4000).ToArray();
        if (!Enum.IsDefined(source) || ids.Length == 0 || ids.Any(id => id is 0 or > 0x3fff) ||
            ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Supply a valid source and distinct game-local card IDs.");
        return new(DecisionKind.Card, source, ids, []);
    }

    public static DecisionStep Position(IEnumerable<BattlePosition> allowedPositions)
    {
        ArgumentNullException.ThrowIfNull(allowedPositions);
        var positions = allowedPositions.Take(4).ToArray();
        if (positions.Length == 0 || positions.Any(p => !Enum.IsDefined(p)) ||
            positions.Distinct().Count() != positions.Length)
            throw new ArgumentException("Supply distinct supported battle positions.", nameof(allowedPositions));
        return new(DecisionKind.Position, null, [], positions);
    }

    public static DecisionStep EmptyMonsterZone() => new(DecisionKind.MonsterZone, null, [], []);
}

/// <summary>
/// Explicit engine configuration for a selection-only workflow, not caller-supplied command data.
/// By default completion freezes the research slice without manufacturing a duel outcome.
/// </summary>
public sealed class DecisionSequence
{
    public string Id { get; }
    public IReadOnlyList<DecisionStep> Steps { get; }
    public bool StopAfterCompletion { get; }

    public DecisionSequence(string id, IEnumerable<DecisionStep> steps, bool stopAfterCompletion = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(steps);
        var snapshot = steps.Take(33).ToArray();
        if (snapshot.Length is 0 or > 32 || snapshot.Any(s => s is null))
            throw new ArgumentException("A decision sequence needs 1–32 nonnull steps.", nameof(steps));
        Id = id;
        Steps = Array.AsReadOnly(snapshot);
        StopAfterCompletion = stopAfterCompletion;
    }
}

public sealed record DecisionAnswer(DecisionKind Kind, int? CardInstanceId = null,
    BattlePosition? Position = null, int? MonsterZone = null);

/// <summary>Immutable full-information request; a presentation layer must restrict private card options.</summary>
public sealed class PendingDecision
{
    public long DecisionId { get; }
    public string SequenceId { get; }
    public PlayerId Player { get; }
    public int StepIndex { get; }
    public DecisionKind Kind { get; }
    public DecisionCardSource? CardSource { get; }
    public IReadOnlyList<CardInstance> CardOptions { get; }
    public IReadOnlyList<BattlePosition> PositionOptions { get; }
    public IReadOnlyList<int> MonsterZoneOptions { get; }
    public IReadOnlyList<DecisionAnswer> Answers { get; }

    internal PendingDecision(long id, DecisionSequence sequence, PlayerId player, int index,
        IEnumerable<CardInstance> cards, IEnumerable<int> zones, IEnumerable<DecisionAnswer> answers)
    {
        DecisionId = id;
        SequenceId = sequence.Id;
        Player = player;
        StepIndex = index;
        var step = sequence.Steps[index];
        Kind = step.Kind;
        CardSource = step.CardSource;
        CardOptions = Array.AsReadOnly(cards.ToArray());
        PositionOptions = Array.AsReadOnly(step.AllowedPositions.ToArray());
        MonsterZoneOptions = Array.AsReadOnly(zones.ToArray());
        Answers = Array.AsReadOnly(answers.ToArray());
    }
}

public sealed class DecisionSequenceResult
{
    public string SequenceId { get; }
    public PlayerId Player { get; }
    public long FinalDecisionId { get; }
    public IReadOnlyList<DecisionAnswer> Answers { get; }

    internal DecisionSequenceResult(PendingDecision pending, IEnumerable<DecisionAnswer> answers)
    {
        SequenceId = pending.SequenceId;
        Player = pending.Player;
        FinalDecisionId = pending.DecisionId;
        Answers = Array.AsReadOnly(answers.ToArray());
    }
}

public sealed record BeginDecisionSequence(PlayerId Player, string SequenceId) : DuelCommand(Player);
public abstract record DecisionCommand(PlayerId Player, long DecisionId) : DuelCommand(Player);
public sealed record ChooseDecisionCard(PlayerId Player, long DecisionId, int CardInstanceId) : DecisionCommand(Player, DecisionId);
public sealed record ChooseDecisionPosition(PlayerId Player, long DecisionId, BattlePosition Position) : DecisionCommand(Player, DecisionId);
public sealed record ChooseDecisionZone(PlayerId Player, long DecisionId, int MonsterZone) : DecisionCommand(Player, DecisionId);

public sealed record DecisionRequested(PendingDecision Decision) : DuelEvent;
public sealed record DecisionAnswered(PlayerId Player, string SequenceId, long DecisionId, DecisionAnswer Answer) : DuelEvent;
public sealed record DecisionSequenceCompleted(DecisionSequenceResult Result) : DuelEvent;
