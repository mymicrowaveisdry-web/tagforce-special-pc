using TagForceSpecial.PC.App.Visual.Flow;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>A Deck or Database submenu's label slots (both row states of every row) as host-neutral commands, and its bar pieces in the
/// Main Menu panel atlas. Port of the MonoGame host's submenu layers.</summary>
public sealed class SubmenuLayers(SubmenuProfile profile)
{
    public SubmenuProfile Profile => profile;
    public int LabelLayerHeight => MainMenuLayers.LabelHeight * 2 * profile.Rows.Length;

    /// <summary>Measured: a submenu's plain bar uses its own base pieces in the panel atlas; its focus pieces are the Main Menu's.</summary>
    public IReadOnlyList<(string Piece, int X, int Y, int Width, int Height)> BarPieces =>
    [
        .. profile.BasePieces.Select((p, i) => (new[] { "BaseLeft", "BaseCenter", "BaseRight" }[i], p[0], p[1], p[2], p[3])),
        .. MainMenuLayers.BarPieces.Where(p => p.Piece.StartsWith("Focus", StringComparison.Ordinal))
    ];

    /// <summary>Glyphs for both row states at the measured sub-pixel positions relative to the bar; focused rows black with a light shadow,
    /// others white with a heavier one, as on the Main Menu.</summary>
    public IReadOnlyList<FlowCommand> Labels(FlowTextSources text, PgfGlyphAtlas font, string fontKey)
    {
        var commands = new List<FlowCommand>();
        for (int row = 0; row < profile.Rows.Length; row++)
            for (int state = 0; state < 2; state++)
            {
                var label = profile.Rows[row].Label;
                float slot = (row * 2 + state) * MainMenuLayers.LabelHeight - profile.Rows[row].Y;
                float[] x = Array.ConvertAll(label.X, v => v - profile.BarLeft);
                commands.AddRange(FlowTextLayout.Line(font, fontKey, text.Read(label), x[0], label.Y + slot, label.ScaleX, label.ScaleY,
                    state == 1 ? 0 : 0xffffff, state == 1 ? 0x40 : 0x80, x));
            }
        return commands;
    }
}
