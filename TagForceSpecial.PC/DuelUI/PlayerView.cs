using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;

namespace TagForceSpecial.PC.DuelUI;

/// <summary>One legal action as the viewer may see it: its token, label, the card it concerns, its kind, own zone, target zone and phase.</summary>
public sealed record ChoiceView(int Id, string Label, int? FocusInstanceId = null, ActionKind? Kind = null, int? TargetZone = null,
    DuelPhase? Phase = null, int? Zone = null);
public sealed record DuelView(long Revision, PlayerId Viewer, PlayerId ActivePlayer, int Turn,
    DuelPhase Phase, PlayerView Self, PlayerView Opponent, string Prompt,
    IReadOnlyList<ChoiceView> Choices, DuelOutcome? Outcome, ActionAvailability Availability = ActionAvailability.Ready);
