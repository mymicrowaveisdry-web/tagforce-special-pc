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
    /// <summary>The Summon cannot be negated: it opens no Summon response window.</summary>
    public bool CannotBeNegated { get; init; }
    /// <summary>When it is Normal Summoned, no cards or effects can be activated in response.</summary>
    public bool NoResponses { get; init; }
    /// <summary>"If you control ...": field requirements that must all hold when the Summon is performed.</summary>
    public IReadOnlyList<ControlsMonstersCondition> Requirements { get; init; } = [];

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
