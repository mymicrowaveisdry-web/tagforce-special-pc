namespace TagForceSpecial.Core;

/// <summary>One uninterrupted monster placement, independent of card/catalog identity.</summary>
public sealed record MonsterBinding(PlayerId Player, int Zone, CardInstance Card, long PlacementId)
{
    public bool IsStillPresent(DuelState state) =>
        Zone is >= 0 and < 5 && state.GetPlayer(Player).MonsterZones[Zone] is { } current &&
        current.Card == Card && current.PlacementId == PlacementId;
}
public sealed record EffectTimingPoint(long TransitionId, int Step);
public sealed record MonsterEventOccurrence(MonsterEventKind Kind, MonsterBinding Subject,
    EffectTimingPoint? TimingPoint = null, bool IsActivationCost = false);
public sealed record TriggerActivation(int Id, TriggerEffectProfile Profile, MonsterBinding Source,
    MonsterEventOccurrence Occurrence, CardInstance? DiscardedCost = null, int PaidLifePoints = 0,
    MonsterBinding? Target = null)
{
    public PlayerId Player => Source.Player;
}

/// <summary>All trigger groups are ordered before either player may add a fast response.</summary>
public sealed class PendingTriggerBatch
{
    public long DecisionId { get; }
    public IReadOnlyList<TriggerActivation> Remaining { get; }
    public IReadOnlyList<TriggerActivation> Ordered { get; }
    public IReadOnlyList<TriggerActivation> Options { get; }
    public IReadOnlyList<TriggerActivation> CommitQueue { get; }
    public PlayerId Player { get; }
    public bool Mandatory { get; }

    internal PendingTriggerBatch(long decisionId, PlayerId turnPlayer,
        IEnumerable<TriggerActivation> remaining, IEnumerable<TriggerActivation>? ordered = null,
        IEnumerable<TriggerActivation>? commitQueue = null)
    {
        DecisionId = decisionId;
        Remaining = Array.AsReadOnly(remaining.ToArray());
        Ordered = Array.AsReadOnly(ordered?.ToArray() ?? []);
        int Rank(TriggerActivation t) => (t.Profile.Requirement == TriggerRequirement.Mandatory ? 0 : 2)
            + (t.Player == turnPlayer ? 0 : 1);
        CommitQueue = Array.AsReadOnly(commitQueue?.ToArray() ?? []);
        int rank = Remaining.Count == 0 ? -1 : Remaining.Min(Rank);
        Options = Array.AsReadOnly(CommitQueue.Count != 0 ? [] : Remaining.Where(t => Rank(t) == rank).ToArray());
        var first = CommitQueue.FirstOrDefault() ?? Options.FirstOrDefault() ?? Ordered.Last();
        Player = first.Player;
        Mandatory = first.Profile.Requirement == TriggerRequirement.Mandatory;
    }
}

public sealed record StatModifier(string EffectId, MonsterBinding? Source, MonsterBinding Target,
    int AttackDelta, int DefenseDelta, long? ResolutionId = null, int? ExpiresOnTurn = null, int ActionOrdinal = 0, long? ExpiresAfterAttackId = null, SpellTrapTarget? SpellTrapSource = null);
public sealed record EffectiveMonsterStatistics(MonsterBinding Monster, int? Attack, int? Defense);

public sealed record TriggerUse(PlayerId Player, int Turn, string Key, int? InstanceId, long? PlacementId);
public sealed record MonsterEffectNegation(string EffectId, MonsterBinding Source, MonsterBinding Target,
    long ResolutionId, int ExpiresOnTurn, int ActionOrdinal = 0);
public enum TriggerCommitmentKind { DiscardCost, MonsterTarget }
public sealed class PendingTriggerCommitment
{
    public long DecisionId { get; }
    public TriggerActivation Trigger { get; }
    public PlayerId Player => Trigger.Player;
    public TriggerCommitmentKind Kind { get; }
    public IReadOnlyList<CardInstance> CardOptions { get; }
    public IReadOnlyList<MonsterBinding> TargetOptions { get; }
    internal PendingTriggerCommitment(long id, TriggerActivation trigger, TriggerCommitmentKind kind,
        IEnumerable<CardInstance>? cards = null, IEnumerable<MonsterBinding>? targets = null)
    {
        DecisionId = id; Trigger = trigger; Kind = kind;
        CardOptions = Array.AsReadOnly(cards?.ToArray() ?? []);
        TargetOptions = Array.AsReadOnly(targets?.ToArray() ?? []);
    }
}

