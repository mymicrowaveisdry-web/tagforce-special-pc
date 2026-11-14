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
    /// <summary>Any monster allowed as a Summon material qualifies; rank or Tuner and Level rules still apply.</summary>
    public bool AnyMaterial { get; }
    /// <summary>"You can also Xyz Summon this card by using" one face-up Xyz Monster matching this filter; its materials carry over.</summary>
    public CardFilter? OverlayFrom { get; }
    /// <summary>The Proper Summon procedure the Summon records; an overlay procedure records the card's main Xyz procedure.</summary>
    public string ProperProcedureId { get; }

    public ExtraDeckMaterialProcedureProfile(string procedureId, IEnumerable<uint> targetCardIds,
        IEnumerable<uint> materialCardIds, int materialCount, MaterialDestination destination = MaterialDestination.Graveyard,
        bool requireFaceUp = true, SynchroMaterialRequirements? synchro = null, XyzMaterialRequirements? xyz = null, bool anyMaterial = false,
        CardFilter? overlayFrom = null, string? properProcedureId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(procedureId);
        ArgumentNullException.ThrowIfNull(targetCardIds);
        ArgumentNullException.ThrowIfNull(materialCardIds);
        var targets = targetCardIds.ToArray(); var materials = materialCardIds.ToArray();
        if (anyMaterial && (materials.Length != 0 || synchro is null && xyz is null))
            throw new ArgumentException("An any-material procedure is a Synchro or Xyz procedure with no explicit material list.", nameof(anyMaterial));
        AnyMaterial = anyMaterial;
        if (overlayFrom is not null && (xyz is null || anyMaterial || materials.Length != 0 || materialCount != 1 || string.IsNullOrWhiteSpace(properProcedureId)) ||
            overlayFrom is null && properProcedureId is not null)
            throw new ArgumentException("An overlay procedure is an Xyz procedure using exactly one Xyz Monster and names the card's main procedure.", nameof(overlayFrom));
        if (targets.Length == 0 || materials.Length == 0 && !anyMaterial && overlayFrom is null || targets.Any(id => id == 0) || materials.Any(id => id == 0) ||
            targets.Distinct().Count() != targets.Length || materials.Distinct().Count() != materials.Length ||
            materialCount is < 1 or > 5 || !Enum.IsDefined(destination))
            throw new ArgumentException("Supply distinct target and material IDs, one to five materials and a supported destination.");
        if (synchro is not null && (!requireFaceUp || destination != MaterialDestination.Graveyard ||
            materialCount != synchro.MinimumNonTuners + 1 ||
            !anyMaterial && !materials.Order().SequenceEqual(synchro.TunerCardIds.Concat(synchro.NonTunerCardIds).Order())))
            throw new ArgumentException("Synchro profiles require face-up Graveyard materials, a matching allowlist and minimum count.");
        if ((destination == MaterialDestination.Attached) != (xyz is not null) ||
            xyz is not null && (!requireFaceUp || materialCount < (overlayFrom is null ? 2 : 1) || synchro is not null))
            throw new ArgumentException("Xyz procedures require face-up attachment of two to five materials and no Synchro requirements.");
        ProcedureId = procedureId; TargetCardIds = Array.AsReadOnly(targets); MaterialCardIds = Array.AsReadOnly(materials);
        MaterialCount = materialCount; Destination = destination; RequireFaceUp = requireFaceUp; Synchro = synchro; Xyz = xyz;
        OverlayFrom = overlayFrom; ProperProcedureId = properProcedureId ?? procedureId;
    }
}

/// <summary>Explicit one-Tuner Synchro procedure using printed levels and designated material IDs.</summary>
public sealed class SynchroMaterialRequirements
{
    public IReadOnlyList<uint> TunerCardIds { get; }
    public IReadOnlyList<uint> NonTunerCardIds { get; }
    public int MinimumNonTuners { get; }
    public int MaximumNonTuners { get; }
    /// <summary>Tuners are read from card rules, so no card ID lists are needed.</summary>
    public bool ByTunerRule { get; }

    /// <summary>"1 Tuner + N or more non-Tuner monsters": Tuners come from <see cref="DuelCardRules.IsTuner"/>.</summary>
    public static SynchroMaterialRequirements AnyTuner(int minimumNonTuners = 1, int maximumNonTuners = 4) => new(minimumNonTuners, maximumNonTuners);

    private SynchroMaterialRequirements(int minimumNonTuners, int maximumNonTuners)
    {
        if (minimumNonTuners < 1 || maximumNonTuners > 4 || minimumNonTuners > maximumNonTuners)
            throw new ArgumentException("Supply one to four non-Tuners.");
        ByTunerRule = true; MinimumNonTuners = minimumNonTuners; MaximumNonTuners = maximumNonTuners;
        TunerCardIds = Array.AsReadOnly(Array.Empty<uint>()); NonTunerCardIds = Array.AsReadOnly(Array.Empty<uint>());
    }

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
