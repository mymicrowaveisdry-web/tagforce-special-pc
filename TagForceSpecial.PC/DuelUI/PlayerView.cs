using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;

namespace TagForceSpecial.PC.DuelUI;

/// <summary>One legal action as the viewer may see it: its token, label, the card it concerns, its kind, own zone, target zone and phase.</summary>
public sealed record ChoiceView(int Id, string Label, int? FocusInstanceId = null, ActionKind? Kind = null, int? TargetZone = null,
    DuelPhase? Phase = null, int? Zone = null, BattlePosition? Position = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AuthorizedFieldTarget? FieldTarget { get; init; }

    /// <summary>The card a choose-card option or a position choice is about (the chooser's own decision).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CardView? Subject { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DecisionCardSource? SubjectSource { get; init; }
}
/// <summary>The viewer's authorized duel state. <paramref name="ChainTop"/> is the card of the newest chain link (public once activated).</summary>
public sealed record DuelView(long Revision, PlayerId Viewer, PlayerId ActivePlayer, int Turn,
    DuelPhase Phase, PlayerView Self, PlayerView Opponent, string Prompt,
    IReadOnlyList<ChoiceView> Choices, DuelOutcome? Outcome, ActionAvailability Availability = ActionAvailability.Ready,
    CardView? ChainTop = null);
