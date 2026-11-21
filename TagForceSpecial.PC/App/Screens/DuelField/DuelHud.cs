using Microsoft.Xna.Framework;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Draws the top status bar and both players' Life Points.</summary>
public static class DuelHud
{
    /// <summary>Measured: the opponent's portrait frame sits 286 pixels right of the player's (150 → 436).</summary>
    public const float OpponentPortraitOffset = 286f;

    public static void DrawBar(DuelDrawList list)
    {
        list.Fill(DuelPart.Hud, DuelSceneLayout.HudBar, new(24, 36, 55));
        list.Text(DuelPart.Hud, "TAG FORCE / INITIAL DUEL", 8, 5, DuelPalette.Gold);
    }

    public static void DrawLifePoints(DuelDrawList list, DuelView view)
    {
        list.Text(DuelPart.Hud, $"YOU {view.Self.LifePoints}", 280, 5, new Color(104, 218, 214));
        list.Text(DuelPart.Hud, $"CPU {view.Opponent.LifePoints}", 382, 5, new Color(238, 136, 123));
    }

    /// <summary>Original: the score bar, the Help tab and both portrait frames. The additive glow marks the turn player's portrait; every
    /// capture shows it on the player's side, so the opponent's position is the measured frame offset.</summary>
    public static void DrawOriginal(DuelDrawList list, DuelFieldProfile field, DuelView view)
    {
        list.FieldPiece(DuelPart.Hud, field, "hud.frame");
        list.FieldPiece(DuelPart.Hud, field, "hud.ornament");
        list.FieldPiece(DuelPart.Hud, field, "hud.help");
        list.FieldPiece(DuelPart.Hud, field, "portrait.player.frame");
        bool playerTurn = view.ActivePlayer == view.Viewer;
        list.FieldPiece(DuelPart.Hud, field, "portrait.player.glow", new(Offset: new(playerTurn ? 0 : OpponentPortraitOffset, 0)));
        list.FieldPiece(DuelPart.Hud, field, "portrait.opponent.frame");
    }
}
