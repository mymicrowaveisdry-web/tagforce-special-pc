namespace TagForceSpecial.Core;

public enum MonsterEventKind { NormalSummoned, SpecialSummoned, FlipSummoned, Destroyed, SentToGraveyard, FlippedFaceUp, TributedForSummon,
    /// <summary>The End Phase of a turn this face-up monster was Summoned or flipped face-up.</summary>
    EndPhaseAfterSummonOrFlip,
    /// <summary>This monster destroyed an opponent's monster by battle and sent it to the Graveyard.</summary>
    DestroyedOpponentByBattle,
    /// <summary>A monster declared an attack; the attacker is the subject.</summary>
    AttackDeclared,
    /// <summary>A Standby Phase began; the turn player is the subject's player. Only face-up Spells and Traps use it.</summary>
    StandbyPhase,
    /// <summary>This monster inflicted battle damage to its controller's opponent.</summary>
    InflictedBattleDamage,
    /// <summary>An End Phase began; like <see cref="StandbyPhase"/>, only face-up Spells, Traps and scales use it.</summary>
    EndPhase,
    /// <summary>Control of this face-up monster changed; the subject is the monster in its new place.</summary>
    ControlChanged,
    /// <summary>This monster was Tributed, for a Summon or as a cost; only its Graveyard trigger uses it.</summary>
    Tributed,
    /// <summary>A face-up Normal Summon completed with at least one Tributed monster.</summary>
    TributeSummoned }
/// <summary>
/// Narrows a Destroyed or SentToGraveyard event by where it came from or what caused it; <see cref="FromOwnField"/> is "this card you
/// control is sent to your Graveyard", from a field zone while its owner controlled it.
/// </summary>
public enum TriggerEventCause { Any, FromField, ByBattle, FromOwnField, ByDiscardEffect, ByOpponentDiscardEffect }
public enum EffectRelation { Self, Controller, Opponent, Any }
public enum TriggerRequirement { Mandatory, Optional }
public enum TriggerSourceLocation { FaceUpMonster, DestroyedSelf, GraveyardSelf, FaceUpSpellTrap, PendulumZone,
    /// <summary>The card is in a player's hand; the event is relative to that player.</summary>
    Hand }
