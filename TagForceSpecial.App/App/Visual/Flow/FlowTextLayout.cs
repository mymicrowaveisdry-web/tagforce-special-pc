using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Visual.Flow;

/// <summary>PGF text as flow batches: measured runs use their recorded per-character positions when they cover the whole string.</summary>
public static class FlowTextLayout
{
    public static IEnumerable<FlowBatch> Run(PgfGlyphAtlas font, string fontKey, string value, FlowText run) =>
        Line(font, fontKey, value, run.X[0], run.Y, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha, run.X.Length == value.Length ? run.X : null);

    public static IEnumerable<FlowBatch> Line(PgfGlyphAtlas font, string fontKey, string value, float x, float y, float scaleX, float scaleY,
        int color, int shadowAlpha, float[]? measuredX = null, int alpha = 255) =>
        font.Line(value, x, y, scaleX, scaleY, color, shadowAlpha, measuredX, alpha)
            .Select(g => FlowDrawCompiler.Quad(fontKey, g.Quad, 0, 0, linear: true, g.Color)).ToArray();
}
