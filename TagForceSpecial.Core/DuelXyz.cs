namespace TagForceSpecial.Core;

/// <summary>Authored rank, separate from a monster's level; ordinary equal printed-level materials only.</summary>
public sealed class XyzMaterialRequirements
{
    public uint Rank { get; }
    public XyzMaterialRequirements(uint rank)
    {
        if (rank == 0) throw new ArgumentOutOfRangeException(nameof(rank));
        Rank = rank;
    }
}

public sealed record XyzMaterialAttached(CardInstance Host, MonsterBinding Material, string ProcedureId) : DuelEvent;
/// <summary>Sent from attachment storage, never from the field or by destruction.</summary>
public sealed record XyzMaterialSentToGraveyard(CardInstance Host, CardInstance Material, bool IsCost) : DuelEvent;

/// <summary>A card became Xyz Material of a monster by a card effect.</summary>
public sealed record CardAttachedAsMaterial(PlayerId Controller, CardInstance Host, CardInstance Card, string EffectId) : DuelEvent;

public sealed record ChooseTriggerXyzMaterials : DuelCommand
{
    public long DecisionId { get; }
    public IReadOnlyList<int> MaterialInstanceIds { get; }
    public ChooseTriggerXyzMaterials(PlayerId player, long decisionId, IEnumerable<int> materialInstanceIds) : base(player)
    {
        ArgumentNullException.ThrowIfNull(materialInstanceIds);
        DecisionId = decisionId;
        MaterialInstanceIds = Array.AsReadOnly(materialInstanceIds.ToArray());
    }
}
