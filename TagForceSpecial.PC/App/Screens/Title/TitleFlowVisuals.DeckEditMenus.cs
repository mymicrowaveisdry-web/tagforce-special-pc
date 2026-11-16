using System.Globalization;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Deck Edit menu popups drawn over the Deck Edit layer from the measured menu profile (owner-approved partial, 16 Nov 2026).
/// Recorded stacking: Search, Deck &amp; Recipe, Change Layout, Change Music and Label Display draw over the Deck Edit Menu, whose row keeps
/// its pulsing focus band; Sort and Narrow Search stand alone; Order Normal/Inverse shows beside Sort with a marker on the current order;
/// Narrow Search grids draw over Narrow Search, whose row takes the marker band.</summary>
public sealed partial class TitleFlowVisuals
{
    public DeckEditMenuProfile DeckEditMenuProfile { get; } = DeckEditMenuProfile.LoadEmbedded();

    private static readonly Dictionary<string, string> DrawnOver = new(StringComparer.Ordinal)
    {
        ["search"] = "main", ["deck-recipe"] = "main", ["layout"] = "main", ["music"] = "main", ["label-display"] = "main", ["sort-order"] = "sort",
        ["card-frame"] = "narrow", ["type"] = "narrow", ["attribute"] = "narrow", ["level"] = "narrow", ["scale"] = "narrow",
        ["spell-trap-icon"] = "narrow", ["forbidden"] = "narrow", ["key-search"] = "narrow"
    };

    private enum MenuBand { None, Focus, Marker }

    private void DrawDeckEditMenus(DeckEditSession session)
    {
        var menus = session.Menus;
        if (menus.RunCount is { } count) { DrawRunNotice(count); return; }
        if (menus.Top is not { } top) return;
        int alpha = DeckEditMenuProfile.Pulse.AlphaAt(menus.PulseFrame);
        var stack = menus.Stack;
        if (DrawnOver.TryGetValue(top.Id, out var under) && stack.LastOrDefault(l => l.Id == under) is { } parent)
        {
            // Measured: a list parent keeps the focus band at the child's alpha; Narrow Search and Sort mark their row instead.
            var band = under is "narrow" or "sort" ? MenuBand.Marker : MenuBand.Focus;
            DrawPopup(session, parent, band, null, alpha);
        }
        int? marker = top.Id switch
        {
            "label-display" => (int)session.Query.LabelDisplay,
            "sort-order" => null,
            _ => null
        };
        DrawPopup(session, top, top.OnRun ? MenuBand.None : MenuBand.Focus, marker, alpha);
        if (top.Id == "sort") DrawPopup(session, new("sort-order", session.Query.Inverse ? 1 : 0), MenuBand.Marker, null, alpha);
    }

    private void DrawPopup(DeckEditSession session, DeckEditMenuLevel level, MenuBand band, int? marker, int alpha)
    {
        var popup = DeckEditMenuProfile[level.Id];
        DrawMeasured(popup.Shade); DrawMeasured(popup.Panel);
        if (level.Id == "main") DrawMenuHeader(popup, session);
        else foreach (var draw in popup.Chrome) DrawMeasured(draw);
        if (popup.IsGrid) DrawGrid(session, popup, level, band, alpha);
        else DrawRows(session, popup, level.Focus, band, marker, alpha);
        foreach (var text in popup.Texts) DrawMenuText(text, DeckText(text.Reference!, text.Start, text.Chars));
    }

    /// <summary>Main menu chrome: separators replay; the header card shows the focused card's frame and row image, and the label tag only
    /// when that card carries a label (inferred: the recorded card was labeled).</summary>
    private void DrawMenuHeader(DeckEditMenuPopup popup, DeckEditSession session)
    {
        var card = session.Focused?.Card;
        foreach (var draw in popup.Chrome)
        {
            string key = draw.AssetKey ?? "";
            if (key == "deck.all.ciss")
            {
                if (card is null) continue;
                DrawMeasured(Lookup("cardFrameByKind", card.Codes?.RawKind ?? 0, "card frame cell for kind") is { } frame ? WithUv(draw, frame) : draw);
            }
            else if (key.StartsWith("deck.all.cis-all.", StringComparison.Ordinal))
            {
                if (card?.SourceRow is { } source && DeckEditProfile.GetMiniImage(source.InternalIndex) is { } mini) DrawMeasured(WithUv(draw, mini.Uv, mini.AssetKey));
            }
            else if (key == "deck.all.icon04")
            {
                if (card is not null && session.Query.Labeled.Contains(card.GameCardId)) DrawMeasured(draw);
            }
            else DrawMeasured(draw);
        }
    }

    private void DrawRows(DeckEditSession session, DeckEditMenuPopup popup, int focus, MenuBand band, int? marker, int alpha)
    {
        var rows = popup.Rows!;
        var b = popup.Band!;
        var styleOn = rows.First(r => !r.Disabled).Text;
        var styleOff = DeckEditMenuProfile["main"].Rows!.First(r => r.Disabled).Text;
        void Band(int row, int[] rgb, int a)
        {
            var t = rows[row].Text;
            float y0 = MathF.Round(t.Y + b.DY0);
            foreach (var piece in DeckEditMenuProfile.Band(MathF.Round(t.X + b.DX0), y0, b.X1, y0 + b.Height, rgb, a)) DrawMeasured(piece);
        }
        if (marker is { } m) Band(m, DeckEditMenuProfile.BandTemplate.MarkerColor, DeckEditMenuProfile.BandTemplate.MarkerColor[3]);
        if (band == MenuBand.Focus) Band(focus, DeckEditMenuProfile.FocusColor, alpha);
        else if (band == MenuBand.Marker) Band(focus, DeckEditMenuProfile.BandTemplate.MarkerColor, DeckEditMenuProfile.BandTemplate.MarkerColor[3]);
        if (popup.Title is { } title) DrawMenuText(title, DeckText(title.Reference!, title.Start, title.Chars));
        for (int i = 0; i < rows.Length; i++)
        {
            foreach (var icon in rows[i].Icons) DrawMeasured(icon);
            var t = rows[i].Text;
            var style = session.Menus.IsDisabled(popup.Id, i, session) ? styleOff : styleOn;
            DrawLine(DeckText(t.Reference!, t.Start, t.Chars), t.X, t.Y, t.ScaleX, t.ScaleY, style.Color, style.ShadowAlpha);
        }
    }

