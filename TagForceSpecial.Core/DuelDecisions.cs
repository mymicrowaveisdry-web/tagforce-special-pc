using System.Text.Json.Serialization;

namespace TagForceSpecial.Core;

public enum DecisionKind { Card, Position, MonsterZone, SpellTrapTarget, FieldTarget }
public enum DecisionCardSource { Hand, Deck, Graveyard, Banished, ExtraDeck, FaceUpExtraDeck }

/// <summary>Trusted, immutable selection policy. It does not activate or resolve a card effect.</summary>
public sealed class DecisionStep
{
    public DecisionKind Kind { get; }
    public DecisionCardSource? CardSource { get; }
    public IReadOnlyList<uint> AllowedCardIds { get; }
    public IReadOnlyList<BattlePosition> AllowedPositions { get; }
    /// <summary>A card step that accepts any card of its source (the allowlist is then empty).</summary>
    public bool AllowsAnyCard { get; }

    private DecisionStep(DecisionKind kind, DecisionCardSource? source, uint[] ids, BattlePosition[] positions, bool anyCard = false)
    {
        Kind = kind;
        CardSource = source;
        AllowedCardIds = Array.AsReadOnly(ids);
        AllowedPositions = Array.AsReadOnly(positions);
        AllowsAnyCard = anyCard;
    }

    internal bool Allows(uint cardId) => AllowsAnyCard || AllowedCardIds.Contains(cardId);

    /// <summary>Any one card from the source (e.g. an activation's "discard 1 card" cost).</summary>
    public static DecisionStep AnyCard(DecisionCardSource source) => Enum.IsDefined(source)
        ? new(DecisionKind.Card, source, [], [], anyCard: true) : throw new ArgumentOutOfRangeException(nameof(source));

    public static DecisionStep Card(DecisionCardSource source, IEnumerable<uint> allowedCardIds)
    {
        ArgumentNullException.ThrowIfNull(allowedCardIds);
        var ids = allowedCardIds.ToArray();
        if (!Enum.IsDefined(source) || ids.Length == 0 || ids.Any(id => id == 0) ||
            ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Supply a valid source and distinct nonzero catalog card IDs.");
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

    /// <summary>The field, Graveyard or banished target this step chooses, distinct from earlier answers.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EffectTarget? FieldTargetSpec { get; private init; }
    /// <summary>An optional pick that <see cref="FinishDecision"/> may skip, ending the choices.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Optional { get; private init; }

    public static DecisionStep FieldTargetChoice(EffectTarget spec, bool optional = false) => spec is null ? throw new ArgumentNullException(nameof(spec))
        : new(DecisionKind.FieldTarget, null, [], []) { FieldTargetSpec = spec, Optional = optional };

    public static DecisionStep EmptyMonsterZone() => new(DecisionKind.MonsterZone, null, [], []);
    public static DecisionStep SpellTrapTarget() => new(DecisionKind.SpellTrapTarget, null, [], []);
}

/// <summary>Engine configuration for a selection-only workflow. Completion freezes the duel by default.</summary>
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
    BattlePosition? Position = null, int? MonsterZone = null, SpellTrapTarget? Target = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FieldCardTarget? FieldTarget = null);

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
    public IReadOnlyList<SpellTrapTarget> TargetOptions { get; }
    /// <summary>Field target options; null unless this is a field-target decision.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FieldCardTarget>? FieldTargetOptions { get; }
    /// <summary>The choices may end here with <see cref="FinishDecision"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CanFinish { get; }

    internal PendingDecision(long id, DecisionSequence sequence, PlayerId player, int index,
        IEnumerable<CardInstance> cards, IEnumerable<int> zones, IEnumerable<DecisionAnswer> answers,
        IEnumerable<SpellTrapTarget>? targets = null, IEnumerable<FieldCardTarget>? fieldTargets = null)
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
        TargetOptions = Array.AsReadOnly(targets?.ToArray() ?? []);
        FieldTargetOptions = fieldTargets is null ? null : Array.AsReadOnly(fieldTargets.ToArray());
        CanFinish = step.Optional;
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
public sealed record ChooseDecisionTarget(PlayerId Player, long DecisionId, PlayerId TargetPlayer,
    int SpellTrapZone, int CardInstanceId, bool IsPendulumZone = false) : DecisionCommand(Player, DecisionId);

public sealed record DecisionRequested(PendingDecision Decision) : DuelEvent;
public sealed record DecisionAnswered(PlayerId Player, string SequenceId, long DecisionId, DecisionAnswer Answer) : DuelEvent;
public sealed record DecisionSequenceCompleted(DecisionSequenceResult Result) : DuelEvent;
