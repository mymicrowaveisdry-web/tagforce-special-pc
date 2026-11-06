using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Deck Menu layers bound to the Gum screen: the submenu reuses the Main Menu panel atlas and draws its own label slots.</summary>
public sealed partial class TitleFlowVisuals
{
    public DeckMenuProfile DeckMenuProfile { get; } = DeckMenuProfile.LoadEmbedded();
    public RenderTarget2D DeckMenuLabels { get; private set; } = null!;

    /// <summary>Measured: the submenu's plain bar uses its own base pieces in the panel atlas; its focus pieces are the Main Menu's.</summary>
    public IReadOnlyList<(string Piece, Rectangle Source)> DeckMenuBarPieces => [.. DeckMenuProfile.BasePieces.Select((p, i) =>
        (new[] { "BaseLeft", "BaseCenter", "BaseRight" }[i], new Rectangle(p[0], p[1], p[2], p[3]))),
        .. MenuBarPieces.Where(p => p.Piece.StartsWith("Focus", StringComparison.Ordinal))];

    private void InitializeDeckMenu() =>
        DeckMenuLabels = new(device, LabelWidth, LabelHeight * 2 * DeckMenuProfile.Rows.Length, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

    /// <summary>Glyphs for both row states at the measured sub-pixel positions relative to the submenu bar, read from source text.</summary>
    public void RenderDeckMenuLabels()
    {
        var projection = effect.Projection;
        var rows = DeckMenuProfile.Rows;
        WithTarget(DeckMenuLabels, () =>
        {
            device.Clear(Color.Transparent);
            effect.Projection = Matrix.CreateOrthographicOffCenter(0, LabelWidth, DeckMenuLabels.Height, 0, 0, 1);
            for (int row = 0; row < rows.Length; row++)
                for (int state = 0; state < 2; state++)
                {
                    var label = rows[row].Label;
                    float slot = (row * 2 + state) * LabelHeight - rows[row].Y;
                    float[] x = Array.ConvertAll(label.X, v => v - DeckMenuProfile.BarLeft);
                    // Measured as on the Main Menu: focused rows use black text with a light shadow; others white with a heavier one.
                    DrawLine(Source(label.Source, label.Offset, label.Start, label.Chars), x[0], label.Y + slot, label.ScaleX, label.ScaleY,
                        state == 1 ? 0 : 0xffffff, state == 1 ? 0x40 : 0x80, x);
                }
        });
        effect.Projection = projection;
    }

    /// <summary>Authored text when no PSP font was imported; never presented as original typography.</summary>
    public void SetDeckMenuFallback(int menuFocus, int? focus)
    {
        FallbackLines.Clear();
        if (OriginalFontAvailable) return;
        var menu = frames[MainMenuFrame].Menu!;
        for (int row = 0; row < menu.Rows.Length; row++)
        {
            var label = menu.Rows[row].Label;
            FallbackLines.Add((Source(label.Source, label.Offset, label.Start, label.Chars).ToUpperInvariant(), (int)label.X[0], (int)label.Y + 3, Color.Gray));
        }
        for (int row = 0; row < DeckMenuProfile.Rows.Length; row++)
        {
            var label = DeckMenuProfile.Rows[row].Label;
            FallbackLines.Add((Source(label.Source, label.Offset, label.Start, label.Chars).ToUpperInvariant(), (int)label.X[0], (int)label.Y + 3, row == focus ? Color.Black : Color.White));
        }
    }
}
