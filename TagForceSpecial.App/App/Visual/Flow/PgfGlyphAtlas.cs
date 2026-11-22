using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Visual.Flow;

/// <summary>A pinned PGF font's glyphs in a 512 × 512 straight-alpha atlas (white, coverage in alpha) and the measured line layout:
/// advances from the font, optional measured per-character positions, and the two-copy shadow beneath the text.</summary>
public sealed class PgfGlyphAtlas
{
    public const int Size = 512;
    private const int CellSize = 30, CellPitch = 32, BaselineRow = 24, GlyphLeft = 7;
    private const char Bullet = '●';
    private readonly PgfFont font;
    private readonly Dictionary<char, (int X, int Y)> cells = [];

    /// <summary>RGBA bytes of the atlas; <see cref="Version"/> changes whenever glyphs are added.</summary>
    public byte[] Pixels { get; } = new byte[Size * Size * 4];
    public int Version { get; private set; }

    public PgfGlyphAtlas(byte[] fontFile, string requiredSha256)
    {
        var parsed = new PgfFont(fontFile);
        if (parsed.Sha256 != requiredSha256) throw new InvalidDataException("PSP font file differs from the pinned system font.");
        font = parsed;
    }

    /// <summary>Width of a line using the font's own advances.</summary>
    public float TextWidth(string value, float scaleX) => value.Sum(c => font.Glyph(c)?.AdvanceH26_6 / 64f * scaleX ?? CellSize * scaleX / 2);

    /// <summary>Glyph quads of one line in draw order: shadow copies at (+0,+1) and (+1,+1) first when <paramref name="shadowAlpha"/> is
    /// set, then the text. <paramref name="color"/> is 0xBBGGRR.</summary>
    public IReadOnlyList<(FlowQuad Quad, (int R, int G, int B, int A) Color)> Line(string value, float x, float y, float scaleX, float scaleY,
        int color, int shadowAlpha, float[]? measuredX = null, int alpha = 255)
    {
        foreach (char c in value) EnsureGlyph(c);
        var quads = new List<(FlowQuad, (int, int, int, int))>();
        if (shadowAlpha > 0)
        {
            Glyphs(quads, value, x, y + 1, scaleX, scaleY, (0, 0, 0, shadowAlpha), measuredX, 0);
            Glyphs(quads, value, x + 1, y + 1, scaleX, scaleY, (0, 0, 0, shadowAlpha), measuredX, 1);
        }
        Glyphs(quads, value, x, y, scaleX, scaleY, (color & 255, (color >> 8) & 255, (color >> 16) & 255, alpha), measuredX, 0);
        return quads;
    }

    private void Glyphs(List<(FlowQuad, (int, int, int, int))> output, string value, float x, float y, float scaleX, float scaleY,
        (int, int, int, int) color, float[]? measuredX, float measuredOffset)
    {
        float pen = x, cellW = CellSize * scaleX, cellH = CellSize * scaleY;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            float at = measuredX is not null ? measuredX[i] + measuredOffset : pen;
            var (cx, cy) = cells[c];
            output.Add((new FlowQuad(at, y, at + cellW, y + cellH, cx / (float)Size, cy / (float)Size, (cx + CellSize) / (float)Size,
                (cy + CellSize) / (float)Size, [255, 255, 255, 255], false), color));
            pen += font.Glyph(c)?.AdvanceH26_6 / 64f * scaleX ?? cellW / 2;
        }
    }

    private void EnsureGlyph(char c)
    {
        if (cells.ContainsKey(c)) return;
        int index = cells.Count;
        int cx = 1 + index % 16 * CellPitch, cy = 1 + index / 16 * CellPitch;
        if (cy + CellSize > Size) throw new InvalidOperationException("Glyph atlas budget exhausted.");
        cells[c] = (cx, cy);
        var glyph = font.Glyph(c);
        if (glyph is null && c == Bullet) { DrawBullet(cx, cy); Version++; return; }
        if (glyph is null || glyph.Width == 0) return;
        for (int y = 0; y < glyph.Height; y++)
            for (int x = 0; x < glyph.Width; x++)
            {
                int ax = cx + GlyphLeft + glyph.Left + x, ay = cy + BaselineRow - glyph.Top + y;
                if (ax < cx || ay < cy || ax >= cx + CellSize || ay >= cy + CellSize) continue;
                Set(ax, ay, glyph.Coverage4[y * glyph.Width + x] * 17);
            }
        Version++;
    }

    /// <summary>The Latin font has no bullet. This disc is authored and recorded as a known mismatch, not original typography.</summary>
    private void DrawBullet(int cx, int cy)
    {
        const float centerX = GlyphLeft + 7.5f, centerY = BaselineRow - 7.5f, radius = 6.5f;
        for (int y = 0; y < CellSize; y++)
            for (int x = 0; x < CellSize; x++)
            {
                float distance = MathF.Sqrt((x + .5f - centerX) * (x + .5f - centerX) + (y + .5f - centerY) * (y + .5f - centerY));
                Set(cx + x, cy + y, (int)(Math.Clamp(radius + .5f - distance, 0, 1) * 255));
            }
    }

    private void Set(int x, int y, int coverage)
    {
        int at = (y * Size + x) * 4;
        Pixels[at] = Pixels[at + 1] = Pixels[at + 2] = 255;
        Pixels[at + 3] = (byte)coverage;
    }
}
