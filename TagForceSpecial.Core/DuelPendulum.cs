namespace TagForceSpecial.Core;

/// <summary>Where a card that left the field went. <see cref="Removed"/>: a Token ceased to exist (no zone).</summary>
public enum FieldCardDestination { Graveyard, FaceUpExtraDeck, Removed, Hand, ExtraDeck, Banished, Deck }
public sealed record PendulumCardProfile(uint CardId, uint LeftScale, uint RightScale)
{
    /// <summary>When set, the scale is <see cref="ScaleWithoutPartner"/> unless the other Pendulum Zone holds a matching card.</summary>
    public CardFilter? PartnerFilter { get; init; }
    public uint ScaleWithoutPartner { get; init; }
}
public sealed record ActivatePendulumScale(PlayerId Player, int CardInstanceId, int PendulumZone) : DuelCommand(Player);
public sealed record PendulumSummonSelection(int CardInstanceId, DecisionCardSource Source, int MonsterZone,
    BattlePosition Position = BattlePosition.FaceUpAttack);
public sealed record PendulumSummon : DuelCommand
{
    public long ExpectedRevision { get; }
    public IReadOnlyList<PendulumSummonSelection> Selections { get; }
    public PendulumSummon(PlayerId player, long expectedRevision, IEnumerable<PendulumSummonSelection> selections) : base(player)
    {
        ArgumentNullException.ThrowIfNull(selections);
        ExpectedRevision = expectedRevision; Selections = Array.AsReadOnly(selections.ToArray());
    }
}
public sealed record PendulumScaleActivated(PlayerId Player, CardInstance Card, int Zone) : DuelEvent;
public sealed record CardSentToFaceUpExtraDeck(CardInstance Card) : DuelEvent;
public sealed record SummonMaterialSentToFaceUpExtraDeck(MonsterBinding Material, string ProcedureId) : DuelEvent;
public sealed record FusionMaterialSentToFaceUpExtraDeck(EffectSummonMaterial Material, string EffectId) : DuelEvent;
