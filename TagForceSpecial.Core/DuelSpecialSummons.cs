namespace TagForceSpecial.Core;

[Flags]
public enum SpecialSummonSources { None = 0, Hand = 1, Deck = 2, Graveyard = 4, Banished = 8, ExtraDeck = 16, All = 31 }
public enum SummonMethod { Normal, Set, Flip, SpecialEffect, SpecialProcedure, Token }
public enum SummonStatus { Pending, Completed, Negated }

/// <summary>Latest summon/placement attempt per instance, with retained proper-procedure eligibility.</summary>
public sealed record SummonRecord(long AttemptId, PlayerId Player, CardInstance Card, int MonsterZone,
    BattlePosition Position, SummonMethod Method, DecisionCardSource? Source, SummonStatus Status,
    int TurnNumber, long? CompletedOnRevision, long? PlacementId, string? ProcedureId, string? ProperProcedureId);

/// <summary>Trusted card eligibility. A registered policy narrows even existing effect allowlists.</summary>
public sealed class SpecialSummonCardProfile
{
    public uint CardId { get; }
    public SpecialSummonSources EffectSources { get; }
    public string? RequiredProperProcedureId { get; }
    public SpecialSummonCardProfile(uint cardId, SpecialSummonSources effectSources = SpecialSummonSources.None,
        string? requiredProperProcedureId = null)
    {
        if (cardId == 0 || (effectSources & ~SpecialSummonSources.All) != 0 ||
            requiredProperProcedureId is not null && string.IsNullOrWhiteSpace(requiredProperProcedureId))
            throw new ArgumentException("Supply a catalog monster, valid effect-source mask and optional proper procedure ID.");
        CardId = cardId; EffectSources = effectSources; RequiredProperProcedureId = requiredProperProcedureId;
    }
}

/// <summary>Costless authored procedure, not an activated effect or an inferred real card contract.</summary>
public sealed class SpecialSummonProcedureProfile
{
    public string ProcedureId { get; }
    public IReadOnlyList<uint> CardIds { get; }
    public DecisionCardSource Source { get; }
    public IReadOnlyList<BattlePosition> Positions { get; }
    public bool RequiresEmptyOwnField { get; }
    public bool RequiresOpposingMonster { get; }
    public SpecialSummonProcedureProfile(string procedureId, IEnumerable<uint> cardIds,
        DecisionCardSource source = DecisionCardSource.Hand, IEnumerable<BattlePosition>? positions = null,
        bool requiresEmptyOwnField = false, bool requiresOpposingMonster = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(procedureId);
        ArgumentNullException.ThrowIfNull(cardIds);
        var ids = cardIds.ToArray();
        var choices = (positions ?? [BattlePosition.FaceUpAttack, BattlePosition.FaceUpDefense]).ToArray();
        if (source is not (DecisionCardSource.Hand or DecisionCardSource.Graveyard or DecisionCardSource.Banished) ||
            ids.Length == 0 || ids.Any(id => id == 0) || ids.Distinct().Count() != ids.Length ||
            choices.Length == 0 || choices.Any(p => p is not (BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense)) ||
            choices.Distinct().Count() != choices.Length)
            throw new ArgumentException("Supply distinct explicit targets, a supported source and face-up positions.");
        ProcedureId = procedureId; CardIds = Array.AsReadOnly(ids); Source = source;
        Positions = Array.AsReadOnly(choices); RequiresEmptyOwnField = requiresEmptyOwnField;
        RequiresOpposingMonster = requiresOpposingMonster;
    }
}

public sealed record SpecialSummonByProcedure(PlayerId Player, long ExpectedRevision, string ProcedureId,
    int CardInstanceId, int MonsterZone, BattlePosition Position = BattlePosition.FaceUpAttack) : DuelCommand(Player);
