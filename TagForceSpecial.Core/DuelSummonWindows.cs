namespace TagForceSpecial.Core;

/// <summary>A summon target committed to an attempt but not on the field.</summary>
public sealed record PendingSummon(long AttemptId, long DecisionId, PlayerId Player,
    CardInstance Card, int MonsterZone, PlayerId Responder, int ConsecutivePasses = 0,
    bool Negated = false, bool IsSpecialSummon = false, DecisionCardSource Source = DecisionCardSource.Hand,
    BattlePosition Position = BattlePosition.FaceUpAttack, string? ProcedureId = null, string? ProperProcedureId = null)
{
    public IReadOnlyList<CardInstance> XyzMaterials { get; internal init; } = Array.AsReadOnly(Array.Empty<CardInstance>());
    public IReadOnlyList<PendingSummon> PendulumGroup { get; internal init; } = Array.AsReadOnly(Array.Empty<PendingSummon>());
}

public sealed record PassSummonResponse(PlayerId Player, long DecisionId) : DuelCommand(Player);
public sealed record ActivateSummonNegation(PlayerId Player, long DecisionId, int SpellTrapZone) : DuelCommand(Player);
public sealed record SummonAttempted(PendingSummon Attempt) : DuelEvent;
public sealed record SummonResponseRequested(PendingSummon Attempt) : DuelEvent;
public sealed record SummonResponsePassed(PlayerId Player) : DuelEvent;
/// <summary>The unsuccessful target is destroyed and sent to its owner's Graveyard, without field entry.</summary>
public sealed record SummonNegated(PendingSummon Attempt) : DuelEvent;