    private void DrawGrid(DeckEditSession session, DeckEditMenuPopup popup, DeckEditMenuLevel level, MenuBand band, int alpha)
    {
        var cells = popup.Cells!;
        var p = DeckEditMenuProfile;
        var sel = p.SelectedCell;
        for (int i = 0; i < cells.Length; i++)
        {
            var box = cells[i].Box;
            if (session.Menus.IsSelected(popup.Id, i)) DrawMeasured(Shifted(sel.Draw, box[0] - sel.Cell[0], box[1] - sel.Cell[1]));
            if (band == MenuBand.Focus && i == level.Focus && !level.OnRun)
            {
                var o = popup.CellBand!;
                foreach (var piece in p.Band(box[0] + o[0], box[1] + o[1], box[0] + o[2], box[1] + o[3], p.FocusColor, alpha)) DrawMeasured(piece);
            }
            foreach (var part in cells[i].Parts) DrawMeasured(part);
        }
        // Observed (run-18-runfocus): the title bar is blank while Run is focused.
        if (!level.OnRun && popup.Title is { } title && GridTitle(popup, level.Focus) is { Length: > 0 } name) DrawMenuText(title, name);
        if (popup.Run is not { } run) return;
        if (level.OnRun)
        {
            float dx = run.Button.Points.Min(q => q[0]) - p.RunFocus.Rect[0], dy = run.Button.Points.Min(q => q[1]) - p.RunFocus.Rect[1];
            foreach (var piece in p.RunFocus.Pieces) DrawMeasured(Shifted(piece, dx, dy));
        }
        else DrawMeasured(run.Button);
        DrawMenuText(run.Label, DeckText(run.Label.Reference!, run.Label.Start, run.Label.Chars));
    }

    /// <summary>The focused cell's name: its source string, or for Level/Rank and Scale the number shown (ranges as "1~4"; inferred for
    /// cells not stepped in titles-a). Type names and DIVINE have no located source string and stay blank.</summary>
    private string GridTitle(DeckEditMenuPopup popup, int cell)
    {
        if (popup.CellNames is { } names && cell < names.Length && names[cell] is { } source) return DeckText(source);
        return popup.Id switch
        {
            "level" => cell switch
            {
                0 => "1~4", 1 => "5~6", 2 => "7~",
                < DeckEditQuery.LevelRanges + DeckEditQuery.Levels => (cell - DeckEditQuery.LevelRanges + 1).ToString(CultureInfo.InvariantCulture),
                _ => (cell - DeckEditQuery.LevelRanges - DeckEditQuery.Levels + 1).ToString(CultureInfo.InvariantCulture)
            },
            "scale" => (cell + 1).ToString(CultureInfo.InvariantCulture),
            _ => ""
        };
    }

    /// <summary>A row's label or a grid cell's title, as the popup shows it (for the PC notice title).</summary>
    public string DeckEditMenuLabel(string popupId, int row)
    {
        var popup = DeckEditMenuProfile[popupId];
        if (popup.IsGrid) return GridTitle(popup, row) is { Length: > 0 } name ? name : popup.Title?.Reference is { } t ? DeckText(t) : "";
        var text = popup.Rows![row].Text;
        return DeckText(text.Reference!, text.Start, text.Chars);
    }

    private void DrawMenuText(DeckEditMenuText run, string value) => DrawLine(value, run.X, run.Y, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha);

    /// <summary>"%d card(s) have met these conditions." with OK focused: the count in its recorded colour, then the measured lines.</summary>
    private void DrawRunNotice(int count)
    {
        var n = DeckEditMenuProfile.RunNotice;
        DrawMeasured(n.Shade); DrawMeasured(n.Panel);
        foreach (var piece in n.Button) DrawMeasured(piece);
        string digits = count.ToString(CultureInfo.InvariantCulture);
        float pitch = n.Number?.Pitch ?? 0;
        if (n.Number is { } number)
            DrawLine(digits, number.X, number.Y, number.ScaleX, number.ScaleY, number.Color, number.ShadowAlpha,
                pitch > 0 ? Enumerable.Range(0, digits.Length).Select(k => number.X + k * pitch).ToArray() : null);
        string message = DeckText(n.Message);
        for (int i = 0; i < n.Lines.Length; i++)
        {
            var line = n.Lines[i];
            // The first line follows the count's digit cells (recorded: three cells end exactly at its X).
            var placed = i == 0 && n.Number is { } at ? line with { X = at.X + digits.Length * (pitch > 0 ? pitch : TextWidth("0", at.ScaleX)) } : line;
            DrawMenuText(placed, message.Substring(line.Start, Math.Min(line.Chars ?? message.Length, message.Length - line.Start)));
        }
        DrawMenuText(n.Ok, DeckText(n.Ok.Reference!, n.Ok.Start, n.Ok.Chars));
    }
}
