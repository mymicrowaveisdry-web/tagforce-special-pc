namespace TagForceSpecial.Core;

/// <summary>Additive hand-level adjustment from each live, face-up, unnegated monster source.</summary>
public sealed class SummonLevelModifierProfile
{
    public string EffectId { get; }
    public uint SourceCardId { get; }
    public EffectRelation Relation { get; }
    public IReadOnlyList<uint> TargetCardIds { get; }
    public int LevelDelta { get; }

    public SummonLevelModifierProfile(string effectId, uint sourceCardId, EffectRelation relation,
        IEnumerable<uint> targetCardIds, int levelDelta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        ArgumentNullException.ThrowIfNull(targetCardIds);
        var ids = targetCardIds.ToArray();
        if (sourceCardId == 0 || relation is not (EffectRelation.Controller or EffectRelation.Opponent or EffectRelation.Any) ||
            ids.Length == 0 || ids.Any(id => id == 0) || ids.Distinct().Count() != ids.Length || levelDelta == 0)
            throw new ArgumentException("Supply an explicit monster source, hand relation, distinct targets and nonzero delta.");
        EffectId = effectId; SourceCardId = sourceCardId; Relation = relation;
        TargetCardIds = Array.AsReadOnly(ids); LevelDelta = levelDelta;
    }
}

/// <summary>Named alternative procedure with an exact Tribute count.</summary>
public sealed class TributeProcedureProfile
{
    public string ProcedureId { get; }
    public uint CardId { get; }
    public int TributeCount { get; }
    public IReadOnlyList<uint> AllowedTributeCardIds { get; }
    public bool RequireFaceUpTributes { get; }
    public bool AllowsSet { get; }

    public TributeProcedureProfile(string procedureId, uint cardId, int tributeCount,
        IEnumerable<uint>? allowedTributeCardIds = null, bool requireFaceUpTributes = false, bool allowsSet = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(procedureId);
        var ids = (allowedTributeCardIds ?? []).ToArray();
        if (cardId == 0 || tributeCount is < 0 or > 5 || ids.Any(id => id == 0) || ids.Distinct().Count() != ids.Length ||
            tributeCount == 0 && (ids.Length != 0 || requireFaceUpTributes))
            throw new ArgumentException("Supply a catalog target, zero to five tributes and a consistent donor policy.");
        ProcedureId = procedureId; CardId = cardId; TributeCount = tributeCount;
        AllowedTributeCardIds = Array.AsReadOnly(ids); RequireFaceUpTributes = requireFaceUpTributes; AllowsSet = allowsSet;
    }
}

/// <summary>Opt an eligible Normal or profiled Special Summon procedure into a pre-success window. Sets/effect resolution are excluded.</summary>
public sealed class SummonWindowProfile
{
    public uint CardId { get; }
    public SummonWindowProfile(uint cardId)
    {
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        CardId = cardId;
    }
}

[Flags]
public enum SummonNegationKinds { Normal = 1, Special = 2, All = 3 }

/// <summary>Earlier-turn set Counter Trap: fixed LP cost, negate and destroy an explicitly eligible pending summon.</summary>
public sealed class SummonNegationProfile
{
    public string EffectId { get; }
    public uint CardId { get; }
    public int LifePointCost { get; }
    public SummonNegationKinds Kinds { get; }
    public bool AllowsMultipleSummons { get; }
    public SummonNegationProfile(string effectId, uint cardId, int lifePointCost = 0,
        SummonNegationKinds kinds = SummonNegationKinds.Normal, bool allowsMultipleSummons = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        if (cardId == 0 || lifePointCost < 0 || kinds == 0 || (kinds & ~SummonNegationKinds.All) != 0)
            throw new ArgumentException("Invalid negation source, cost or summon kinds.");
        EffectId = effectId; CardId = cardId; LifePointCost = lifePointCost; Kinds = kinds;
        AllowsMultipleSummons = allowsMultipleSummons;
    }
}
