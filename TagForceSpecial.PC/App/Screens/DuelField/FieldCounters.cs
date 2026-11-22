using System.Globalization;
using Microsoft.Xna.Framework;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Draws the small card_icon03 numerals over piles and cards. Measured: 7 × 9 texel digits at u = 7 × digit, placed at the
/// truncated projection of an anchor, shifted left by 7 × digits / 2 and up by 4 (all 8 pile and 13 Level samples).</summary>
public static class FieldCounters
{
    public const int DigitWidth = 7, DigitHeight = 9, AtlasWidth = 128;
    public static readonly Vector4 White = new(1, 1, 1, 1), Tuner = new(1, 1, 0, 1);

    /// <param name="lineOffset">Pixels below the anchored line (the Battle Phase DEF readout).</param>
    public static void Draw(DuelDrawList list, DuelPart part, DuelFieldProfile field, int value, Vector3 anchor, Vector4 color, int lineOffset = 0)
    {
        string text = value.ToString(CultureInfo.InvariantCulture);
        var screen = DuelLayoutProfile.Project(field, anchor);
        int x0 = (int)screen.X - DigitWidth * text.Length / 2, y0 = (int)screen.Y - DigitHeight / 2 + lineOffset;
        var template = field.Piece("digit.small")[0];
        float templateX = template.Vertices![0][0], templateY = template.Vertices[0][1];
        // The template shows the digit 1.
        for (int i = 0; i < text.Length; i++)
        {
            int digit = text[i] - '0';
            list.Field(part, "digit.small", 0, new(Offset: new(x0 + i * DigitWidth - templateX, y0 - templateY),
                UvOffset: new((digit - 1) * DigitWidth / (float)AtlasWidth, 0), Color: color));
        }
    }
}
