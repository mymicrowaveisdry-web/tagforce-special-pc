using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Owns the pinned PGF glyph atlas, metrics, shadows and authored fallback text on the game thread.</summary>
internal sealed class PgfTextRenderer : IDisposable
{
    internal const string RequiredFontSha256 = "250babd8a242d7eaaa84284c78d5e2273b29e61d0d8c01b65414e3248d7c1164";
    private const int CellSize = 30, CellPitch = 32, BaselineRow = 24, GlyphLeft = 7;
    private const char Bullet = '\u25CF';
    private readonly Action<Texture2D, FlowQuad, Color> drawGlyph;
    private readonly PgfFont? font;
    private readonly Texture2D? atlas;
    private readonly Dictionary<char, (int X, int Y)> cells = [];
    private readonly Color[] atlasPixels = new Color[512 * 512];
    private bool atlasDirty;
    internal bool OriginalFontAvailable => font is not null;
    internal List<(string Text, int X, int Y, Color Color)> FallbackLines { get; } = [];

    internal PgfTextRenderer(GraphicsDevice device, byte[]? fontFile, Action<Texture2D, FlowQuad, Color> drawGlyph)
    {
        this.drawGlyph = drawGlyph;
        if (fontFile is not null)
        {
            var parsed = new PgfFont(fontFile);
            if (parsed.Sha256 != RequiredFontSha256) throw new InvalidDataException("PSP font file differs from the pinned system font.");
            font = parsed;
            atlas = new(device, 512, 512);
        }
    }

    /// <summary>Width of a line using the PSP font's own advances; without the font, half a glyph cell per character.</summary>
    internal float TextWidth(string value, float scaleX)
    {
        if (font is null) return value.Length * CellSize * scaleX / 2;
        return value.Sum(c => font.Glyph(c)?.AdvanceH26_6 / 64f * scaleX ?? CellSize * scaleX / 2);
    }

    internal void DrawLine(string value, float x, float y, float scaleX, float scaleY, int color, int shadowAlpha, float[]? measuredX = null)
    {
        if (font is null || atlas is null)
        {
            FallbackLines.Add((value.ToUpperInvariant(), (int)x, (int)y + 3, new Color(color & 255, (color >> 8) & 255, (color >> 16) & 255)));
            return;
        }
        foreach (char c in value) EnsureGlyph(c);
        if (atlasDirty) { atlas.SetData(atlasPixels); atlasDirty = false; }
        var main = new Color((color) & 255, (color >> 8) & 255, (color >> 16) & 255, 255);
        // Measured shadow: two copies at (+0,+1) and (+1,+1) beneath the opaque text.
        if (shadowAlpha > 0)
        {
            var shadow = new Color(0, 0, 0, shadowAlpha);
            Glyphs(value, x, y + 1, scaleX, scaleY, shadow, measuredX, 0);
            Glyphs(value, x + 1, y + 1, scaleX, scaleY, shadow, measuredX, 1);
        }
        Glyphs(value, x, y, scaleX, scaleY, main, measuredX, 0);
    }

    private void Glyphs(string value, float x, float y, float scaleX, float scaleY, Color color, float[]? measuredX, float measuredOffset)
    {
        float pen = x;
        float cellW = CellSize * scaleX, cellH = CellSize * scaleY;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            float at = measuredX is not null ? measuredX[i] + measuredOffset : pen;
            var (cx, cy) = cells[c];
            var quad = new FlowQuad(at, y, at + cellW, y + cellH, cx / 512f, cy / 512f, (cx + CellSize) / 512f, (cy + CellSize) / 512f, [255, 255, 255, 255], false);
            drawGlyph(atlas!, quad, color);
            pen += font!.Glyph(c)?.AdvanceH26_6 / 64f * scaleX ?? cellW / 2;
        }
    }

    private void EnsureGlyph(char c)
    {
        if (cells.ContainsKey(c)) return;
        int index = cells.Count;
        int cx = 1 + index % 16 * CellPitch, cy = 1 + index / 16 * CellPitch;
        if (cy + CellSize > 512) throw new InvalidOperationException("Glyph atlas budget exhausted.");
        cells[c] = (cx, cy);
        var glyph = font!.Glyph(c);
        if (glyph is null && c == Bullet) { DrawBullet(cx, cy); atlasDirty = true; return; }
        if (glyph is null || glyph.Width == 0) return;
        for (int y = 0; y < glyph.Height; y++)
            for (int x = 0; x < glyph.Width; x++)
            {
                int ax = cx + GlyphLeft + glyph.Left + x, ay = cy + BaselineRow - glyph.Top + y;
                if (ax < cx || ay < cy || ax >= cx + CellSize || ay >= cy + CellSize) continue;
                int coverage = glyph.Coverage4[y * glyph.Width + x] * 17;
                atlasPixels[ay * 512 + ax] = new Color(255, 255, 255, coverage);
            }
        atlasDirty = true;
    }

    /// <summary>The Latin font has no bullet. This disc is authored and recorded as a known mismatch, not original typography.</summary>
    private void DrawBullet(int cx, int cy)
    {
        const float centerX = GlyphLeft + 7.5f, centerY = BaselineRow - 7.5f, radius = 6.5f;
        for (int y = 0; y < CellSize; y++)
            for (int x = 0; x < CellSize; x++)
            {
                float distance = MathF.Sqrt((x + .5f - centerX) * (x + .5f - centerX) + (y + .5f - centerY) * (y + .5f - centerY));
                int coverage = (int)(Math.Clamp(radius + .5f - distance, 0, 1) * 255);
                atlasPixels[(cy + y) * 512 + cx + x] = new Color(255, 255, 255, coverage);
            }
    }

    public void Dispose() => atlas?.Dispose();
}
