using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Cards;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.Album;

/// <summary>Draws the card detail into the album's layer in the measured order: background and panels, the card composite, header (name,
/// icons, level, type line, attribute and stats), tab title and counters, the scrolled tab content clipped to its box, the scroll thumb,
/// the Spell Speed panel and the two tab buttons. Data (genres, packs, Spell Speed) comes from <see cref="CardDetailSource"/>.</summary>
public sealed class CardAlbumDetailRenderer
{
    private const float IconTexture = 128, IconTextureHeight = 256, IconCell = 16;
    private static readonly RasterizerState Scissored = new() { CullMode = CullMode.None, ScissorTestEnable = true };
    private readonly GraphicsDevice device;
    private readonly TitleFlowVisuals visuals;
    private readonly CardAlbumRenderer album;
    private readonly CardCatalog catalog;
    public CardAlbumDetailProfile Profile { get; }
    public CardDetailSource Source { get; }

    public CardAlbumDetailRenderer(GraphicsDevice device, TitleFlowVisuals visuals, CardAlbumRenderer album, CardCatalog catalog, CardAlbumDetailProfile profile,
        CardDetailSource source)
    {
        this.device = device; this.visuals = visuals; this.album = album; this.catalog = catalog; Profile = profile; Source = source;
    }

    /// <summary>Content height of a tab, the basis of its scroll range (EBOOT 0x17744): wrapped description lines, 24 px per genre row,
    /// 23 px per pack row less 6.</summary>
    public float ContentHeight(uint cardId, CardAlbumDetailTab tab)
    {
        var card = catalog[cardId];
        var s = Profile.Scroll;
        return tab switch
        {
            CardAlbumDetailTab.Description => DescriptionLines(card).Count * Profile.Body.LineHeight,
            CardAlbumDetailTab.Genre => Genres(card).Count * s.GenreRow,
            _ => Source.PacksOf(cardId) is { Count: > 0 } packs ? packs.Count * s.PackRowCounted - s.PackTrailing : 0
        };
    }

    private List<string> DescriptionLines(CardDefinition card) => Wrap(card.Description, Profile.Body.Width, Profile.Body.ScaleX);

