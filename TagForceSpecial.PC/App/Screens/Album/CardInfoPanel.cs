using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Cards;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.Album;

/// <summary>The left card panel shared by the Card Album and the Duel Field (same measured geometry in both): the preview card composite or a
/// back, the info strip with its moving glint, text box and scroll bar, the strip's ATK/DEF, attribute and type icons, the level star, and
/// the clipped name, type line and card text.</summary>
public sealed class CardInfoPanel(GraphicsDevice device, TitleFlowVisuals visuals, CardAlbumProfile profile)
{
    private static readonly RasterizerState Scissored = new() { CullMode = CullMode.None, ScissorTestEnable = true };
    public CardAlbumProfile Profile => profile;

    /// <summary>Draws the panel for <paramref name="card"/>, or with <paramref name="back"/> the card back (<paramref name="drawBack"/> when the
    /// caller has its own back, else the album's) and no text. <paramref name="stripTint"/> replaces the strip colour (the duel tints it by
    /// card kind).</summary>
    public void Draw(CardDefinition? card, bool back, int glintFrame, Color? stripTint = null, Action? drawBack = null)
    {
        if (card is not null) visuals.DrawCardComposite(card);
        else if (back) { if (drawBack is not null) drawBack(); else visuals.DrawFlow(profile.CardBack.Draw); }
        // Measured order: strip, text box and scroll bar, then the text glyphs over the box.
        var glint = profile.Glint;
        string strip = profile.Panel[0].AssetKey!;
        foreach (var draw in profile.Panel)
        {
            var piece = draw.AssetKey == glint.AssetKey ? CardAlbumRenderer.Shift(draw, glint.Left(glintFrame) - glint.MeasuredLeft, 0) : draw;
            // Measured in the duel (through-mode vertex colour, no additive term): the tint replaces the album strip's colour and its add.
            if (stripTint is { } tint && draw.AssetKey == strip) piece = piece with { Color = [tint.R, tint.G, tint.B, draw.Color[3]], Add = null };
            visuals.DrawFlow(piece);
        }
        if (card is null) return;
        visuals.DrawCardStats(card); DrawIcons(card); DrawLevel(card, tunerYellow: stripTint is not null);
        // Measured: the info text is clipped to the text box (GE scissor on its draws).
        var s = profile.InfoScissor;
        var previous = device.RasterizerState;
        var previousScissor = device.ScissorRectangle;
        device.ScissorRectangle = new Rectangle(s[0], s[1], s[2] - s[0] + 1, s[3] - s[1] + 1);
        device.RasterizerState = Scissored;
        try { DrawInfo(card); }
        finally
        {
            device.ScissorRectangle = previousScissor;
            device.RasterizerState = previous ?? RasterizerState.CullNone;
        }
    }

    /// <summary>The two compact panels of the duel's attack target (aki-attack draws 210–289). Measured against this panel: the strip group
    /// (strip, glint, ATK/DEF, icons) moves by <see cref="CardInfoCompactLayout.StripDy"/>; the text box group keeps its bottom while its top
    /// rises 7 (items above y 200), then moves by <see cref="CardInfoCompactLayout.BoxDy"/>; the Level row moves by its own offset; nine text
    /// lines. Each card's art lies 78 × 78 under its box.</summary>
    public void DrawCompact(CardDefinition? card, CardInfoCompactLayout layout, int glintFrame, Color stripTint)
    {
        var glint = profile.Glint;
        string strip = profile.Panel[0].AssetKey!;
        foreach (var draw in profile.Panel.Where(d => d.AssetKey == strip || d.AssetKey == glint.AssetKey))
        {
            if (draw.AssetKey == glint.AssetKey && !layout.Glint) continue; // measured: the attacker's glint is invisible (alpha 0)
            var piece = draw.AssetKey == glint.AssetKey ? CardAlbumRenderer.Shift(draw, glint.Left(glintFrame) - glint.MeasuredLeft, layout.StripDy)
                : CardAlbumRenderer.Shift(draw, 0, layout.StripDy) with { Color = [stripTint.R, stripTint.G, stripTint.B, draw.Color[3]], Add = null };
            visuals.DrawFlow(piece);
        }
        if (card is not null) { visuals.DrawCardStats(card, layout.StripDy); DrawIcons(card, layout.StripDy); DrawLevel(card, layout.LevelDx, layout.LevelDy, tunerYellow: true); }
        foreach (var draw in profile.Panel.Where(d => d.AssetKey != strip && d.AssetKey != glint.AssetKey))
            visuals.DrawFlow(draw with { Points = draw.Points.Select(p => new[] { p[0], Box(p[1], layout) }).ToArray() });
        if (card is null) return;
        var s = profile.InfoScissor;
        var previous = device.RasterizerState;
        int top = (int)Box(s[1], layout), bottom = (int)Box(s[3], layout);
        device.ScissorRectangle = new Rectangle(s[0], top, s[2] - s[0] + 1, bottom - top + 1);
        device.RasterizerState = Scissored;
        DrawInfo(card, Box(profile.InfoText.FirstY, layout) - profile.InfoText.FirstY, CardInfoCompactLayout.Lines);
        device.RasterizerState = previous ?? RasterizerState.CullNone;
    }