/// <summary>Immutable event-effect state retained across decisions, chains and snapshots.</summary>
public sealed record EventEffectState
{
    public PendingTriggerBatch? PendingTriggers { get; private init; }
    public PendingTriggerCommitment? PendingCommitment { get; private init; }
    public IReadOnlyList<TriggerActivation> DeferredTriggers { get; private init; } = Array.AsReadOnly(Array.Empty<TriggerActivation>());
    public IReadOnlyList<StatModifier> TimedModifiers { get; private init; } = Array.AsReadOnly(Array.Empty<StatModifier>());
    public IReadOnlyList<StatModifier> AppliedModifiers { get; private init; } = Array.AsReadOnly(Array.Empty<StatModifier>());
    public IReadOnlyList<EffectiveMonsterStatistics> Statistics { get; private init; } = Array.AsReadOnly(Array.Empty<EffectiveMonsterStatistics>());
    public IReadOnlyList<TriggerUse> UsedTriggers { get; private init; } = Array.AsReadOnly(Array.Empty<TriggerUse>());
    public IReadOnlyList<MonsterEffectNegation> TimedNegations { get; private init; } = Array.AsReadOnly(Array.Empty<MonsterEffectNegation>());
    public IReadOnlyList<MonsterBinding> NegatedMonsters { get; private init; } = Array.AsReadOnly(Array.Empty<MonsterBinding>());
    public EffectTimingPoint? LastTimingPoint { get; private init; }

    internal EventEffectState WithTriggers(PendingTriggerBatch? pending, PendingTriggerCommitment? commitment = null) =>
        this with { PendingTriggers = pending, PendingCommitment = commitment };
    internal EventEffectState WithDeferred(IEnumerable<TriggerActivation> deferred, EffectTimingPoint? last) =>
        this with { DeferredTriggers = Array.AsReadOnly(deferred.ToArray()), LastTimingPoint = last };
    internal EventEffectState WithModifiers(IEnumerable<StatModifier> timed, IEnumerable<StatModifier> applied,
        IEnumerable<EffectiveMonsterStatistics> statistics) => this with {
            TimedModifiers = Array.AsReadOnly(timed.ToArray()), AppliedModifiers = Array.AsReadOnly(applied.ToArray()),
            Statistics = Array.AsReadOnly(statistics.ToArray()) };
    internal EventEffectState WithUses(IEnumerable<TriggerUse> uses) => this with { UsedTriggers = Array.AsReadOnly(uses.ToArray()) };
    internal EventEffectState WithNegations(IEnumerable<MonsterEffectNegation> timed, IEnumerable<MonsterBinding> negated) => this with {
        TimedNegations = Array.AsReadOnly(timed.ToArray()), NegatedMonsters = Array.AsReadOnly(negated.ToArray()) };
}

public sealed record ChooseTriggerCostCard(PlayerId Player, long DecisionId, int CardInstanceId) : DuelCommand(Player);
public sealed record ChooseTriggerMonsterTarget(PlayerId Player, long DecisionId, PlayerId TargetPlayer,
    int MonsterZone, int CardInstanceId) : DuelCommand(Player);
public sealed record TriggerCommitmentRequested(PendingTriggerCommitment Commitment) : DuelEvent;
public sealed record TriggerCostPaid(TriggerActivation Trigger, CardInstance? DiscardedCard, int LifePoints) : DuelEvent;
public sealed record TriggerTargetChosen(TriggerActivation Trigger, MonsterBinding Target) : DuelEvent;
public sealed record TriggerLimitConsumed(TriggerUse Use) : DuelEvent;
public sealed record TriggerMissedTiming(TriggerActivation Trigger) : DuelEvent;
public sealed record MonsterModifierCreated(StatModifier Modifier) : DuelEvent;
public sealed record MonsterEffectsNegated(MonsterEffectNegation Negation) : DuelEvent;
public sealed record MonsterEffectNegationEnded(MonsterEffectNegation Negation) : DuelEvent;

/// <summary>Order every mandatory option, or an ordered subset of optional options (empty declines all).</summary>
public sealed record ChooseTriggerOrder(PlayerId Player, long DecisionId, IReadOnlyList<int> TriggerIds) : DuelCommand(Player);
public sealed record TriggerOrderRequested(PendingTriggerBatch Batch) : DuelEvent;
public sealed record TriggerOrderChosen(PlayerId Player, IReadOnlyList<int> TriggerIds) : DuelEvent;
public sealed record TriggerActivated(TriggerActivation Trigger, int LinkNumber) : DuelEvent;
public sealed record LifePointsGained(PlayerId Player, int Amount, int Before, int After, string EffectId) : DuelEvent;
public sealed record StatModifierApplied(StatModifier Modifier) : DuelEvent;
public enum ModifierRemovalReason { ConditionNoLongerMet, EndPhaseReached, DamageStepEnded }
public sealed record StatModifierRemoved(StatModifier Modifier, ModifierRemovalReason Reason) : DuelEvent;

public sealed record TriggerUnavailable(TriggerActivation Trigger) : DuelEvent;
public sealed record TriggerSubjectUnavailable(TriggerActivation Trigger) : DuelEvent;