    /// <summary>Greedy wrap at the decoded 332 px with the card-text font's advances; a line may also break after a hyphen (as measured on
    /// the album and here: "non-" / "Tuner" stays together only when it fits).</summary>
    private List<string> Wrap(string text, float width, float scaleX)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                var parts = System.Text.RegularExpressions.Regex.Split(word, "(?<=-)(?=.)");
                for (int i = 0; i < parts.Length; i++)
                {
                    string glue = line.Length == 0 || i > 0 ? "" : " ";
                    string next = line + glue + parts[i];
                    if (line.Length > 0 && visuals.MeasureCardFontText(next, scaleX) > width) { lines.Add(line); line = parts[i]; }
                    else line = next;
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    private IReadOnlyList<int> Genres(CardDefinition card) => Source.GenresOf(card.GameCardId, card.SourceRow?.InternalIndex ?? -1);

    public void Draw(CardAlbumDetail detail)
    {
        if (detail.Closing) return; // measured: one blank frame
        var p = Profile;
        var card = catalog[detail.Card];
        foreach (var draw in p.Static) visuals.DrawFlow(draw);
        visuals.DrawCardComposite(card);
        DrawHeader(card);
        visuals.DrawRun(p.Titles[CardAlbumDetailProfile.Tabs[(int)detail.Tab]]);
        int[] counts = [detail.Counts.Main, detail.Counts.Side, detail.Counts.Trunk];
        for (int i = 0; i < counts.Length; i++)
        {
            // Measured single digits; inferred: wider counts grow to the left at the album counter's 9 px pitch.
            string text = Math.Max(0, counts[i]).ToString(CultureInfo.InvariantCulture);
            var c = p.Counters;
            visuals.DrawText(text, c.X[i] - (text.Length - 1) * 9f, c.Y, c.ScaleX, c.ScaleY, c.Color, c.ShadowAlpha);
        }
        var s = p.Body.Scissor;
        device.ScissorRectangle = new Rectangle(s[0], s[1], s[2] - s[0] + 1, s[3] - s[1] + 1);
        device.RasterizerState = Scissored;
        switch (detail.Tab)
        {
            case CardAlbumDetailTab.Description: DrawDescription(card, detail.Scroll); break;
            case CardAlbumDetailTab.Genre: DrawGenreRows(card, detail.Scroll); break;
            default: DrawPackRows(card, detail.Scroll); break;
        }
        device.RasterizerState = RasterizerState.CullNone;
        DrawThumb(detail);
        DrawSpeedPanel(card);
        string previous = CardAlbumDetailProfile.Tabs[((int)detail.Tab + 2) % 3], next = CardAlbumDetailProfile.Tabs[((int)detail.Tab + 1) % 3];
        visuals.DrawRun(p.Buttons[previous] with { Y = p.ButtonY[0] });
        visuals.DrawRun(p.Buttons[next] with { Y = p.ButtonY[1] });
    }

    private void DrawHeader(CardDefinition card)
    {
        var p = Profile;
        var n = p.Name;
        // Inferred: a long name shrinks like the Pack List names (only names that fit were captured).
        float nameScale = Fit(card.Name, n.ScaleX);
        visuals.DrawCardFontText(card.Name, n.X[0] + 7 * (n.ScaleX - nameScale), n.Y, nameScale, n.ScaleY, n.Color, n.ShadowAlpha);
        var (attribute, type) = album.IconCells(card);
        if (attribute is not null) visuals.DrawFlow(CardAlbumRenderer.Quad(p.AttributeIconRect, attribute));
        if (type is not null) visuals.DrawFlow(CardAlbumRenderer.Quad(p.TypeIconRect, type));
        var t = p.TypeLine;
        if (album.TypeLine(card) is { } line) visuals.DrawCardFontText(line, t.X[0], t.Y, t.ScaleX, t.ScaleY, t.Color, t.ShadowAlpha);
        if (card.Codes is not { } codes || codes.RawKind is 13 or 14) return;
        var kind = Source.KindRow(codes.GetterKind);
        if (card.Statistics.Level > 0)
        {
            var star = kind.Frame == CardDetailSource.XyzFrame ? p.RankStar : p.LevelStar;
            visuals.DrawFlow(CardAlbumRenderer.Quad(star.Rect, star.Uv));
            DrawGlyphs("x" + card.Statistics.Level.ToString(CultureInfo.InvariantCulture), p.LevelText);
        }
        if (kind.Pendulum && codes.Field23To26 > 0)
        {
            visuals.DrawFlow(CardAlbumRenderer.Quad(p.ScaleIcon.Rect, p.ScaleIcon.Uv));
            DrawGlyphs("x" + codes.Field23To26.ToString(CultureInfo.InvariantCulture), p.ScaleText);
        }
        if (codes.Attribute is >= 1 and <= 7)
        {
            var a = p.Attribute;
            visuals.DrawCardFontText(visuals.Message(a.Source, a.Words[codes.Attribute - 1]), a.X, t.Y, t.ScaleX, t.ScaleY, a.Color, t.ShadowAlpha);
        }
        var st = p.Stats;
        for (int i = 0; i < 4; i++)
        {
            visuals.DrawText("ATK/"[i].ToString(), st.AtkLabelX[i], st.Y, st.ScaleX, st.ScaleY, st.Color, st.ShadowAlpha);
            visuals.DrawText("DEF/"[i].ToString(), st.DefLabelX[i], st.Y, st.ScaleX, st.ScaleY, st.Color, st.ShadowAlpha);
        }
        DrawRightAligned(Stat(card.Statistics.Attack), st.AtkLastDigitX, st);
        DrawRightAligned(Stat(card.Statistics.Defense), st.DefLastDigitX, st);
    }

    private static string Stat(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "?";

    private void DrawRightAligned(string value, float lastX, CardDetailStats st)
    {
        for (int i = 0; i < value.Length; i++)
            visuals.DrawText(value[i].ToString(), lastX - (value.Length - 1 - i) * st.DigitPitch, st.Y, st.ScaleX, st.ScaleY, st.Color, st.ShadowAlpha);
    }

    private void DrawGlyphs(string text, CardAlbumGlyphs at)
    {
        float pitch = at.X.Length > 1 ? at.X[^1] - at.X[^2] : 6;
        for (int i = 0; i < text.Length; i++)
            visuals.DrawText(text[i].ToString(), i < at.X.Length ? at.X[i] : at.X[^1] + (i - at.X.Length + 1) * pitch, at.Y, at.ScaleX, at.ScaleY, at.Color, at.ShadowAlpha);
    }

    /// <summary>Largest scale from <paramref name="start"/> down in the measured 0.05 steps whose width stays under the measured 278 px.</summary>
    private float Fit(string text, float start)
    {
        var r = Profile.PackRows;
        float scale = start;
        while (scale > r.ScaleStep && visuals.MeasureCardFontText(text, scale) >= r.MaxWidth) scale = MathF.Round(scale - r.ScaleStep, 2);
        return scale;
    }

    private void DrawDescription(CardDefinition card, float scroll)
    {
        var b = Profile.Body;
        var lines = DescriptionLines(card);
        for (int i = 0; i < lines.Count; i++)
        {
            float y = b.Top + b.FirstBaselineOffset - scroll + i * b.LineHeight;
            if (y + b.LineHeight < b.Scissor[1] || y > b.Scissor[3]) continue;
            if (lines[i].Length > 0) visuals.DrawCardFontText(lines[i], b.PenX + b.GlyphOffset * b.ScaleX, y, b.ScaleX, b.ScaleY, b.Color, b.ShadowAlpha);
        }
    }

    private FlowDraw Icon(FlowDraw at, int label, float dx, float dy)
    {
        var (u, v) = CardDetailSource.IconCell(label);
        float u0 = u / IconTexture, v0 = v / IconTextureHeight, u1 = (u + IconCell) / IconTexture, v1 = (v + IconCell) / IconTextureHeight;
        float minU = at.Uv.Min(q => q[0]), minV = at.Uv.Min(q => q[1]);
        var moved = CardAlbumRenderer.Shift(at, dx, dy);
        return moved with { Uv = at.Uv.Select(q => new[] { q[0] == minU ? u0 : u1, q[1] == minV ? v0 : v1 }).ToArray() };
    }

    private void DrawGenreRows(CardDefinition card, float scroll)
    {
        var g = Profile.GenreRows;
        var genres = Genres(card);
        for (int k = 0; k < genres.Count; k++)
        {
            float dy = k * g.IconPitch - scroll;
            visuals.DrawFlow(Icon(g.Icon, genres[k], 0, dy));
            visuals.DrawCardFontText(Source.Labels[genres[k]], g.LabelX, g.LabelY + dy, g.ScaleX, g.ScaleY, g.Color, g.ShadowAlpha);
        }
    }

    private void DrawPackRows(CardDefinition card, float scroll)
    {
        var r = Profile.PackRows;
        var packs = Source.PacksOf(card.GameCardId);
        for (int k = 0; k < packs.Count; k++)
        {
            float dy = k * r.Pitch - scroll;
            if (r.Y + dy > Profile.Body.Scissor[3] + r.Pitch || r.Y + dy + r.Pitch < Profile.Body.Scissor[1] - r.Pitch) continue;
            foreach (var piece in r.Frame) visuals.DrawFlow(CardAlbumRenderer.Shift(piece, 0, dy));
            visuals.DrawFlow(CardAlbumRenderer.Shift(r.Icon, 0, dy));
            string name = Source.PackNames[packs[k].Pack];
            float scale = Fit(name, r.StartScale);
            visuals.DrawCardFontText(name, r.PenX + r.GlyphOffset * scale, r.Y + dy, scale, r.ScaleY, r.Color, r.ShadowAlpha);
            if (packs[k].Rarity is >= 1 and <= 3) visuals.DrawFlow(CardAlbumRenderer.Shift(r.Badges[packs[k].Rarity - 1], 0, dy));
        }
    }

    /// <summary>Thumb caps and middle placed from the measured full-length thumb at the decoded offset and length.</summary>
    private void DrawThumb(CardAlbumDetail detail)
    {
        var track = Profile.Track;
        var (offset, length) = detail.Thumb(track.Length);
        float top = track.Top + offset, bottom = top + MathF.Round(length);
        var t = Profile.Thumb; // top cap, bottom cap, middle at full length
        float cap = t[0].Points.Max(q => q[1]) - t[0].Points.Min(q => q[1]);
        visuals.DrawFlow(Span(t[0], top, top + cap));
        visuals.DrawFlow(Span(t[1], bottom - cap, bottom));
        visuals.DrawFlow(Span(t[2], top + cap, bottom - cap));
    }

    private static FlowDraw Span(FlowDraw draw, float y0, float y1)
    {
        float min = draw.Points.Min(q => q[1]);
        return draw with { Points = draw.Points.Select(q => new[] { q[0], q[1] == min ? y0 : y1 }).ToArray() };
    }

    private void DrawSpeedPanel(CardDefinition card)
    {
        var sp = Profile.Speed;
        var genres = Genres(card);
        for (int n = 0; n < genres.Count; n++)
            visuals.DrawFlow(Icon(sp.GenreIcon, genres[n], n % sp.IconsPerRow * sp.IconPitch, n / sp.IconsPerRow * sp.IconPitch));
        if (card.Codes is not { } codes || Source.SpellSpeed(card.GameCardId, codes.Type, codes.Icon, codes.GetterKind) is not int speed) return;
        visuals.DrawRun(sp.Label);
        // Measured speeds 1 and 2; speed 3 continues the 12-texel step (inferred from the cell layout).
        visuals.DrawFlow(sp.Sprite with { Uv = sp.Sprite.Uv.Select(q => new[] { q[0], q[1] + (speed - 1) * sp.SpriteStepV }).ToArray() });
    }
}
