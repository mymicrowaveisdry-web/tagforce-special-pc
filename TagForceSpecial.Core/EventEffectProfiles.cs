namespace TagForceSpecial.Core;

public enum MonsterEventKind { NormalSummoned, SpecialSummoned, FlipSummoned, Destroyed, SentToGraveyard }
public enum EffectRelation { Self, Controller, Opponent, Any }
public enum TriggerRequirement { Mandatory, Optional }
public enum TriggerSourceLocation { FaceUpMonster, DestroyedSelf, GraveyardSelf }
public enum TriggerTiming { If, When }
public enum TriggerTurnLimit { None, PerPlacement, SharedPerPlayer }
public enum TriggerOperation
{
    GainLifePoints, ModifyEventMonsterUntilEndPhase, ModifyTargetMonsterUntilEndPhase,
    DestroyTargetMonster, NegateTargetMonsterUntilEndPhase
}

/// <summary>One reusable resolution action. Steps declare sequential versus simultaneous timing.</summary>
public sealed class TriggerEffectAction
{
    public TriggerOperation Operation { get; }
    public int LifePointGain { get; }
    public int AttackDelta { get; }
    public int DefenseDelta { get; }
    public bool RequiresPreviousActionSuccess { get; }
    public TriggerEffectAction(TriggerOperation operation, int lifePointGain = 0, int attackDelta = 0, int defenseDelta = 0,
        bool requiresPreviousActionSuccess = false)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        bool valid = operation switch
        {
            TriggerOperation.GainLifePoints => lifePointGain > 0 && attackDelta == 0 && defenseDelta == 0,
            TriggerOperation.ModifyEventMonsterUntilEndPhase or TriggerOperation.ModifyTargetMonsterUntilEndPhase =>
                lifePointGain == 0 && (attackDelta != 0 || defenseDelta != 0),
            _ => lifePointGain == 0 && attackDelta == 0 && defenseDelta == 0
        };
        if (!valid) throw new ArgumentException("Action payload does not match its operation.");
        Operation = operation; LifePointGain = lifePointGain; AttackDelta = attackDelta; DefenseDelta = defenseDelta;
        RequiresPreviousActionSuccess = requiresPreviousActionSuccess;
    }
}

/// <summary>All actions in a step are simultaneous for timing. Successive steps happen in sequence.</summary>
public sealed class TriggerEffectStep
{
    public IReadOnlyList<TriggerEffectAction> Actions { get; }
    public bool RequiresPreviousStepSuccess { get; }
    public TriggerEffectStep(IEnumerable<TriggerEffectAction> actions, bool requiresPreviousStepSuccess = false)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var copy = actions.ToArray();
        if (copy.Length == 0 || copy.Any(a => a is null) || copy[0].RequiresPreviousActionSuccess) throw new ArgumentException("A step requires nonnull actions.", nameof(actions));
        Actions = Array.AsReadOnly(copy);
        RequiresPreviousStepSuccess = requiresPreviousStepSuccess;
    }
}

public sealed class MonsterTargetPolicy
{
    public EffectRelation Relation { get; }
    public IReadOnlyList<uint> AllowedCardIds { get; }
    public bool ExcludeSource { get; }
    public bool RequireFaceUpAtResolution { get; }
    public MonsterTargetPolicy(EffectRelation relation, IEnumerable<uint>? allowedCardIds = null,
        bool excludeSource = false, bool requireFaceUpAtResolution = true)
    {
        if (!Enum.IsDefined(relation)) throw new ArgumentOutOfRangeException(nameof(relation));
        var ids = (allowedCardIds ?? []).ToArray();
        if (ids.Any(id => id == 0) || ids.Distinct().Count() != ids.Length) throw new ArgumentException("Invalid target catalog IDs.");
        Relation = relation; AllowedCardIds = Array.AsReadOnly(ids);
        ExcludeSource = excludeSource; RequireFaceUpAtResolution = requireFaceUpAtResolution;
    }
}

/// <summary>Explicit trigger, activation commitments and ordered resolution; no card-text inference.</summary>
public sealed class TriggerEffectProfile
{
    public string EffectId { get; }
    public uint CardId { get; }
    public MonsterEventKind EventKind { get; }
    public EffectRelation Relation { get; }
    public TriggerRequirement Requirement { get; }
    public TriggerSourceLocation SourceLocation { get; }
    public TriggerTiming Timing { get; }
    public TriggerTurnLimit TurnLimit { get; }
    public string LimitKey { get; }
    public int LifePointCost { get; }
    public IReadOnlyList<uint> DiscardCardIds { get; }
    public MonsterTargetPolicy? Target { get; }
    public IReadOnlyList<TriggerEffectStep> Steps { get; }

    public TriggerEffectProfile(string effectId, uint cardId, MonsterEventKind eventKind,
        EffectRelation relation, TriggerRequirement requirement, TriggerOperation operation,
        int lifePointGain = 0, int attackDelta = 0, int defenseDelta = 0,
        TriggerSourceLocation sourceLocation = TriggerSourceLocation.FaceUpMonster,
        TriggerTiming timing = TriggerTiming.If, TriggerTurnLimit turnLimit = TriggerTurnLimit.None,
        string? limitKey = null, int lifePointCost = 0, IEnumerable<uint>? discardCardIds = null,
        MonsterTargetPolicy? target = null)
        : this(effectId, cardId, eventKind, relation, requirement,
            [new TriggerEffectStep([new TriggerEffectAction(operation, lifePointGain, attackDelta, defenseDelta)])],
            sourceLocation, timing, turnLimit, limitKey, lifePointCost, discardCardIds, target) { }