    /// <summary>The card art under a compact panel's box (78 × 78 at x 29).</summary>
    public void DrawCompactArt(CardDefinition card, CardInfoCompactLayout layout) => visuals.DrawCardArt(card, 29, layout.ArtY, 78);

    /// <summary>A translucent black band (the text box's own draw stretched to a rectangle): the two bands between the attack panels.</summary>
    public void DrawBand(float x0, float y0, float x1, float y1)
    {
        var box = profile.Panel.First(d => d.AssetKey is null);
        float bx0 = box.Points.Min(p => p[0]), by0 = box.Points.Min(p => p[1]), bx1 = box.Points.Max(p => p[0]), by1 = box.Points.Max(p => p[1]);
        visuals.DrawFlow(box with { Points = box.Points.Select(p => new[] { x0 + (p[0] - bx0) / (bx1 - bx0) * (x1 - x0), y0 + (p[1] - by0) / (by1 - by0) * (y1 - y0) }).ToArray() });
    }

    private static float Box(float y, CardInfoCompactLayout layout) => (y < CardInfoCompactLayout.Split ? y + CardInfoCompactLayout.BoxTopDy : y) + layout.BoxDy;
    private static float[] Moved(float[] rect, float dx, float dy) => [rect[0] + dx, rect[1] + dy, rect[2] + dx, rect[3] + dy];

    /// <summary>"[Type/Suffix]" for monsters, "[Spell/Kind]" or "[Trap/Kind]" (no kind for Normal) from cardinfo_jpn.ehp words; null when a
    /// code has no word.</summary>
    public string? TypeLine(CardDefinition card)
    {
        var w = profile.TypeLine;
        if (card.Codes is not { } codes) return null;
        string Word(int offset) => visuals.Message(w.Source, offset);
        string body;
        if (codes.RawKind is 13 or 14)
        {
            body = Word(codes.RawKind == 13 ? w.Spell : w.Trap);
            if (codes.Icon is > 0 && codes.Icon < w.IconWords.Length) body += "/" + Word(w.IconWords[codes.Icon]);
        }
        else
        {
            if (codes.Type >= w.TypeWords.Length) return null;
            body = Word(w.TypeWords[codes.Type]);
            if (codes.RawKind != 0)
            {
                if (!w.KindSuffix.TryGetValue(codes.RawKind.ToString(CultureInfo.InvariantCulture), out int suffix)) return null;
                body += Word(suffix);
            }
        }
        return Word(w.Open) + body + Word(w.Close);
    }

    /// <summary>icon01 cells of the attribute and of the monster type or Spell/Trap kind (the album preview and the detail use the same).</summary>
    public (float[]? Attribute, float[]? Type) IconCells(CardDefinition card)
    {
        if (card.Codes is not { } codes) return (null, null);
        var tables = visuals.DeckEditProfile.Tables;
        float[]? Cell(string table, string key) =>
            tables.TryGetValue(table, out var t) && t.TryGetValue(key, out var e) ? e.EnumerateArray().Select(v => v.GetSingle()).ToArray() : null;
        var attribute = Cell("attributeIcon", codes.Attribute.ToString(CultureInfo.InvariantCulture));
        var type = codes.RawKind is 13 or 14
            ? Cell("spellTrapIcon", $"{codes.RawKind}:{codes.Icon}")
            : Cell("typeIcon", codes.Type.ToString(CultureInfo.InvariantCulture))
              ?? (profile.TypeIconExtra.TryGetValue(codes.Type.ToString(CultureInfo.InvariantCulture), out var extra) ? extra : null);
        return (attribute, type);
    }

