using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.PC.App.Visual.Flow;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Uploads the shared PGF glyph atlas and draws its line layout on the game thread; keeps the authored fallback text.</summary>
internal sealed class PgfTextRenderer : IDisposable
{
    internal const string RequiredFontSha256 = TagForceSpecial.Import.Resources.GameFonts.MenuSha256;
    private const int CellSize = 30;
    private readonly Action<Texture2D, FlowQuad, Color> drawGlyph;
    private readonly PgfGlyphAtlas? glyphs;
    private readonly Texture2D? atlas;
    private int uploadedVersion = -1;
    internal bool OriginalFontAvailable => glyphs is not null;
    internal List<(string Text, int X, int Y, Color Color)> FallbackLines { get; } = [];

    /// <summary>FTT-NewRodin Pro Latin (system ltn0): Deck Edit's card-face text, whose recorded glyph steps equal its advances x 0.2.</summary>
    internal const string CardTextFontSha256 = TagForceSpecial.Import.Resources.GameFonts.CardSha256;

    internal PgfTextRenderer(GraphicsDevice device, byte[]? fontFile, Action<Texture2D, FlowQuad, Color> drawGlyph, string requiredSha256 = RequiredFontSha256)
    {
        this.drawGlyph = drawGlyph;
        if (fontFile is not null)
        {
            glyphs = new(fontFile, requiredSha256);
            atlas = new(device, PgfGlyphAtlas.Size, PgfGlyphAtlas.Size);
        }
    }

    /// <summary>Width of a line using the PSP font's own advances; without the font, half a glyph cell per character.</summary>
    internal float TextWidth(string value, float scaleX) => glyphs?.TextWidth(value, scaleX) ?? value.Length * CellSize * scaleX / 2;

    internal void DrawLine(string value, float x, float y, float scaleX, float scaleY, int color, int shadowAlpha, float[]? measuredX = null, int alpha = 255)
    {
        if (glyphs is null || atlas is null)
        {
            FallbackLines.Add((value.ToUpperInvariant(), (int)x, (int)y + 3, new Color(color & 255, (color >> 8) & 255, (color >> 16) & 255)));
            return;
        }
        var quads = glyphs.Line(value, x, y, scaleX, scaleY, color, shadowAlpha, measuredX, alpha);
        if (glyphs.Version != uploadedVersion) { atlas.SetData(glyphs.Pixels); uploadedVersion = glyphs.Version; }
        foreach (var (quad, (r, g, b, a)) in quads) drawGlyph(atlas, quad, new Color(r, g, b, a));
    }

    public void Dispose() => atlas?.Dispose();
}