    public TriggerEffectProfile(string effectId, uint cardId, MonsterEventKind eventKind,
        EffectRelation relation, TriggerRequirement requirement, IEnumerable<TriggerEffectStep> steps,
        TriggerSourceLocation sourceLocation = TriggerSourceLocation.FaceUpMonster,
        TriggerTiming timing = TriggerTiming.If, TriggerTurnLimit turnLimit = TriggerTurnLimit.None,
        string? limitKey = null, int lifePointCost = 0, IEnumerable<uint>? discardCardIds = null,
        MonsterTargetPolicy? target = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        ArgumentNullException.ThrowIfNull(steps);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (!Enum.IsDefined(eventKind) || !Enum.IsDefined(relation) || !Enum.IsDefined(requirement) ||
            !Enum.IsDefined(sourceLocation) || !Enum.IsDefined(timing) || !Enum.IsDefined(turnLimit))
            throw new ArgumentException("Unknown trigger metadata.");
        if (sourceLocation == TriggerSourceLocation.DestroyedSelf && (eventKind != MonsterEventKind.Destroyed || relation != EffectRelation.Self) ||
            sourceLocation == TriggerSourceLocation.GraveyardSelf && (eventKind != MonsterEventKind.SentToGraveyard || relation != EffectRelation.Self))
            throw new ArgumentException("Graveyard sources require the matching self event.");
        if (lifePointCost < 0 || limitKey is not null && (turnLimit == TriggerTurnLimit.None || string.IsNullOrWhiteSpace(limitKey)))
            throw new ArgumentException("Invalid activation cost or turn-limit key.");
        var discards = discardCardIds is null ? [] : DecisionStep.Card(DecisionCardSource.Hand, discardCardIds).AllowedCardIds.ToArray();
        var copy = steps.ToArray();
        if (copy.Length == 0 || copy.Any(s => s is null) || copy[0].RequiresPreviousStepSuccess) throw new ArgumentException("A trigger requires nonnull resolution steps.");
        var actions = copy.SelectMany(s => s.Actions).ToArray();
        bool targeted = actions.Any(a => a.Operation is TriggerOperation.ModifyTargetMonsterUntilEndPhase or
            TriggerOperation.DestroyTargetMonster or TriggerOperation.NegateTargetMonsterUntilEndPhase);
        if (targeted != (target is not null) || actions.Any(a => a.Operation == TriggerOperation.ModifyEventMonsterUntilEndPhase &&
            eventKind is MonsterEventKind.Destroyed or MonsterEventKind.SentToGraveyard))
            throw new ArgumentException("Target policy and resolution operations do not agree.");
        EffectId = effectId; CardId = cardId; EventKind = eventKind; Relation = relation;
        Requirement = requirement; SourceLocation = sourceLocation; Timing = timing;
        TurnLimit = turnLimit; LimitKey = limitKey ?? effectId; LifePointCost = lifePointCost;
        DiscardCardIds = Array.AsReadOnly(discards); Target = target; Steps = Array.AsReadOnly(copy);
    }
}

/// <summary>Scoped face-up monster negation aura. Explicit target IDs keep dependencies reviewable.</summary>
public sealed class ContinuousNegationProfile
{
    public string EffectId { get; }
    public uint CardId { get; }
    public EffectRelation Affects { get; }
    public IReadOnlyList<uint> TargetCardIds { get; }
    public ContinuousNegationProfile(string effectId, uint cardId, EffectRelation affects, IEnumerable<uint> targetCardIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (!Enum.IsDefined(affects)) throw new ArgumentOutOfRangeException(nameof(affects));
        ArgumentNullException.ThrowIfNull(targetCardIds);
        var ids = targetCardIds.ToArray();
        if (ids.Length == 0 || ids.Any(id => id == 0) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Negation requires distinct target catalog IDs.");
        EffectId = effectId; CardId = cardId; Affects = affects; TargetCardIds = Array.AsReadOnly(ids);
    }
}

/// <summary>Additive ATK/DEF aura from one face-up monster placement; never starts a chain.</summary>
public sealed class ContinuousEffectProfile
{
    public string EffectId { get; }
    public uint CardId { get; }
    public EffectRelation Affects { get; }
    public int AttackDelta { get; }
    public int DefenseDelta { get; }

    public ContinuousEffectProfile(string effectId, uint cardId, EffectRelation affects, int attackDelta, int defenseDelta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (!Enum.IsDefined(affects)) throw new ArgumentOutOfRangeException(nameof(affects));
        if (attackDelta == 0 && defenseDelta == 0) throw new ArgumentException("An aura must change a statistic.");
        EffectId = effectId;
        CardId = cardId;
        Affects = affects;
        AttackDelta = attackDelta;
        DefenseDelta = defenseDelta;
    }
}