    /// <summary>Attribute icon, then the monster type or Spell/Trap kind icon, from the Deck Edit icon tables (same icon01 cells).</summary>
    private void DrawIcons(CardDefinition card, float dy = 0)
    {
        var (attribute, type) = IconCells(card);
        if (attribute is not null) visuals.DrawFlow(CardAlbumRenderer.Quad(Moved(profile.AttributeIconRect, 0, dy), attribute));
        if (type is not null) visuals.DrawFlow(CardAlbumRenderer.Quad(Moved(profile.TypeIconRect, 0, dy), type));
    }

    /// <summary>Monsters: the star and "x" with the level above the strip; Xyz monsters show the rank star (measured on Pit Boss; "x" is the
    /// drawn glyph).</summary>
    /// <param name="tunerYellow">Measured in the duel (Krebons, Gusto Gulldo): a Tuner's level text is yellow (album not captured).</param>
    private void DrawLevel(CardDefinition card, float dx = 0, float dy = 0, bool tunerYellow = false)
    {
        if (card.Codes?.RawKind is 13 or 14 or null || card.Statistics.Level == 0) return;
        var star = card.Codes.RawKind is 22 or 23 ? profile.RankStar : profile.LevelStar;
        visuals.DrawFlow(CardAlbumRenderer.Quad(Moved(star.Rect, dx, dy), star.Uv));
        var t = profile.LevelText;
        string text = "x" + card.Statistics.Level.ToString(CultureInfo.InvariantCulture);
        float[] x = text.Length == t.X.Length ? t.X : [.. t.X, .. Enumerable.Range(1, text.Length - t.X.Length).Select(i => t.X[^1] + i * (t.X[^1] - t.X[0]))];
        for (int i = 0; i < text.Length; i++) visuals.DrawText(text[i].ToString(), x[i] + dx, t.Y + dy, t.ScaleX, t.ScaleY,
                tunerYellow && card.Codes?.GetterKind is 15 or 16 or 19 or 30 or 32 ? 0x00ffff : t.Color, t.ShadowAlpha);
    }

    /// <summary>Name lines (yellow), the bracketed type line (green), then card text, in the card-text font (measured: its advances).</summary>
    private void DrawInfo(CardDefinition card, float dy = 0, int? lineCount = null)
    {
        var t = profile.InfoText;
        // Measured in the duel: a type line too wide for the box breaks after a slash ("[Psychic/Tuner/" / "Effect]").
        var lines = Wrap(card.Name, t.Width, t.ScaleX).Select(l => (l, t.NameColor)).Concat(Wrap(TypeLine(card) ?? "", t.Width, t.ScaleX, "/").Select(l => (l, t.TypeColor)))
            .Concat(Wrap(card.Description, t.Width, t.ScaleX).Select(l => (l, t.TextColor))).Take(lineCount ?? t.Lines).ToList();
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Item1.Length > 0)
                visuals.DrawCardFontText(lines[i].Item1, t.X, t.FirstY + dy + i * t.LineHeight, t.ScaleX, t.ScaleY, lines[i].Item2, t.ShadowAlpha);
    }

    /// <summary>Greedy wrap with the card-text font's advances; measured on the album: a line may also break after a hyphen ("face-" / "up").</summary>
    private List<string> Wrap(string text, float width, float scaleX, string breakAfter = "-")
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                var parts = System.Text.RegularExpressions.Regex.Split(word, "(?<=" + System.Text.RegularExpressions.Regex.Escape(breakAfter) + ")(?=.)");
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
}

/// <summary>One compact panel of the attack target (measured offsets from the standard panel, aki-attack).</summary>
public sealed record CardInfoCompactLayout(float StripDy, float BoxDy, float LevelDx, float LevelDy, float ArtY, bool Glint)
{
    /// <summary>Items of the text box group above this y move with the box top.</summary>
    public const float Split = 200, BoxTopDy = -7;
    public const int Lines = 9;
    public static readonly CardInfoCompactLayout Target = new(-50, -164, 2, -20, 16, true), Attacker = new(-7, 0, 2, -8, 180, false);
}