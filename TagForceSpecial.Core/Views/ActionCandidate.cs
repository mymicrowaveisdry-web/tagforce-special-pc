namespace TagForceSpecial.Core.Views;

/// <summary>A trusted command and its public description before authorization and token assignment.</summary>
internal sealed record ActionCandidate(DuelCommand Command, LegalAction Action)
{
    internal static PlayerId Other(PlayerId player) => player == PlayerId.One ? PlayerId.Two : PlayerId.One;

    internal static ActionCandidate B(DuelCommand command, ActionKind kind, string label, int? card = null, int? zone = null,
        int? target = null, BattlePosition? position = null, DuelPhase? phase = null, PlayerId? targetPlayer = null)
        => new(command, new(0, $"{kind}|{card}|{zone}|{target}|{position}|{phase}|{targetPlayer}", kind, label, card, zone, target, position, phase));

    internal static int? Option(int effectIndex) => effectIndex == 0 ? null : effectIndex;

}
