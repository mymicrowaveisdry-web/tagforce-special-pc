namespace TagForceSpecial.Core;

/// <summary>One uninterrupted monster placement, independent of card/catalog identity.</summary>
public sealed record MonsterBinding(PlayerId Player, int Zone, CardInstance Card, long PlacementId)
{
    public bool IsStillPresent(DuelState state) =>
        state.GetPlayer(Player).MonsterZones[Zone] is { } current &&
        current.Card == Card && current.PlacementId == PlacementId;
}
public sealed record MonsterEventOccurrence(MonsterEventKind Kind, MonsterBinding Subject);
public sealed record TriggerActivation(int Id, TriggerEffectProfile Profile, MonsterBinding Source,
    MonsterEventOccurrence Occurrence)
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
    public PlayerId Player { get; }
    public bool Mandatory { get; }

    internal PendingTriggerBatch(long decisionId, PlayerId turnPlayer,
        IEnumerable<TriggerActivation> remaining, IEnumerable<TriggerActivation>? ordered = null)
    {
        DecisionId = decisionId;
        Remaining = Array.AsReadOnly(remaining.ToArray());
        Ordered = Array.AsReadOnly(ordered?.ToArray() ?? []);
        int Rank(TriggerActivation t) => (t.Profile.Requirement == TriggerRequirement.Mandatory ? 0 : 2)
            + (t.Player == turnPlayer ? 0 : 1);
        int rank = Remaining.Min(Rank);
        Options = Array.AsReadOnly(Remaining.Where(t => Rank(t) == rank).ToArray());
        Player = Options[0].Player;
        Mandatory = Options[0].Profile.Requirement == TriggerRequirement.Mandatory;
    }
}

public sealed record StatModifier(string EffectId, MonsterBinding Source, MonsterBinding Target,
    int AttackDelta, int DefenseDelta, long? ResolutionId = null, int? ExpiresOnTurn = null);
public sealed record EffectiveMonsterStatistics(MonsterBinding Monster, int? Attack, int? Defense);

/// <summary>Immutable event-effect state retained across decisions, chains and snapshots.</summary>
public sealed class EventEffectState
{
    public PendingTriggerBatch? PendingTriggers { get; }
    public IReadOnlyList<TriggerActivation> DeferredTriggers { get; }
    public IReadOnlyList<StatModifier> TimedModifiers { get; }
    public IReadOnlyList<StatModifier> AppliedModifiers { get; }
    public IReadOnlyList<EffectiveMonsterStatistics> Statistics { get; }

    internal EventEffectState(PendingTriggerBatch? pendingTriggers = null,
        IEnumerable<TriggerActivation>? deferredTriggers = null, IEnumerable<StatModifier>? timedModifiers = null,
        IEnumerable<StatModifier>? appliedModifiers = null, IEnumerable<EffectiveMonsterStatistics>? statistics = null)
    {
        PendingTriggers = pendingTriggers;
        DeferredTriggers = Array.AsReadOnly(deferredTriggers?.ToArray() ?? []);
        TimedModifiers = Array.AsReadOnly(timedModifiers?.ToArray() ?? []);
        AppliedModifiers = Array.AsReadOnly(appliedModifiers?.ToArray() ?? []);
        Statistics = Array.AsReadOnly(statistics?.ToArray() ?? []);
    }
}

/// <summary>Order every mandatory option, or an ordered subset of optional options (empty declines all).</summary>
public sealed record ChooseTriggerOrder(PlayerId Player, long DecisionId, IReadOnlyList<int> TriggerIds) : DuelCommand(Player);
public sealed record TriggerOrderRequested(PendingTriggerBatch Batch) : DuelEvent;
public sealed record TriggerOrderChosen(PlayerId Player, IReadOnlyList<int> TriggerIds) : DuelEvent;
public sealed record TriggerActivated(TriggerActivation Trigger, int LinkNumber) : DuelEvent;
public sealed record LifePointsGained(PlayerId Player, int Amount, int Before, int After, string EffectId) : DuelEvent;
public sealed record StatModifierApplied(StatModifier Modifier) : DuelEvent;
public enum ModifierRemovalReason { ConditionNoLongerMet, EndPhaseReached }
public sealed record StatModifierRemoved(StatModifier Modifier, ModifierRemovalReason Reason) : DuelEvent;

public sealed record TriggerUnavailable(TriggerActivation Trigger) : DuelEvent;
public sealed record TriggerSubjectUnavailable(TriggerActivation Trigger) : DuelEvent;
