namespace TagForceSpecial.Core;

public enum FieldCardDestination { Graveyard, FaceUpExtraDeck }
public sealed record PendulumCardProfile(uint CardId, uint LeftScale, uint RightScale);
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
