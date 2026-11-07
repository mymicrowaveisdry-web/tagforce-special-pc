namespace TagForceSpecial.Core;

public sealed record FieldSpellTrap(CardInstance Card, bool IsFaceUp, long PlacementId = 0, int SetOnTurn = 0);
/// <summary>
/// An activation placed or flipped but still answering its cost/target decisions. A chain response records the
/// link it answers (<see cref="ResponseToLink"/>); the source keeps <see cref="SourcePlacementId"/>.
/// </summary>
public sealed record PendingSpellActivation(PlayerId Player, string EffectId, CardInstance Spell, int SpellTrapZone,
    CardInstance? DiscardedCost = null, int ResponseToLink = 0, long SourcePlacementId = 0);
public sealed record ActivateSpell(PlayerId Player, int CardInstanceId, int SpellTrapZone) : DuelCommand(Player);
public sealed record SpellActivated(PlayerId Player, string EffectId, CardInstance Card, int Zone) : DuelEvent;
public sealed record MonsterSpecialSummoned(PlayerId Player, FieldMonster Monster, int Zone, DecisionCardSource Source,
    string? ProcedureId = null, long? AttemptId = null, string? ProperProcedureId = null) : DuelEvent;
public sealed record SpellSentToGraveyard(PlayerId Player, CardInstance Card, int Zone, bool IsPendulumZone = false) : DuelEvent;
/// <summary>
/// Bound to the summoned instance and its uninterrupted field placement; due in the End Phase of
/// <see cref="DueTurn"/>. If that placement is gone by then, the banishment expires.
/// </summary>
public sealed record PendingEndPhaseBanishment(PlayerId Player, string EffectId, CardInstance Spell,
    CardInstance Monster, int MonsterZone, int DueTurn, long PlacementId);
public sealed record EndPhaseBanishmentScheduled(PendingEndPhaseBanishment Banishment) : DuelEvent;
public sealed record MonsterBanished(PlayerId Player, CardInstance Card, int Zone, string EffectId) : DuelEvent;
public sealed record EndPhaseBanishmentExpired(PlayerId Player, CardInstance Card, string EffectId) : DuelEvent;
