namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>An original command window for <paramref name="Rows"/> with <paramref name="Selected"/> highlighted, anchored to an object's
/// truncated projected centre (<paramref name="CentreX"/>, <paramref name="CentreY"/>).</summary>
public sealed record DuelFieldMenu(DuelPart Part, IReadOnlyList<MenuItem> Rows, int Selected, float CentreX, float CentreY) : DuelDraw(Part);

/// <summary>A template draw's GE state repeated with generated through-mode sprite vertices and UVs.</summary>
public sealed record DuelFieldSprites(DuelPart Part, string Piece, int Index, float[][] Vertices, float[][] Uv) : DuelDraw(Part);

/// <summary>Builds the measured command window (13.2 captures: card, monster, Extra Deck, Deck and global menus): window04 frame with
/// fixed corners, stretched edges, 8-pixel interior strips and a 7 × 10 tail under the body; a dialog-panel highlight row inset 6; a
/// card_icon02 icon at (+6, +8) and card-font text (0.6 × 0.54, black, shadow 64) at (+16.8, +5.42) on a 15-pixel pitch.
/// Width = ⌈widest label + 29⌉; body height = 13 + 15 × rows; the tail ends 14 above the anchor; x clamped to 148–480.</summary>
public static class DuelMenuWindow
{
    public const int RowPitch = 15, Padding = 29, TailHeight = 6, AnchorGap = 14, LeftLimit = 148, RightLimit = 480;
    public const float TextX = 16.8f, TextY = 5.42f, TextScaleX = 0.6f, TextScaleY = 0.54f;

    public static (float X0, float Y0, float Width, float Height) Rect(DuelFieldMenu menu, IReadOnlyList<string> labels, Func<string, float, float> measure)
    {
        float width = MathF.Ceiling(labels.Max(l => measure(l, TextScaleX)) + Padding);
        float height = 13 + RowPitch * labels.Count;
        float bottom = (int)menu.CentreY - AnchorGap - TailHeight;
        float x0 = Math.Clamp((int)menu.CentreX - (int)width / 2, LeftLimit, RightLimit - width);
        return (x0, bottom - height, width, height);
    }

    public static IEnumerable<DuelDraw> Expand(DuelFieldMenu menu, DuelFieldProfile field, Func<string, float, float> measure, Func<int, string> text)
    {
        var labels = menu.Rows.Select(r => MenuVerbs.TextOffset(r.Verb) is { } offset ? text(offset) : r.Label).ToArray();
        var (x0, y0, w, h) = Rect(menu, labels, measure);
        float x1 = x0 + w, y1 = y0 + h;
        // Measured: a depth-only clear precedes the window, so it draws over the 3D cards.
        yield return new DuelFieldDraw(menu.Part, "menu.clear", 0);
        yield return Frame(menu.Part, field, "menu.window", x0, y0, x1, y1, Math.Clamp((int)menu.CentreX, x0 + 4, x1 - 11));
        foreach (var draw in Highlight(menu.Part, field, x0 + 6, y0 + 7 + RowPitch * menu.Selected, x1 - 6)) yield return draw;
        var icon = field.Piece("menu.icon")[0];
        var atlas = field.Textures[icon.Texture!].Size;
        for (int i = 0; i < labels.Length; i++)
        {
            float rowY = y0 + RowPitch * i;
            if (MenuVerbs.Icon(menu.Rows[i].Verb) is { } cell)
                yield return Sprite(menu.Part, "menu.icon", x0 + 6, rowY + 8, x0 + 19, rowY + 21,
                    cell.U / (float)atlas[0], cell.V / (float)atlas[1], (cell.U + 13) / (float)atlas[0], (cell.V + 13) / (float)atlas[1]);
            yield return new DuelFieldText(menu.Part, labels[i], x0 + TextX, rowY + TextY, TextScaleX, TextScaleY, 0x000000, 64);
        }
    }

    /// <summary>The window04 frame: template sprites 0–3 corners, 4 top, 5 left, 6 right, 7 bottom, 8 an 8-pixel interior strip, 17 the tail.</summary>
    public static DuelFieldSprites Frame(DuelPart part, DuelFieldProfile field, string piece, float x0, float y0, float x1, float y1, float? tailX)
    {
        var t = field.Piece(piece)[0];
        float[] Uv(int sprite) => [t.Uv![2 * sprite][0], t.Uv[2 * sprite][1], t.Uv[2 * sprite + 1][0], t.Uv[2 * sprite + 1][1]];
        // Generated sprites keep the template's depth: the original window passes the depth test over the 3D cards.
        float z = t.Vertices![0][2];
        var v = new List<float[]>(); var uv = new List<float[]>();
        void Add(float ax, float ay, float bx, float by, float[] u)
        {
            v.Add([ax, ay, z]); v.Add([bx, by, z]); uv.Add([u[0], u[1]]); uv.Add([u[2], u[3]]);
        }
        Add(x0, y0, x0 + 3, y0 + 3, Uv(0)); Add(x1 - 4, y0, x1, y0 + 3, Uv(1));
        Add(x0, y1 - 4, x0 + 3, y1, Uv(2)); Add(x1 - 4, y1 - 4, x1, y1, Uv(3));
        Add(x0 + 3, y0, x1 - 4, y0 + 3, Uv(4)); Add(x0, y0 + 3, x0 + 3, y1 - 4, Uv(5));
        Add(x1 - 4, y0 + 3, x1, y1 - 4, Uv(6)); Add(x0 + 3, y1 - 4, x1 - 4, y1, Uv(7));
        var strip = Uv(8);
        for (float y = y0 + 3; y < y1 - 4; y += 8)
        {
            float height = Math.Min(8, y1 - 4 - y);
            Add(x0 + 3, y, x1 - 4, y + height, [strip[0], strip[1], strip[2], strip[1] + (strip[3] - strip[1]) * height / 8]);
        }
        if (tailX is { } tail) Add(tail, y1 - 4, tail + 7, y1 + TailHeight, Uv(17));
        return new(part, piece, 0, [.. v], [.. uv]);
    }

    /// <summary>The highlight 9-slice: template draws pinned to the nearest edge of the template row (4-pixel corners), moved to the row.</summary>
    private static IEnumerable<DuelFieldSprites> Highlight(DuelPart part, DuelFieldProfile field, float x0, float y0, float x1)
    {
        var draws = field.Piece("menu.highlight");
        float tx0 = draws.Min(d => d.Vertices!.Min(p => p[0])), tx1 = draws.Max(d => d.Vertices!.Max(p => p[0]));
        float ty0 = draws.Min(d => d.Vertices!.Min(p => p[1])), ty1 = draws.Max(d => d.Vertices!.Max(p => p[1]));
        float y1 = y0 + (ty1 - ty0);
        float Pin(float value, float from0, float from1, float to0, float to1) => value - from0 <= from1 - value ? to0 + value - from0 : to1 - (from1 - value);
        for (int i = 0; i < draws.Length; i++)
            yield return new(part, "menu.highlight", i,
                draws[i].Vertices!.Select(p => new[] { Pin(p[0], tx0, tx1, x0, x1), Pin(p[1], ty0, ty1, y0, y1), p[2] }).ToArray(), draws[i].Uv!);
    }

    private static DuelFieldSprites Sprite(DuelPart part, string piece, float ax, float ay, float bx, float by, float u0, float v0, float u1, float v1) =>
        new(part, piece, 0, [[ax, ay, 0], [bx, by, 0]], [[u0, v0], [u1, v1]]);
}
