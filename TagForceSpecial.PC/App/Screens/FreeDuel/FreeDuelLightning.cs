using Microsoft.Xna.Framework;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

internal static class FreeDuelLightning
{
    private const int Segments = 8;
    private static readonly Color Teal = new(108, 237, 204);
    private static readonly Color Violet = new(209, 154, 255);

    internal static void Draw(TitleFlowVisuals visuals, int frame, float centerY)
    {
        float seconds = frame / (float)MainMenuEntry.GameFramesPerSecond;
        var points = new List<float[]>();
        var colors = new List<uint>();
        AddBolt(points, colors, new(0, centerY + 29), new(218, centerY), seconds, Teal);
        AddBolt(points, colors, new(480, centerY - 28), new(262, centerY), seconds + 0.43f, Violet);

        visuals.DrawFlow(new FlowDraw(null, 3, points.ToArray(), [], [255, 255, 255, 255], colors.ToArray(), null,
            Additive: true, LinearSampling: false, ClearStencil: false, WriteMask: false, Stencil: false));

        const string caption = "VS";
        const float scale = 1.2f;
        float width = visuals.MeasureCardFontText(caption, scale);
        visuals.DrawCardFontText(caption, 240 - width / 2 - 5, centerY - 24, scale, scale, 0xffffff, 220);
    }

    private static void AddBolt(List<float[]> points, List<uint> colors, Vector2 start, Vector2 end, float time, Color tint)
    {
        Vector2 previous = start;
        for (int i = 1; i <= Segments; i++)
        {
            float fraction = (float)i / Segments;
            Vector2 next = Vector2.Lerp(start, end, fraction);
            float zigzag = (i % 2 == 0 ? -1 : 1) * (6 + 4 * MathF.Sin(time * 5 + i * 0.9f));
            next.Y += zigzag * MathF.Sin(MathF.PI * fraction);

            float crest = 0.5f + 0.5f * MathF.Cos(2 * MathF.PI * (fraction - time * 0.8f));
            float strength = 0.45f + 0.55f * crest * crest;
            Color core = Color.Lerp(tint, Color.White, 0.35f);
            core.A = (byte)(220 * strength);
            Color glow = tint;
            glow.A = (byte)(50 * strength);
            AddRibbon(points, colors, previous, next, 3.5f, glow);
            AddRibbon(points, colors, previous, next, 0.9f, core);
            previous = next;
        }
    }

    private static void AddRibbon(List<float[]> points, List<uint> colors, Vector2 start, Vector2 end, float radius, Color color)
    {
        var upperStart = new[] { start.X, start.Y - radius };
        var lowerStart = new[] { start.X, start.Y + radius };
        var upperEnd = new[] { end.X, end.Y - radius };
        var lowerEnd = new[] { end.X, end.Y + radius };
        points.AddRange([upperStart, lowerStart, upperEnd, upperEnd, lowerStart, lowerEnd]);
        for (int i = 0; i < 6; i++) colors.Add(color.PackedValue);
    }
}
