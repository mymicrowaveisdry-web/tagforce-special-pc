using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

/// <summary>Draws the character picker into the Free Duel layer. Owner decisions (18 Nov 2026): every character is listed (the unlocked
/// reference state), and the record slots show the PC's empty record: counters 0 and no rating (as the original draws characters without one).</summary>
public sealed class FreeDuelPickerRenderer(GraphicsDevice device, TitleFlowVisuals visuals, FreeDuelPickerProfile profile)
{
    public FreeDuelPickerProfile Profile => profile;
    public IReadOnlyList<string> Gaps { get; } = Array.AsReadOnly(new[]
    {
        "\"No.\" prefix composed as No.%03d (measured text; the original format string is not located)",
        "scroll arrows drawn with their measured overlay (blink period not measured)"
    });

    public void Render(RenderTarget2D layer, FreeDuelPickerSession picker, int backgroundFrame) => visuals.Within(layer, () =>
    {
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, 1, 0);
        device.RasterizerState = RasterizerState.CullNone;
        device.DepthStencilState = DepthStencilState.None;
        var p = profile; var t = p.Text;
        visuals.DrawMenuBackdrop(backgroundFrame);
        Draw(p.Window);
        Draw(p.Tabs[picker.Series - 1].Strip);
        for (int tab = 0; tab < FreeDuelPickerProfile.SeriesCount; tab++)
        {
            var style = tab == picker.Series - 1 ? t.TabActive : t.TabInactive;
            Line(Text(p.TabLabels[tab]), p.TabLabelX[tab], style, 0);
        }
        var list = picker.List;
        int shown = Math.Min(p.VisibleRows, list.Count - picker.Top);
        for (int row = 0; row < shown; row++)
        {
            int index = list[picker.Top + row];
            var c = p.Characters[index];
            float dy = row * p.RowPitch;
            foreach (var d in p.Row) visuals.DrawFlow(Shift(d, dy));
            visuals.DrawFlow(Cell(Shift(p.Portrait, dy), c.Bust < p.PortraitPerAtlas ? "title.flow.sml-bup01" : "title.flow.sml-bup11",
                c.Bust % p.PortraitPerAtlas, p.PortraitColumns, p.PortraitCell, p.PortraitCell, 512, c.Bust < p.PortraitPerAtlas ? 512 : 256));
            visuals.DrawFlow(Cell(Shift(p.Sprite, dy), $"title.flow.sddb-0{c.Series}", c.Rank, p.SpriteColumns, p.SpriteWidth, p.SpriteHeight, 256, 256));
            Line("No." + FreeDuelPickerProfile.Number(index).ToString("000", CultureInfo.InvariantCulture), t.Number.X, t.Number, dy);
            Line(Text(p.NameLabel), t.NameLabel.X, t.NameLabel, dy);
            Line(Text(c.Name), t.Name.X, t.Name, dy);
            Line(Text(c.Team), t.Team.X, t.Team, dy);
            foreach (var counter in t.Counters) Line("0", counter.X, counter, dy);
        }
        foreach (var d in p.Cursor) visuals.DrawFlow(Shift(d, (picker.Focus - picker.Top) * p.RowPitch));
        DrawScroll(picker);
    });

    /// <summary>Track and arrows as measured; the thumb's caps and middle (parts v 40 / 45 / 55) placed by the measured rule.</summary>
    private void DrawScroll(FreeDuelPickerSession picker)
    {
        var (top, length) = picker.Thumb();
        foreach (var d in profile.Scroll)
        {
            float v = MathF.Round(d.Uv[0][1] * 128);
            if (Math.Abs(Left(d) - 436) > 0.5f || v is not (40 or 45 or 50 or 55)) { visuals.DrawFlow(d); continue; }
            float y0 = v == 40 ? top : v == 55 ? top + length - 5 : top + 5, y1 = v == 40 ? top + 5 : v == 55 ? top + length : top + length - 5;
            visuals.DrawFlow(Place(d, y0, y1));
        }
    }

    private static float Left(FlowDraw d) => d.Points.Min(q => q[0]);

    private static FlowDraw Place(FlowDraw d, float y0, float y1)
    {
        float min = d.Points.Min(q => q[1]), max = d.Points.Max(q => q[1]);
        return d with { Points = d.Points.Select(q => new[] { q[0], y0 + (q[1] - min) / Math.Max(1e-3f, max - min) * (y1 - y0) }).ToArray() };
    }

    /// <summary>The draw retargeted to one atlas cell: column = cell % columns, row = cell / columns, in pixels of an atlas of the given size.</summary>
    private static FlowDraw Cell(FlowDraw d, string key, int cell, int columns, int width, int height, int atlasWidth, int atlasHeight)
    {
        float u0 = cell % columns * width / (float)atlasWidth, v0 = cell / columns * height / (float)atlasHeight;
        float du = width / (float)atlasWidth, dv = height / (float)atlasHeight;
        float minU = d.Uv.Min(q => q[0]), maxU = d.Uv.Max(q => q[0]), minV = d.Uv.Min(q => q[1]), maxV = d.Uv.Max(q => q[1]);
        return d with
        {
            AssetKey = key,
            Uv = d.Uv.Select(q => new[] { u0 + (q[0] - minU) / Math.Max(1e-6f, maxU - minU) * du, v0 + (q[1] - minV) / Math.Max(1e-6f, maxV - minV) * dv }).ToArray()
        };
    }

    private static FlowDraw Shift(FlowDraw d, float dy) => d with { Points = d.Points.Select(q => new[] { q[0], q[1] + dy }).ToArray() };

    private void Line(string text, float x, FreeDuelTextStyle style, float dy)
    {
        if (text.Length > 0) visuals.DrawCardFontText(text, x, style.Y + dy, style.ScaleX, style.ScaleY, style.Color, style.ShadowAlpha);
    }

    private string Text(FlowTextSource source)
    {
        string message = visuals.Message(source.Source, source.Offset);
        return source.Start + source.Chars <= message.Length ? message.Substring(source.Start, source.Chars)
            : throw new InvalidDataException("Free Duel picker text reference outside its message.");
    }

    private void Draw(IEnumerable<FlowDraw> draws) { foreach (var d in draws) visuals.DrawFlow(d); }
}
