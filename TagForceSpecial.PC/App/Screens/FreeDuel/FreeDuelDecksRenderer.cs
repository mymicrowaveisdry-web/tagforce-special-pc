using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

/// <summary>Draws a deck picker into the Free Duel layer. Original rows the PC cannot play are drawn in grey (PC presentation); the PC row uses
/// the measured current-deck icon. Names shrink in 0.05 steps from 0.6 until they fit the measured width.</summary>
public sealed class FreeDuelDecksRenderer(GraphicsDevice device, TitleFlowVisuals visuals, FreeDuelDecksProfile profile)
{
    private const int Grey = 0x808080;
    private const float FirstRowTop = 56;
    public FreeDuelDecksProfile Profile => profile;
    public IReadOnlyList<string> Gaps { get; } = Array.AsReadOnly(new[]
    {
        "tabs not recreated: the opponent's distributed recipes (downloaded data) and the player's Character tab are not shown",
        "the two small icons on opponent rows are not drawn (their meaning is not decoded)",
        "fit width 106 inferred inside the measured range [103.95, 108.45)"
    });

    public void Render(RenderTarget2D layer, FreeDuelDeckPickerSession picker, int backgroundFrame) => visuals.Within(layer, () =>
    {
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, 1, 0);
        device.RasterizerState = RasterizerState.CullNone;
        device.DepthStencilState = DepthStencilState.None;
        var side = picker.Player ? profile.Player : profile.Opponent;
        visuals.DrawMenuBackdrop(backgroundFrame);
        Draw(side.Window);
        Run(side.TabLabel);
        int shown = Math.Min(profile.VisibleRows, picker.Entries.Count - picker.Top);
        for (int row = 0; row < shown; row++)
        {
            var entry = picker.Entries[picker.Top + row];
            float dy = row * profile.RowPitch;
            // Measured: the current deck's row uses the first row's panel, every other row the second row's (grey on the player's list).
            foreach (var d in entry.Enabled ? side.Panel : side.OtherPanel) visuals.DrawFlow(Shift(d, dy));
            visuals.DrawFlow(Shift(entry.Enabled ? side.CurrentIcon : side.OtherIcon, dy));
            Name(entry.Literal ?? Text(entry.Source!), side, dy, entry.Enabled ? side.NameColor : Grey);
            if (entry.Slot is int slot && side.SlotDigits is { } cells) Digits(cells, slot, dy);
            if (entry.Updated is DateTime when && side.Date is { } date) Date(date, when, dy, entry.Enabled ? date.Color : Grey);
        }
        foreach (var d in side.Cursor) visuals.DrawFlow(Shift(TexelCentre(d), (picker.Focus - picker.Top) * profile.RowPitch));
        DrawScroll(side, picker);
    });

    public string NoticeTitle(FreeDuelDeckPickerSession picker)
    {
        var e = picker.Entries[picker.Focus];
        return e.Literal ?? Text(e.Source!);
    }

    private void Name(string text, FreeDuelDeckSide side, float dy, int color)
    {
        float scale = profile.ScaleStart;
        while (scale > profile.ScaleStep * 2 && visuals.MeasureCardFontText(text, scale) > profile.FitWidth) scale = MathF.Round(scale - profile.ScaleStep, 2);
        visuals.DrawCardFontText(text, side.PenX - 7 * scale, side.NameY + dy, scale, 0.54f, color, side.NameShadow);
    }

    /// <summary>Measured slot number: three digit cells of mein_menu00, units first, digit d at u = 64 + 7d pixels.</summary>
    private static readonly int[] Powers = [1, 10, 100];
    private void Digits(FlowDraw[] cells, int slot, float dy)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            var d = cells[i];
            float minU = d.Uv.Min(q => q[0]), width = 64 / minU, shift = slot / Powers[i] % 10 * 7 / width;
            visuals.DrawFlow(Shift(d with { Uv = d.Uv.Select(q => new[] { q[0] + shift, q[1] }).ToArray() }, dy));
        }
    }

    /// <summary>Measured fixed-pitch date: yy/MM/dd, hour, raised colon, minutes, each glyph at its captured position.</summary>
    private void Date(FreeDuelDateStyle s, DateTime when, float dy, int color)
    {
        // DY is measured from the top of its row (the captured row at 85 px), so it applies to the first row's top (56 px) plus the row offset.
        string text = when.ToString("yy/MM/ddHHmm", CultureInfo.InvariantCulture);
        for (int i = 0; i < text.Length && i < s.X.Length; i++)
            visuals.DrawCardFontText(text[i].ToString(), s.X[i], FirstRowTop + s.DY + dy, s.ScaleX, s.ScaleY, color, s.ShadowAlpha);
        visuals.DrawCardFontText(":", s.ColonX, FirstRowTop + s.ColonDY + dy, s.ScaleX, s.ScaleY, color, s.ShadowAlpha);
    }

    private void DrawScroll(FreeDuelDeckSide side, FreeDuelDeckPickerSession picker)
    {
        var (top, length) = picker.Thumb(profile.TrackTop, profile.TrackLength);
        // The thumb is the three draws at x 305 (top cap, middle, bottom cap by measured y); everything else is drawn as captured.
        var thumb = side.Scroll.Where(d => Math.Abs(d.Points.Min(q => q[0]) - 305) < 0.5f).OrderBy(d => d.Points.Min(q => q[1])).ToList();
        foreach (var d in side.Scroll.Except(thumb)) visuals.DrawFlow(d);
        for (int i = 0; i < thumb.Count; i++)
        {
            var d = thumb[i];
            float min = d.Points.Min(q => q[1]), max = d.Points.Max(q => q[1]);
            bool first = i == 0, last = i == thumb.Count - 1;
            float y0 = first ? top : last ? top + length - 3 : top + 3, y1 = first ? top + 3 : last ? top + length : top + length - 3;
            visuals.DrawFlow(d with { Points = d.Points.Select(q => new[] { q[0], y0 + (q[1] - min) / Math.Max(1e-3f, max - min) * (y1 - y0) }).ToArray() });
        }
    }

    /// <summary>A piece whose texture coordinates have no extent samples one texel; the PSP takes the texel at that coordinate (trnsmt01,
    /// 256 x 128), so sample its centre instead of a texel boundary.</summary>
    private static FlowDraw TexelCentre(FlowDraw d)
    {
        bool flat = d.Uv.Max(q => q[0]) - d.Uv.Min(q => q[0]) < 1e-6f && d.Uv.Max(q => q[1]) - d.Uv.Min(q => q[1]) < 1e-6f;
        return flat ? d with { Uv = d.Uv.Select(q => new[] { q[0] + 0.5f / 256, q[1] + 0.5f / 128 }).ToArray() } : d;
    }

    private static FlowDraw Shift(FlowDraw d, float dy) => d with { Points = d.Points.Select(q => new[] { q[0], q[1] + dy }).ToArray() };

    private void Run(FlowText run) =>
        visuals.DrawCardFontText(Text(new(run.Source, run.Offset, run.Start, run.Chars)), run.X[0], run.Y, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha);

    private string Text(FlowTextSource source)
    {
        string message = visuals.Message(source.Source, source.Offset);
        return source.Start + source.Chars <= message.Length ? message.Substring(source.Start, source.Chars)
            : throw new InvalidDataException("Free Duel deck text reference outside its message.");
    }

    private void Draw(IEnumerable<FlowDraw> draws) { foreach (var d in draws) visuals.DrawFlow(d); }
}
