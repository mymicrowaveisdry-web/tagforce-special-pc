namespace TagForceSpecial.Core;

public sealed record FieldSpellTrap(CardInstance Card, bool IsFaceUp, long PlacementId = 0, int SetOnTurn = 0)
{
    /// <summary>A lasting card whose activation resolved; its continuing effects apply while it stays in place.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EffectActive { get; internal init; }
    /// <summary>The monster this Equip Card is attached to.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public MonsterBinding? EquippedTo { get; internal init; }
    /// <summary>A monster card sitting here as a Union equip.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsUnion { get; internal init; }
    /// <summary>The monster this card revived, and how the two are tied together.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public MonsterBinding? LinkedMonster { get; internal init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public RevivalLink Revival { get; internal init; }
    /// <summary>The turn this card's Standby Phase upkeep was last settled.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int UpkeepSettledTurn { get; internal init; }
    /// <summary>Counters on this card; a card effect places and removes them.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Counters { get; internal init; }
    /// <summary>Counters removed instead of this card being destroyed.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int ProtectionCounters { get; internal init; }
}
/// <summary>
/// An activation placed or flipped but still answering its cost/target decisions. A chain response records the
/// link it answers (<see cref="ResponseToLink"/>); the source keeps <see cref="SourcePlacementId"/>.
/// </summary>
public sealed record PendingSpellActivation(PlayerId Player, string EffectId, CardInstance Spell, int SpellTrapZone,
    CardInstance? DiscardedCost = null, int ResponseToLink = 0, long SourcePlacementId = 0)
{
    /// <summary>The source is a monster in that Monster Zone, not a Spell/Trap card.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMonsterEffect { get; init; }
}
public sealed record ActivateSpell(PlayerId Player, int CardInstanceId, int SpellTrapZone) : DuelCommand(Player);
public sealed record SpellActivated(PlayerId Player, string EffectId, CardInstance Card, int Zone) : DuelEvent;
public sealed record MonsterSpecialSummoned(PlayerId Player, FieldMonster Monster, int Zone, DecisionCardSource Source,
    string? ProcedureId = null, long? AttemptId = null, string? ProperProcedureId = null) : DuelEvent;
public sealed record SpellSentToGraveyard(PlayerId Player, CardInstance Card, int Zone, bool IsPendulumZone = false) : DuelEvent
{
    /// <summary>A negated activation goes to the Graveyard without counting as sent from the field.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ActivationNegated { get; internal init; }
}
/// <summary>
/// Bound to the summoned instance and its uninterrupted field placement; due in the End Phase of
/// <see cref="DueTurn"/>. If that placement is gone by then, the banishment expires.
/// </summary>
public sealed record PendingEndPhaseBanishment(PlayerId Player, string EffectId, CardInstance Spell,
    CardInstance Monster, int MonsterZone, int DueTurn, long PlacementId);
public sealed record EndPhaseBanishmentScheduled(PendingEndPhaseBanishment Banishment) : DuelEvent;
public sealed record MonsterBanished(PlayerId Player, CardInstance Card, int Zone, string EffectId) : DuelEvent;
public sealed record EndPhaseBanishmentExpired(PlayerId Player, CardInstance Card, string EffectId) : DuelEvent;
