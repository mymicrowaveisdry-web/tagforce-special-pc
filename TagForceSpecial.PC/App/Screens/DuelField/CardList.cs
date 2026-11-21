using System.Globalization;
using Microsoft.Xna.Framework;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

public enum CardListPile { ExtraDeck, Graveyard, Banished }

/// <summary>Show Card List on a pile: opens the card list for that pile's owner.</summary>
public sealed record CardListCommand(bool Own, CardListPile Pile) : MenuCommand;

/// <summary>The open card list: the highlighted column (0–4), the first visible card and whether Close holds the cursor.</summary>
public sealed record CardListModal(bool Own, CardListPile Pile, int Column = 0, int Scroll = 0, bool OnClose = false) : DuelModal
{
    public int Selected => Scroll + Column;
}

/// <summary>The original card list (Order 3.2 captures `order32-gallery`, own Extra Deck): five columns, the cursor moving across them and
/// the list scrolling one card at the edge, wrapping from the last card to the first and back; Down moves to Close and Up returns; ○ on
/// Close and × close it. Measured for the own Extra Deck; other piles use the same window with their own titles (inferred).</summary>
public static class CardList
{
    public const int Columns = 5;
    public const float ColumnPitch = 49;

    /// <summary>The pile's public cards; the opponent's face-down Extra Deck is never listed.</summary>
    public static IReadOnlyList<CardView> Cards(DuelView view, bool own, CardListPile pile)
    {
        var owner = own ? view.Self : view.Opponent;
        return pile switch
        {
            CardListPile.ExtraDeck => own ? [.. owner.ExtraDeck ?? [], .. owner.FaceUpExtraDeck] : owner.FaceUpExtraDeck,
            CardListPile.Graveyard => owner.Graveyard,
            _ => owner.Banished ?? []
        };
    }

    /// <summary>Window titles from cardinfo_jpn.ehp ("Your Extra Deck" … "Opponent's Removed Cards").</summary>
    public static int TitleOffset(bool own, CardListPile pile) => (pile, own) switch
    {
        (CardListPile.ExtraDeck, true) => 1214114, (CardListPile.ExtraDeck, false) => 1214146,
        (CardListPile.Graveyard, true) => 1214242, (CardListPile.Graveyard, false) => 1214272,
        (_, true) => 1214314, _ => 1214352
    };

    public const int SortOrderOffset = 406036, CloseOffset = 400970;

    /// <summary>One press or repeat step. Measured: Right moves the cursor to column 5, then scrolls; Right on the last card returns to the
    /// first, Left on the first goes to the last; Left/Right on Close do nothing. Inferred: Left scrolls back at column 1; repeats stop
    /// at the ends instead of wrapping (as menus do).</summary>
    public static CardListModal Move(CardListModal list, int dx, int dy, int count, bool repeated)
    {
        if (dy < 0) return list with { OnClose = true };
        if (dy > 0) return list with { OnClose = false };
        if (dx == 0 || list.OnClose || count == 0) return list;
        int visible = Math.Min(Columns, count);
        for (int step = 0; step < Math.Abs(dx); step++)
        {
            int selected = list.Selected;
            if (dx > 0)
                list = selected == count - 1 ? repeated ? list : list with { Column = 0, Scroll = 0 }
                    : list.Column < visible - 1 ? list with { Column = list.Column + 1 } : list with { Scroll = list.Scroll + 1 };
            else
                list = selected == 0 ? repeated ? list : list with { Column = visible - 1, Scroll = count - visible }
                    : list.Column > 0 ? list with { Column = list.Column - 1 } : list with { Scroll = list.Scroll - 1 };
        }
        return list;
    }

    /// <summary>The highlighted card, for the left panel.</summary>
    public static CardView? Highlighted(DuelView view, CardListModal list)
    {
        var cards = Cards(view, list.Own, list.Pile);
        return list.Selected < cards.Count ? cards[list.Selected] : null;
    }

    private static readonly IReadOnlyDictionary<int, (int, int)> White = new Dictionary<int, (int, int)> { [0] = (0xffffff, 128) };
    private static readonly IReadOnlyDictionary<int, (int, int)> Black = new Dictionary<int, (int, int)> { [0] = (0x000000, 64) };
    /// <summary>Measured thumb: 71 px for 5 of 15 cards, x = 210 + round(143·scroll/(count − 5)); the length rule for other counts is
    /// inferred (span 214 × visible/count).</summary>
    public const float ThumbStart = 210, ThumbSpan = 214;

