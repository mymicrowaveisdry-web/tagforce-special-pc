namespace TagForceSpecial.Core;

public enum MaterialDestination { Graveyard, Banished, Attached }

/// <summary>Explicit generic procedure; it does not infer a Synchro, Xyz or other card family.</summary>
public sealed class ExtraDeckMaterialProcedureProfile
{
    public string ProcedureId { get; }
    public IReadOnlyList<uint> TargetCardIds { get; }
    public IReadOnlyList<uint> MaterialCardIds { get; }
    public int MaterialCount { get; }
    public MaterialDestination Destination { get; }
    public bool RequireFaceUp { get; }
    public SynchroMaterialRequirements? Synchro { get; }
    public XyzMaterialRequirements? Xyz { get; }

    public ExtraDeckMaterialProcedureProfile(string procedureId, IEnumerable<uint> targetCardIds,
        IEnumerable<uint> materialCardIds, int materialCount, MaterialDestination destination = MaterialDestination.Graveyard,
        bool requireFaceUp = true, SynchroMaterialRequirements? synchro = null, XyzMaterialRequirements? xyz = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(procedureId);
        ArgumentNullException.ThrowIfNull(targetCardIds);
        ArgumentNullException.ThrowIfNull(materialCardIds);
        var targets = targetCardIds.ToArray(); var materials = materialCardIds.ToArray();
        if (targets.Length == 0 || materials.Length == 0 || targets.Any(id => id == 0) || materials.Any(id => id == 0) ||
            targets.Distinct().Count() != targets.Length || materials.Distinct().Count() != materials.Length ||
            materialCount is < 1 or > 5 || !Enum.IsDefined(destination))
            throw new ArgumentException("Supply distinct target and material IDs, one to five materials and a supported destination.");
        if (synchro is not null && (!requireFaceUp || destination != MaterialDestination.Graveyard ||
            materialCount != synchro.MinimumNonTuners + 1 ||
            !materials.Order().SequenceEqual(synchro.TunerCardIds.Concat(synchro.NonTunerCardIds).Order())))
            throw new ArgumentException("Synchro profiles require face-up Graveyard materials, a matching allowlist and minimum count.");
        if ((destination == MaterialDestination.Attached) != (xyz is not null) ||
            xyz is not null && (!requireFaceUp || materialCount < 2 || synchro is not null))
            throw new ArgumentException("Xyz procedures require face-up attachment of two to five materials and no Synchro requirements.");
        ProcedureId = procedureId; TargetCardIds = Array.AsReadOnly(targets); MaterialCardIds = Array.AsReadOnly(materials);
        MaterialCount = materialCount; Destination = destination; RequireFaceUp = requireFaceUp; Synchro = synchro; Xyz = xyz;
    }
}

/// <summary>Explicit one-Tuner Synchro procedure using printed levels and designated material IDs.</summary>
public sealed class SynchroMaterialRequirements
{
    public IReadOnlyList<uint> TunerCardIds { get; }
    public IReadOnlyList<uint> NonTunerCardIds { get; }
    public int MinimumNonTuners { get; }
    public int MaximumNonTuners { get; }

    public SynchroMaterialRequirements(IEnumerable<uint> tunerCardIds, IEnumerable<uint> nonTunerCardIds,
        int minimumNonTuners = 1, int maximumNonTuners = 4)
    {
        ArgumentNullException.ThrowIfNull(tunerCardIds);
        ArgumentNullException.ThrowIfNull(nonTunerCardIds);
        var tuners = tunerCardIds.ToArray(); var others = nonTunerCardIds.ToArray();
        var all = tuners.Concat(others).ToArray();
        if (tuners.Length == 0 || others.Length == 0 || all.Any(id => id == 0) ||
            all.Distinct().Count() != all.Length || minimumNonTuners < 1 ||
            maximumNonTuners > 4 || minimumNonTuners > maximumNonTuners)
            throw new ArgumentException("Supply distinct Tuner and non-Tuner IDs and one to four non-Tuners.");
        TunerCardIds = Array.AsReadOnly(tuners); NonTunerCardIds = Array.AsReadOnly(others);
        MinimumNonTuners = minimumNonTuners; MaximumNonTuners = maximumNonTuners;
    }
}

/// <summary>Complete, revision-checked selection of uninterrupted field placements.</summary>
public sealed record SpecialSummonWithMaterials : DuelCommand
{
    public long ExpectedRevision { get; }
    public string ProcedureId { get; }
    public int CardInstanceId { get; }
    public int MonsterZone { get; }
    public BattlePosition Position { get; }
    public IReadOnlyList<MonsterBinding> Materials { get; }
    public SpecialSummonWithMaterials(PlayerId player, long expectedRevision, string procedureId, int cardInstanceId,
        int monsterZone, IEnumerable<MonsterBinding> materials, BattlePosition position = BattlePosition.FaceUpAttack) : base(player)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ExpectedRevision = expectedRevision; ProcedureId = procedureId; CardInstanceId = cardInstanceId;
        MonsterZone = monsterZone; Position = position; Materials = Array.AsReadOnly(materials.ToArray());
    }
}

/// <summary>A material is sent to its owner's Graveyard, not destroyed or Tributed.</summary>
public sealed record SummonMaterialSentToGraveyard(MonsterBinding Material, string ProcedureId) : DuelEvent;
/// <summary>A material is banished to its owner, not destroyed or sent to the Graveyard.</summary>
public sealed record SummonMaterialBanished(MonsterBinding Material, string ProcedureId) : DuelEvent;
