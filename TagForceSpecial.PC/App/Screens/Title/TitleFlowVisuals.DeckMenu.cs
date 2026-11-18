using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Submenu layers bound to the Gum screens: each submenu reuses the Main Menu panel atlas and draws its own label slots.</summary>
public sealed partial class TitleFlowVisuals
{
    private readonly Dictionary<SubmenuKind, SubmenuProfile> submenuProfiles = new()
    {
        [SubmenuKind.Deck] = SubmenuProfile.LoadEmbedded(SubmenuKind.Deck),
        [SubmenuKind.Database] = SubmenuProfile.LoadEmbedded(SubmenuKind.Database)
    };
    private readonly Dictionary<SubmenuKind, RenderTarget2D> submenuLabels = [];

    public SubmenuProfile SubmenuProfileOf(SubmenuKind kind) => submenuProfiles[kind];
    public RenderTarget2D SubmenuLabels(SubmenuKind kind) => submenuLabels[kind];

    /// <summary>Measured: a submenu's plain bar uses its own base pieces in the panel atlas; its focus pieces are the Main Menu's.</summary>
    public IReadOnlyList<(string Piece, Rectangle Source)> SubmenuBarPieces(SubmenuKind kind) => [.. submenuProfiles[kind].BasePieces.Select((p, i) =>
        (new[] { "BaseLeft", "BaseCenter", "BaseRight" }[i], new Rectangle(p[0], p[1], p[2], p[3]))),
        .. MenuBarPieces.Where(p => p.Piece.StartsWith("Focus", StringComparison.Ordinal))];

    private void InitializeDeckMenu()
    {
        foreach (var (kind, profile) in submenuProfiles)
            submenuLabels[kind] = new(device, LabelWidth, LabelHeight * 2 * profile.Rows.Length, false, SurfaceFormat.Color, DepthFormat.None, 0,
                RenderTargetUsage.PreserveContents);
    }

    /// <summary>Glyphs for both row states of every submenu at the measured sub-pixel positions relative to its bar, read from source text.</summary>
    public void RenderSubmenuLabels()
    {
        var projection = effect.Projection;
        foreach (var (kind, profile) in submenuProfiles)
        {
            var target = submenuLabels[kind];
            WithTarget(target, () =>
            {
                device.Clear(Color.Transparent);
                effect.Projection = Matrix.CreateOrthographicOffCenter(0, LabelWidth, target.Height, 0, 0, 1);
                for (int row = 0; row < profile.Rows.Length; row++)
                    for (int state = 0; state < 2; state++)
                    {
                        var label = profile.Rows[row].Label;
                        float slot = (row * 2 + state) * LabelHeight - profile.Rows[row].Y;
                        float[] x = Array.ConvertAll(label.X, v => v - profile.BarLeft);
                        // Measured as on the Main Menu: focused rows use black text with a light shadow; others white with a heavier one.
                        DrawLine(Source(label.Source, label.Offset, label.Start, label.Chars), x[0], label.Y + slot, label.ScaleX, label.ScaleY,
                            state == 1 ? 0 : 0xffffff, state == 1 ? 0x40 : 0x80, x);
                    }
            });
        }
        effect.Projection = projection;
    }

    /// <summary>Authored text when no PSP font was imported; never presented as original typography.</summary>
    public void SetSubmenuFallback(SubmenuKind kind, int? focus)
    {
        FallbackLines.Clear();
        if (OriginalFontAvailable) return;
        var menu = frames[MainMenuFrame].Menu!;
        for (int row = 0; row < menu.Rows.Length; row++)
        {
            var label = menu.Rows[row].Label;
            FallbackLines.Add((Source(label.Source, label.Offset, label.Start, label.Chars).ToUpperInvariant(), (int)label.X[0], (int)label.Y + 3, Color.Gray));
        }
        var rows = submenuProfiles[kind].Rows;
        for (int row = 0; row < rows.Length; row++)
        {
            var label = rows[row].Label;
            FallbackLines.Add((Source(label.Source, label.Offset, label.Start, label.Chars).ToUpperInvariant(), (int)label.X[0], (int)label.Y + 3, row == focus ? Color.Black : Color.White));
        }
    }
}
