using Microsoft.Xna.Framework;
using TagForceSpecial.Core;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Rendering;
using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Draws the PC outcome banner over the last public duel frame.</summary>
public static class DuelEffectLayer
{
    public static void Draw(DuelDrawList list, DuelView view)
    {
        if (view.Outcome is not { } outcome) return;
        var (title, accent) = outcome.IsDraw ? ("Draw", DuelPalette.Gold)
            : outcome.Winner == view.Viewer ? ("You win", new Color(120, 222, 188)) : ("You lose", new Color(255, 147, 137));
        string reason = outcome.Reason switch
        {
            DuelEndReason.UnableToDraw => outcome.Loser == view.Viewer ? "You ran out of cards" : "Opponent ran out of cards",
            DuelEndReason.BothLifePointsDepleted => "Both players reached 0 LP",
            DuelEndReason.LifePointsDepleted => outcome.Loser == view.Viewer ? "Your Life Points reached 0" : "Opponent reached 0 LP",
            _ => "Duel ended"
        };
        const DuelPart part = DuelPart.Effects;
        list.Fill(part, new Rectangle(154, 99, 312, 62), new Color(19, 29, 45), SceneLayer.Modal);
        list.Fill(part, new Rectangle(154, 99, 312, 2), accent, SceneLayer.ModalHighlight);
        list.Text(part, title, 310 - 6 * title.Length, 111, accent, SceneLayer.ModalText, 2);
        list.Text(part, reason, 310 - 3 * reason.Length, 141, Color.White, SceneLayer.ModalText);
    }

    internal static void DrawOriginal(DuelDrawList list, DuelView view, DuelFieldProfile field)
    {
        if (view.Outcome is not { } outcome) return;
        const DuelPart part = DuelPart.Effects;
        string word = outcome.IsDraw ? "draw" : outcome.Winner == view.Viewer ? "win" : "lose";
        list.FieldPiece(part, field, "outcome.dim");
        list.FieldPiece(part, field, "outcome.glow");
        list.FieldPiece(part, field, "outcome." + word);
        bool lost = outcome.Loser == view.Viewer;
        int reason = outcome.Reason == DuelEndReason.UnableToDraw ? lost ? 402004 : 403870 : lost ? 401922 : 403766;
        if (!outcome.IsDraw)
            list.Add(new DuelOutcomeReason(part, reason));
    }
}

internal sealed record DuelOutcomeReason(DuelPart Part, int Offset) : DuelDraw(Part);
