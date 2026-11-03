using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;

namespace TagForceSpecial.PC.DuelUI;

public sealed record ChoiceView(int Id, string Label, int? FocusInstanceId = null);
public sealed record DuelView(long Revision, PlayerId Viewer, PlayerId ActivePlayer, int Turn,
    DuelPhase Phase, PlayerView Self, PlayerView Opponent, string Prompt,
    IReadOnlyList<ChoiceView> Choices, DuelOutcome? Outcome);
