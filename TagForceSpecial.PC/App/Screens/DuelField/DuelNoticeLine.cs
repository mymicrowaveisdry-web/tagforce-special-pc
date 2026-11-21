using Microsoft.Xna.Framework;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Screens.DuelField;

internal static class DuelNoticeLine
{
    private static readonly IReadOnlyDictionary<int, (int, int)> Dark = new Dictionary<int, (int, int)> { [0] = (0x212121, 64) };

    /// <summary>PC feedback (rejected, unsupported, required) in the original message window (measured on the card list's "Checking field…"
    /// message, order32-notice n-checking: depth clear, window04 frame [208, 41, 420, 91] centred on x 314, card-font text 0.6 × 0.54 in
    /// 0x212121 with a 64 shadow from (217.8, 50.42) on an 11.72 line pitch). Inferred from that one sample: the fixed window size and
    /// the wrap width 196 (two lines fit). Empty selections have no overlay.</summary>
    public static void DrawOriginal(DuelDrawList list, DuelFieldProfile field, DuelFeedback feedback)
    {
        if (feedback.Kind is DuelFeedbackKind.None or DuelFeedbackKind.Absent) return;
        const DuelPart part = DuelPart.Prompt;
        list.FieldPiece(part, field, "menu.clear");
        list.Add(DuelMenuWindow.Frame(part, field, "menu.window", 208, 41, 420, 91, null));
        list.Add(new DuelRichText(part, "", 0, feedback.Text, 217.8f, 50.42f, 0.6f, 0.54f, Dark, 196, 11.72f));
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
