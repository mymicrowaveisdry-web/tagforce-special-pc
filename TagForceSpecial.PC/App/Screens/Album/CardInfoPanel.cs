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
            if (stripTint is { } tint && draw.AssetKey == strip) piece = piece with { Color = [tint.R, tint.G, tint.B, draw.Color[3]] };
            visuals.DrawFlow(piece);
        }
        if (card is null) return;
        visuals.DrawCardStats(card); DrawIcons(card); DrawLevel(card);
        // Measured: the info text is clipped to the text box (GE scissor on its draws).
        var s = profile.InfoScissor;
        var previous = device.RasterizerState;
        device.ScissorRectangle = new Rectangle(s[0], s[1], s[2] - s[0] + 1, s[3] - s[1] + 1);
        device.RasterizerState = Scissored;
        DrawInfo(card);
        device.RasterizerState = previous ?? RasterizerState.CullNone;
    }

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
    private void DrawIcons(CardDefinition card)
    {
        var (attribute, type) = IconCells(card);
        if (attribute is not null) visuals.DrawFlow(CardAlbumRenderer.Quad(profile.AttributeIconRect, attribute));
        if (type is not null) visuals.DrawFlow(CardAlbumRenderer.Quad(profile.TypeIconRect, type));
    }

    /// <summary>Monsters: the star and "x" with the level above the strip; Xyz monsters show the rank star (measured on Pit Boss; "x" is the
    /// drawn glyph).</summary>
    private void DrawLevel(CardDefinition card)
    {
        if (card.Codes?.RawKind is 13 or 14 or null || card.Statistics.Level == 0) return;
        var star = card.Codes.RawKind is 22 or 23 ? profile.RankStar : profile.LevelStar;
        visuals.DrawFlow(CardAlbumRenderer.Quad(star.Rect, star.Uv));
        var t = profile.LevelText;
        string text = "x" + card.Statistics.Level.ToString(CultureInfo.InvariantCulture);
        float[] x = text.Length == t.X.Length ? t.X : [.. t.X, .. Enumerable.Range(1, text.Length - t.X.Length).Select(i => t.X[^1] + i * (t.X[^1] - t.X[0]))];
        for (int i = 0; i < text.Length; i++) visuals.DrawText(text[i].ToString(), x[i], t.Y, t.ScaleX, t.ScaleY, t.Color, t.ShadowAlpha);
    }

    /// <summary>Name lines (yellow), the bracketed type line (green), then card text, in the card-text font (measured: its advances).</summary>
    private void DrawInfo(CardDefinition card)
    {
        var t = profile.InfoText;
        // Measured in the duel: a type line too wide for the box breaks after a slash ("[Psychic/Tuner/" / "Effect]").
        var lines = Wrap(card.Name, t.Width, t.ScaleX).Select(l => (l, t.NameColor)).Concat(Wrap(TypeLine(card) ?? "", t.Width, t.ScaleX, "/").Select(l => (l, t.TypeColor)))
            .Concat(Wrap(card.Description, t.Width, t.ScaleX).Select(l => (l, t.TextColor))).Take(t.Lines).ToList();
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Item1.Length > 0)
                visuals.DrawCardFontText(lines[i].Item1, t.X, t.FirstY + i * t.LineHeight, t.ScaleX, t.ScaleY, lines[i].Item2, t.ShadowAlpha);
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
