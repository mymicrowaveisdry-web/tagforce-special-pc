namespace TagForceSpecial.Core;

public enum MaterialDestination { Graveyard, Banished }

/// <summary>Explicit generic procedure; it does not infer a Synchro, Xyz or other card family.</summary>
public sealed class ExtraDeckMaterialProcedureProfile
{
    public string ProcedureId { get; }
    public IReadOnlyList<uint> TargetCardIds { get; }
    public IReadOnlyList<uint> MaterialCardIds { get; }
    public int MaterialCount { get; }
    public MaterialDestination Destination { get; }
    public bool RequireFaceUp { get; }

    public ExtraDeckMaterialProcedureProfile(string procedureId, IEnumerable<uint> targetCardIds,
        IEnumerable<uint> materialCardIds, int materialCount, MaterialDestination destination = MaterialDestination.Graveyard,
        bool requireFaceUp = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(procedureId);
        ArgumentNullException.ThrowIfNull(targetCardIds);
        ArgumentNullException.ThrowIfNull(materialCardIds);
        var targets = targetCardIds.ToArray(); var materials = materialCardIds.ToArray();
        if (targets.Length == 0 || materials.Length == 0 || targets.Any(id => id == 0) || materials.Any(id => id == 0) ||
            targets.Distinct().Count() != targets.Length || materials.Distinct().Count() != materials.Length ||
            materialCount is < 1 or > 5 || !Enum.IsDefined(destination))
            throw new ArgumentException("Supply distinct target and material IDs, one to five materials and a supported destination.");
        ProcedureId = procedureId; TargetCardIds = Array.AsReadOnly(targets); MaterialCardIds = Array.AsReadOnly(materials);
        MaterialCount = materialCount; Destination = destination; RequireFaceUp = requireFaceUp;
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
