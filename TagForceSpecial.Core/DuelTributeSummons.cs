namespace TagForceSpecial.Core;

/// <summary>
/// Complete ordinary Tribute Summon/Set selection from one snapshot. Sources must
/// explicitly support ordinary Graveyard departure; token/Pendulum rules are separate.
/// </summary>
public sealed record TributeSummon : DuelCommand
{
    public long ExpectedRevision { get; }
    public int CardInstanceId { get; }
    public int MonsterZone { get; }
    public bool FaceDown { get; }
    public IReadOnlyList<MonsterBinding> Tributes { get; }

    public TributeSummon(PlayerId player, long expectedRevision, int cardInstanceId,
        int monsterZone, IEnumerable<MonsterBinding> tributes, bool faceDown = false) : base(player)
    {
        ArgumentNullException.ThrowIfNull(tributes);
        ExpectedRevision = expectedRevision;
        CardInstanceId = cardInstanceId;
        MonsterZone = monsterZone;
        FaceDown = faceDown;
        Tributes = Array.AsReadOnly(tributes.ToArray());
    }
}