    public static void Draw(DuelDrawList list, DuelFieldProfile field, DuelView view, CardListModal modal, Func<uint, DuelCardFacts?>? facts)
    {
        var cards = Cards(view, modal.Own, modal.Pile);
        const DuelPart part = DuelPart.Prompt;
        foreach (var piece in new[] { "gallery.clear", "gallery.window" }) list.FieldPiece(part, field, piece);
        list.Add(new DuelRichText(part, CardAlbumResources.CardInfoKey, TitleOffset(modal.Own, modal.Pile), null, 188.8f, 19.42f, 0.6f, 0.54f, White));
        list.Add(new DuelRichText(part, DuelFieldResources.BasicKey, SortOrderOffset, null, 192.8f, 39.42f, 0.6f, 0.54f, Black));
        list.FieldPiece(part, field, "gallery.sort");
        DrawScroll(list, field, modal, cards.Count);
        list.FieldPiece(part, field, "gallery.close");
        if (modal.OnClose) list.FieldPiece(part, field, "gallery.close.focus");
        list.Add(new DuelRichText(part, DuelFieldResources.BasicKey, CloseOffset, null, 293.8f, 212.42f, 0.6f, 0.54f, White));
        // Measured: the selected column keeps a dim highlight while Close has the cursor (captured over column 5).
        if (cards.Count > 0)
            list.FieldPiece(part, field, modal.OnClose ? "gallery.highlight.idle" : "gallery.highlight",
                new(Offset: new((modal.Column - (modal.OnClose ? Columns - 1 : 0)) * ColumnPitch, 0)));
        for (int column = 0; column < Math.Min(Columns, cards.Count - modal.Scroll); column++)
            DrawColumn(list, field, cards[modal.Scroll + column], column * ColumnPitch, facts);
        if (cards.Count > Columns) list.FieldPiece(part, field, "gallery.marker");
    }

    private static void DrawScroll(DuelDrawList list, DuelFieldProfile field, CardListModal modal, int count)
    {
        const DuelPart part = DuelPart.Prompt;
        list.FieldPiece(part, field, "gallery.arrow.left");
        list.FieldPiece(part, field, "gallery.track");
        float length = count <= Columns ? ThumbSpan : MathF.Floor(ThumbSpan * Columns / count);
        float x = count <= Columns ? ThumbStart : ThumbStart + MathF.Round((ThumbSpan - length) * modal.Scroll / (count - Columns));
        // The thumb template is 71 px (5 of 15): left cap, right cap and middle; the caps move, the middle stretches (inferred for other lengths).
        var caps = field.Piece("gallery.thumb");
        float templateLength = caps[1].Vertices![1][0] - caps[0].Vertices![0][0];
        list.Field(part, "gallery.thumb", 0, new(Offset: new(x - ThumbStart, 0)));
        list.Field(part, "gallery.thumb", 1, new(Offset: new(x - ThumbStart + length - templateLength, 0)));
        list.Add(new DuelFieldSprites(part, "gallery.thumb", 2, Stretch(caps[2].Vertices!, x - ThumbStart, length - templateLength), caps[2].Uv!));
        list.FieldPiece(part, field, "gallery.arrow.right");
        // Measured single frames: a faint glow on the arrow toward the remaining cards (pulse rate not measured).
        if (modal.Scroll + Columns < count) list.FieldPiece(part, field, "gallery.glow.right");
        else if (modal.Scroll > 0) list.FieldPiece(part, field, "gallery.glow.left");
    }

    private static float[][] Stretch(float[][] vertices, float dx, float grow) =>
        [[vertices[0][0] + dx, vertices[0][1], vertices[0][2]], [vertices[1][0] + dx + grow, vertices[1][1], vertices[1][2]]];

    private static void DrawColumn(DuelDrawList list, DuelFieldProfile field, CardView card, float dx, Func<uint, DuelCardFacts?>? facts)
    {
        const DuelPart part = DuelPart.Prompt;
        var offset = new PspPlacement(Offset: new(dx, 0));
        list.FieldPiece(part, field, "gallery.column", offset);
        list.FieldPiece(part, field, "gallery.header", offset);
        if (!CardSpriteRenderer.ShowsFace(card, out uint id)) return;
        list.Field(part, "gallery.face", 0, offset with { Texture = CardSpriteRenderer.FaceKey(id) });
        // Measured: the first six characters of the name, trailing spaces dropped ("Black Rose Dragon" → "Black").
        string name = card.Name.Length > 6 ? card.Name[..6].TrimEnd() : card.Name;
        list.Add(new DuelFieldText(part, name, 192.8f + dx, 133.42f, 0.6f, 0.54f, 0x000000, 64));
        if (card.Attack is not { } atk || card.Defense is not { } def) return; // Spells and Traps: not captured, no statistics drawn
        list.FieldPiece(part, field, "gallery.atk", offset);
        list.FieldPiece(part, field, "gallery.def", offset);
        Digits(list, atk, 225.5f + dx, 146.42f);
        Digits(list, def, 225.5f + dx, 159.42f);
        var fact = facts?.Invoke(id);
        int value = fact?.Rank > 0 ? fact.Rank : fact?.Level ?? 0;
        if (value <= 0) return;
        // Level star cell (56, 120) or Rank star (112, 120) of card_icon01; "x" + value at 0.45.
        var uv = fact!.Rank > 0 ? new Vector2(56f / 128f, 0) : Vector2.Zero;
        list.Field(part, "gallery.level", 0, offset with { UvOffset = uv });
        list.Add(new DuelFieldText(part, "x" + value.ToString(CultureInfo.InvariantCulture), 204.85f + dx, 172.85f, 0.45f, 0.45f, 0x000000, 64));
    }

    /// <summary>Measured: statistic digits right-aligned on a 7-pixel pitch (last digit at x 225.5), 0.5 × 0.54, black with a 64 shadow.</summary>
    private static void Digits(DuelDrawList list, int value, float right, float y)
    {
        string text = value.ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < text.Length; i++)
            list.Add(new DuelFieldText(DuelPart.Prompt, text[i].ToString(), right - (text.Length - 1 - i) * 7, y, 0.5f, 0.54f, 0x000000, 64));
    }
}
