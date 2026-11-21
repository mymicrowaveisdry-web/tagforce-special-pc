using Microsoft.Xna.Framework;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Screens.DuelField;

internal static class DuelNoticeLine
{
    public static void DrawOriginal(DuelDrawList list, DuelFeedback feedback)
    {
        // Empty selections have no native overlay. Engine-specific failures retain a readable message.
        if (feedback.Kind is DuelFeedbackKind.None or DuelFeedbackKind.Absent) return;
        list.Add(new DuelFieldText(DuelPart.Prompt, feedback.Text, 154.8f, 245.42f, 0.6f, 0.54f, 0xffffff, 128));
    }

    public static void Draw(DuelDrawList list, DuelView view, DuelFeedback feedback, string notice)
    {
        if (feedback.Kind == DuelFeedbackKind.None && view.Availability == ActionAvailability.Unsupported)
            feedback = new(DuelFeedbackKind.Unsupported, "This decision is not supported by the PC duel.");
        string message = feedback.Kind == DuelFeedbackKind.None ? notice : feedback.Text;
        if (string.IsNullOrWhiteSpace(message) || message == "Choose an action") return;

        (string title, Color accent) = feedback.Kind switch
        {
            DuelFeedbackKind.Rejected => ("Action rejected", new Color(255, 147, 137)),
            DuelFeedbackKind.Unsupported => ("Unsupported", DuelPalette.Gold),
            DuelFeedbackKind.Absent => ("No action", DuelPalette.Muted),
            DuelFeedbackKind.Required => ("Choice required", DuelPalette.Gold),
            _ => ("", DuelPalette.Muted)
        };
        const DuelPart part = DuelPart.Prompt;
        list.Fill(part, new Rectangle(154, 230, 312, 27), new Color(20, 30, 45), SceneLayer.Modal);
        list.Fill(part, new Rectangle(154, 230, 3, 27), accent, SceneLayer.ModalHighlight);
        if (title.Length > 0) list.Text(part, title, 165, 233, accent, SceneLayer.ModalText);
        list.Add(new DuelWrapped(part, message, 165, title.Length == 0 ? 235 : 243, 48, title.Length == 0 ? 2 : 1, Color.White)
            { Ellipsis = true, Layer = SceneLayer.ModalText });
    }
}