public enum TriggerTiming { If, When }
public enum TriggerTurnLimit { None, PerPlacement, SharedPerPlayer, SharedPerDuel,
    /// <summary>Once while this card stays face-up on the field, whoever controls it.</summary>
    WhileFaceUp }
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
    /// <summary>Face-down monsters can be chosen.</summary>
    public bool AllowFaceDown { get; }
    public MonsterTargetPolicy(EffectRelation relation, IEnumerable<uint>? allowedCardIds = null,
        bool excludeSource = false, bool requireFaceUpAtResolution = true, bool allowFaceDown = false)
    {
        if (!Enum.IsDefined(relation)) throw new ArgumentOutOfRangeException(nameof(relation));
        var ids = (allowedCardIds ?? []).ToArray();
        if (ids.Any(id => id == 0) || ids.Distinct().Count() != ids.Length) throw new ArgumentException("Invalid target catalog IDs.");
        Relation = relation; AllowedCardIds = Array.AsReadOnly(ids);
        ExcludeSource = excludeSource; RequireFaceUpAtResolution = requireFaceUpAtResolution; AllowFaceDown = allowFaceDown;
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
    public bool? DamageStepAllowed { get; }
    public TriggerTurnLimit TurnLimit { get; }
    public string LimitKey { get; }
    public int LifePointCost { get; }
    public int DetachMaterialCount { get; }
    public IReadOnlyList<uint> DiscardCardIds { get; }
    public MonsterTargetPolicy? Target { get; }
    public IReadOnlyList<TriggerEffectStep> Steps { get; }
    /// <summary>When set, the link resolves through the shared definition steps instead of <see cref="Steps"/>.</summary>
    public EffectDefinition? Definition { get; }
    public TriggerEventCause Cause { get; }
    /// <summary>The source must be a Gemini monster that has gained its effects.</summary>
    public bool RequiresGeminiActive { get; }
    /// <summary>Activating this trigger costs Tributing 1 monster its controller chooses.</summary>
    public bool TributeCost { get; init; }
    /// <summary>When set, a Special Summon only triggers this if that procedure performed it.</summary>
    public string? RequiredProcedureId { get; init; }
    /// <summary>When set, only an event whose subject matches triggers this.</summary>
    public CardFilter? SubjectFilter { get; init; }
    /// <summary>An attack declaration triggers this only if it is a direct attack.</summary>
    public bool DirectAttackOnly { get; init; }

    /// <summary>A trigger whose resolution is a <see cref="EffectKind.MonsterTrigger"/> definition.</summary>
    public TriggerEffectProfile(EffectDefinition definition, MonsterEventKind eventKind, EffectRelation relation, TriggerRequirement requirement,
        TriggerSourceLocation sourceLocation = TriggerSourceLocation.FaceUpMonster, TriggerTiming timing = TriggerTiming.If,
        TriggerEventCause cause = TriggerEventCause.Any, TriggerTurnLimit turnLimit = TriggerTurnLimit.None, string? limitKey = null,
        bool? damageStepAllowed = null, int lifePointCost = 0, IEnumerable<uint>? discardCardIds = null, MonsterTargetPolicy? target = null,
        int detachMaterialCount = 0, bool requiresGeminiActive = false)
        : this(definition ?? throw new ArgumentNullException(nameof(definition)), cause, definition.EffectId, definition.CardId, eventKind, relation, requirement,
            Array.Empty<TriggerEffectStep>(), sourceLocation, timing, turnLimit, limitKey, lifePointCost, discardCardIds, target, damageStepAllowed, detachMaterialCount, requiresGeminiActive) { }

    public TriggerEffectProfile(string effectId, uint cardId, MonsterEventKind eventKind,
        EffectRelation relation, TriggerRequirement requirement, TriggerOperation operation,
        int lifePointGain = 0, int attackDelta = 0, int defenseDelta = 0,
        TriggerSourceLocation sourceLocation = TriggerSourceLocation.FaceUpMonster,
        TriggerTiming timing = TriggerTiming.If, TriggerTurnLimit turnLimit = TriggerTurnLimit.None,
        string? limitKey = null, int lifePointCost = 0, IEnumerable<uint>? discardCardIds = null,
        MonsterTargetPolicy? target = null, bool? damageStepAllowed = null, int detachMaterialCount = 0)
        : this(effectId, cardId, eventKind, relation, requirement,
            [new TriggerEffectStep([new TriggerEffectAction(operation, lifePointGain, attackDelta, defenseDelta)])],
            sourceLocation, timing, turnLimit, limitKey, lifePointCost, discardCardIds, target, damageStepAllowed, detachMaterialCount) { }

    public TriggerEffectProfile(string effectId, uint cardId, MonsterEventKind eventKind,
        EffectRelation relation, TriggerRequirement requirement, IEnumerable<TriggerEffectStep> steps,
        TriggerSourceLocation sourceLocation = TriggerSourceLocation.FaceUpMonster,
        TriggerTiming timing = TriggerTiming.If, TriggerTurnLimit turnLimit = TriggerTurnLimit.None,
        string? limitKey = null, int lifePointCost = 0, IEnumerable<uint>? discardCardIds = null,
        MonsterTargetPolicy? target = null, bool? damageStepAllowed = null, int detachMaterialCount = 0)
        : this(null, TriggerEventCause.Any, effectId, cardId, eventKind, relation, requirement, steps, sourceLocation, timing, turnLimit,
            limitKey, lifePointCost, discardCardIds, target, damageStepAllowed, detachMaterialCount, false) { }

    private TriggerEffectProfile(EffectDefinition? definition, TriggerEventCause cause, string effectId, uint cardId, MonsterEventKind eventKind,
        EffectRelation relation, TriggerRequirement requirement, IEnumerable<TriggerEffectStep> steps,
        TriggerSourceLocation sourceLocation, TriggerTiming timing, TriggerTurnLimit turnLimit, string? limitKey, int lifePointCost,
        IEnumerable<uint>? discardCardIds, MonsterTargetPolicy? target, bool? damageStepAllowed, int detachMaterialCount, bool requiresGeminiActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        ArgumentNullException.ThrowIfNull(steps);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (!Enum.IsDefined(eventKind) || !Enum.IsDefined(relation) || !Enum.IsDefined(requirement) ||
            !Enum.IsDefined(sourceLocation) || !Enum.IsDefined(timing) || !Enum.IsDefined(turnLimit) || !Enum.IsDefined(cause))
            throw new ArgumentException("Unknown trigger metadata.");
        if (sourceLocation == TriggerSourceLocation.DestroyedSelf && (eventKind != MonsterEventKind.Destroyed || relation != EffectRelation.Self) ||
            sourceLocation == TriggerSourceLocation.GraveyardSelf && (eventKind is not (MonsterEventKind.SentToGraveyard or MonsterEventKind.TributedForSummon or MonsterEventKind.Tributed) || relation != EffectRelation.Self) ||
            eventKind is MonsterEventKind.TributedForSummon or MonsterEventKind.Tributed && sourceLocation != TriggerSourceLocation.GraveyardSelf)
            throw new ArgumentException("Graveyard sources require the matching self event.");
        if (eventKind is MonsterEventKind.EndPhaseAfterSummonOrFlip or MonsterEventKind.DestroyedOpponentByBattle or MonsterEventKind.InflictedBattleDamage or MonsterEventKind.ControlChanged && (sourceLocation != TriggerSourceLocation.FaceUpMonster || relation != EffectRelation.Self) ||
            requiresGeminiActive && sourceLocation != TriggerSourceLocation.FaceUpMonster)
            throw new ArgumentException("This trigger needs its face-up source.");
        // A face-up monster's own Standby or End Phase effect is the only monster use of these events.
        if (eventKind is MonsterEventKind.StandbyPhase or MonsterEventKind.EndPhase &&
            (sourceLocation is TriggerSourceLocation.FaceUpSpellTrap or TriggerSourceLocation.PendulumZone ? relation == EffectRelation.Self
                : !(sourceLocation == TriggerSourceLocation.FaceUpMonster && relation == EffectRelation.Self)))
            throw new ArgumentException("A phase trigger belongs to a face-up card and is about that card.");
        if (cause != TriggerEventCause.Any && eventKind is not (MonsterEventKind.Destroyed or MonsterEventKind.SentToGraveyard))
            throw new ArgumentException("Only destruction and Graveyard events have a cause.");
        if (definition is not null && definition.Kind != EffectKind.MonsterTrigger)
            throw new ArgumentException("A trigger definition must be a monster trigger.", nameof(definition));
        if (lifePointCost < 0 || limitKey is not null && (turnLimit == TriggerTurnLimit.None || string.IsNullOrWhiteSpace(limitKey)))
            throw new ArgumentException("Invalid activation cost or turn-limit key.");
        if (sourceLocation == TriggerSourceLocation.Hand && (definition is null || relation == EffectRelation.Self))
            throw new ArgumentException("A hand trigger needs a definition and an event relative to its player.");
        if (turnLimit == TriggerTurnLimit.WhileFaceUp && sourceLocation != TriggerSourceLocation.FaceUpMonster)
            throw new ArgumentException("A once-while-face-up limit needs a face-up monster source.");
        if (detachMaterialCount is < 0 or > 5 || detachMaterialCount != 0 && sourceLocation != TriggerSourceLocation.FaceUpMonster)
            throw new ArgumentException("Detach costs require a field source and one to five materials.");
        var discards = discardCardIds is null ? [] : DecisionStep.Card(DecisionCardSource.Hand, discardCardIds).AllowedCardIds.ToArray();
        var copy = steps.ToArray();
        if ((copy.Length == 0) != (definition is not null) || copy.Any(s => s is null) || copy.Length != 0 && copy[0].RequiresPreviousStepSuccess)
            throw new ArgumentException("A trigger requires nonnull resolution steps or a definition.");
        var actions = copy.SelectMany(s => s.Actions).ToArray();
        bool targeted = actions.Any(a => a.Operation is TriggerOperation.ModifyTargetMonsterUntilEndPhase or
            TriggerOperation.DestroyTargetMonster or TriggerOperation.NegateTargetMonsterUntilEndPhase);
        if ((targeted || definition is { FieldTargets: [TriggerTargetedMonster] }) != (target is not null) || actions.Any(a => a.Operation == TriggerOperation.ModifyEventMonsterUntilEndPhase &&
            eventKind is MonsterEventKind.Destroyed or MonsterEventKind.SentToGraveyard))
            throw new ArgumentException("Target policy and resolution operations do not agree.");
        EffectId = effectId; CardId = cardId; EventKind = eventKind; Relation = relation;
        Requirement = requirement; SourceLocation = sourceLocation; Timing = timing; DamageStepAllowed = damageStepAllowed;
        TurnLimit = turnLimit; LimitKey = limitKey ?? effectId; LifePointCost = lifePointCost;
        DetachMaterialCount = detachMaterialCount;
        DiscardCardIds = Array.AsReadOnly(discards); Target = target; Steps = Array.AsReadOnly(copy);
        Definition = definition; Cause = cause; RequiresGeminiActive = requiresGeminiActive;
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
    /// <summary>When set, only matching monsters are affected.</summary>
    public CardFilter? Filter { get; init; }

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
