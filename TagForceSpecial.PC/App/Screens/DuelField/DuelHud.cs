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

    /// <summary>Measured large card_icon03 numerals (9 × 12 texels at u = 9 × digit, v = 26): Life Points right-aligned on a 10-pixel pitch
    /// ending at x 201 (player, cyan) and x 473 (opponent, pink), turn number at x 309 (yellow). Turns of two digits are centred on the
    /// same point (inferred; only single digits were captured).</summary>
    public static readonly Microsoft.Xna.Framework.Vector4 PlayerLp = new(0, 0.72549f, 1, 1), OpponentLp = new(1, 0.321569f, 0.521569f, 1),
        TurnColor = new(1, 0.866667f, 0.5f, 1);

    public static void DrawNumbers(DuelDrawList list, DuelFieldProfile field, DuelView view)
    {
        Number(list, field, Math.Max(0, view.Self.LifePoints), 192, 1, PlayerLp);
        Number(list, field, Math.Max(0, view.Opponent.LifePoints), 464, 1, OpponentLp);
        string turn = view.Turn.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int x = 309 - 9 * (turn.Length - 1) / 2;
        for (int i = 0; i < turn.Length; i++) Digit(list, field, turn[i] - '0', x + 9 * i, 12, TurnColor);
    }

    /// <summary>Right-aligned: the last digit at <paramref name="lastX"/>, earlier digits 10 pixels further left each.</summary>
    private static void Number(DuelDrawList list, DuelFieldProfile field, int value, int lastX, int y, Microsoft.Xna.Framework.Vector4 color)
    {
        string text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < text.Length; i++) Digit(list, field, text[text.Length - 1 - i] - '0', lastX - 10 * i, y, color);
    }

    private static void Digit(DuelDrawList list, DuelFieldProfile field, int digit, int x, int y, Microsoft.Xna.Framework.Vector4 color)
    {
        var template = field.Piece("digit.large")[0].Vertices![0]; // the template shows 0
        list.Field(DuelPart.Hud, "digit.large", 0, new(Offset: new(x - template[0], y - template[1]), UvOffset: new(digit * 9 / 128f, 0), Color: color));
    }

    /// <summary>Original: the score bar, the Help tab and both portrait frames. The additive glow marks the turn player's portrait; every
    /// capture shows it on the player's side, so the opponent's position is the measured frame offset.</summary>
    /// <summary>Measured HUD names (card-text font ltn0, white, shadow alpha 128): the player's starts at x 205.8 at 0.6 × 0.54. The
    /// opponent's is laid out right-aligned at x 417.55 with its width at 0.6, then drawn at 0.55 × 0.54 (Aki, Joey and Yuya captures
    /// agree within 0.1 pixel).</summary>
    public const float NameY = -1.58f, PlayerNameX = 205.8f, OpponentNameRight = 417.55f;

    public static void DrawOriginal(DuelDrawList list, DuelFieldProfile field, DuelView view, DuelFieldStyle? style = null)
    {
        style ??= new();
        list.FieldPiece(DuelPart.Hud, field, "hud.frame");
        list.FieldText(DuelPart.Hud, style.PlayerName, PlayerNameX, NameY, 0.6f, 0.54f, 0xffffff, 128);
        list.FieldText(DuelPart.Hud, style.OpponentName, OpponentNameRight, NameY, 0.55f, 0.54f, 0xffffff, 128, alignRight: true, layoutScaleX: 0.6f);
        DrawNumbers(list, field, view);
        list.FieldPiece(DuelPart.Hud, field, "hud.ornament");
        list.FieldPiece(DuelPart.Hud, field, "hud.help");
        list.FieldPiece(DuelPart.Hud, field, "portrait.player.frame");
        list.FieldPiece(DuelPart.Hud, field, "portrait.player.face", new(Texture: Assets.Presentation.DuelFaceIcons.Key(style.PlayerFace)));
        bool playerTurn = view.ActivePlayer == view.Viewer;
        list.FieldPiece(DuelPart.Hud, field, "portrait.player.glow", new(Offset: new(playerTurn ? 0 : OpponentPortraitOffset, 0)));
        list.FieldPiece(DuelPart.Hud, field, "portrait.opponent.frame");
        if (style.OpponentFace is { } face)
            list.FieldPiece(DuelPart.Hud, field, "portrait.opponent.face", new(Texture: Assets.Presentation.DuelFaceIcons.Key(face)));
        // Status icons under the portraits (EBOOT dialog atlas): the captured controller and hourglass cells; every capture was taken with
        // the player acting, so their opponent-turn cells are not measured yet.
        list.FieldPiece(DuelPart.Hud, field, "portrait.opponent.status");
        list.FieldPiece(DuelPart.Hud, field, "portrait.player.status");
    }
}
