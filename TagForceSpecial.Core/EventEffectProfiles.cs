namespace TagForceSpecial.Core;

public enum MonsterEventKind { NormalSummoned, SpecialSummoned, FlipSummoned, Destroyed }
public enum EffectRelation { Self, Controller, Opponent, Any }
public enum TriggerRequirement { Mandatory, Optional }
public enum TriggerSourceLocation { FaceUpMonster, DestroyedSelf }
public enum TriggerOperation { GainLifePoints, ModifyEventMonsterUntilEndPhase }

/// <summary>Explicit opt-in to an if-style monster trigger. No card-text inference or timing exceptions.</summary>
public sealed class TriggerEffectProfile
{
    public string EffectId { get; }
    public uint CardId { get; }
    public MonsterEventKind EventKind { get; }
    public EffectRelation Relation { get; }
    public TriggerRequirement Requirement { get; }
    public TriggerSourceLocation SourceLocation { get; }
    public TriggerOperation Operation { get; }
    public int LifePointGain { get; }
    public int AttackDelta { get; }
    public int DefenseDelta { get; }

    public TriggerEffectProfile(string effectId, uint cardId, MonsterEventKind eventKind,
        EffectRelation relation, TriggerRequirement requirement, TriggerOperation operation,
        int lifePointGain = 0, int attackDelta = 0, int defenseDelta = 0,
        TriggerSourceLocation sourceLocation = TriggerSourceLocation.FaceUpMonster)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (!Enum.IsDefined(eventKind) || !Enum.IsDefined(relation) || !Enum.IsDefined(requirement) ||
            !Enum.IsDefined(operation) || !Enum.IsDefined(sourceLocation))
            throw new ArgumentException("Unknown trigger metadata.");
        if (sourceLocation == TriggerSourceLocation.DestroyedSelf &&
            (eventKind != MonsterEventKind.Destroyed || relation != EffectRelation.Self))
            throw new ArgumentException("Destroyed-self triggers require a self-destruction event.");
        if (operation == TriggerOperation.GainLifePoints
            ? lifePointGain <= 0 || attackDelta != 0 || defenseDelta != 0
            : lifePointGain != 0 || attackDelta == 0 && defenseDelta == 0 || eventKind == MonsterEventKind.Destroyed)
            throw new ArgumentException("Trigger payload does not match its operation.");
        EffectId = effectId;
        CardId = cardId;
        EventKind = eventKind;
        Relation = relation;
        Requirement = requirement;
        SourceLocation = sourceLocation;
        Operation = operation;
        LifePointGain = lifePointGain;
        AttackDelta = attackDelta;
        DefenseDelta = defenseDelta;
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
