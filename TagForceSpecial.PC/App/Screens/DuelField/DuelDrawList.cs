using Microsoft.Xna.Framework;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>The scene responsibility that emitted a draw command.</summary>
public enum DuelPart { Board, Cards, Hud, PhaseBar, Inspector, Prompt, Focus, Effects }

/// <summary>One recorded duel draw command, tagged with its owning scene part.</summary>
public abstract record DuelDraw(DuelPart Part);
public sealed record DuelFill(DuelPart Part, Rectangle Rect, Color Color, SceneLayer Layer) : DuelDraw(Part);
public sealed record DuelBorder(DuelPart Part, Rectangle Rect, Color Color, SceneLayer Layer) : DuelDraw(Part);
public sealed record DuelText(DuelPart Part, string Text, int X, int Y, Color Color, int Scale = 1) : DuelDraw(Part);
public sealed record DuelWrapped(DuelPart Part, string Text, int X, int Y, int Columns, int Lines, Color Color) : DuelDraw(Part)
{
    public bool Ellipsis { get; init; }
}
/// <summary>Original-font text in the field layer: glyph cells start at <paramref name="X"/> (left) or end at it (right-aligned), with the
/// measured two-copy shadow. <paramref name="Color"/> is 0xBBGGRR.</summary>
public sealed record DuelFieldText(DuelPart Part, string Text, float X, float Y, float ScaleX, float ScaleY, int Color, int ShadowAlpha,
    bool AlignRight = false, float? LayoutScaleX = null) : DuelDraw(Part);
/// <summary>The full-screen Show Details page (the Card Album's detail).</summary>
public sealed record DuelCardDetail(DuelPart Part, App.Screens.Album.CardAlbumDetail Detail) : DuelDraw(Part);
/// <summary>The left card panel for <paramref name="CardId"/> (a public card) or a back when null, with its strip tint.</summary>
public sealed record DuelCardPanel(DuelPart Part, uint? CardId, Microsoft.Xna.Framework.Color Tint) : DuelDraw(Part);
public sealed record DuelCardThumbnail(DuelPart Part, uint CardId, Rectangle Rect) : DuelDraw(Part);
/// <summary>One measured original draw (<paramref name="Index"/> of a profile piece), repeated with a placement.</summary>
public sealed record DuelFieldDraw(DuelPart Part, string Piece, int Index, PspPlacement Placement = default) : DuelDraw(Part);

/// <summary>Ordered duel draw commands for one frame. Replay keeps submission order because the sprite batch's depth sort is not stable.</summary>
public sealed class DuelDrawList
{
    private readonly List<DuelDraw> draws = [];
    public IReadOnlyList<DuelDraw> Draws => draws;

    public void Fill(DuelPart part, Rectangle rect, Color color, SceneLayer layer = SceneLayer.Field) => draws.Add(new DuelFill(part, rect, color, layer));
    public void Border(DuelPart part, Rectangle rect, Color color, SceneLayer layer = SceneLayer.Cursor) => draws.Add(new DuelBorder(part, rect, color, layer));
    public void Text(DuelPart part, string text, int x, int y, Color? color = null, int scale = 1) =>
        draws.Add(new DuelText(part, text, x, y, color ?? Color.White, scale));
    public void Wrapped(DuelPart part, string text, int x, int y, int columns, int lines, Color? color = null) =>
        draws.Add(new DuelWrapped(part, text, x, y, columns, lines, color ?? Color.White));

    public void Field(DuelPart part, string piece, int index, PspPlacement placement = default) => draws.Add(new DuelFieldDraw(part, piece, index, placement));

    /// <summary>Every draw of a measured piece, in GE order, with one placement.</summary>
    public void FieldPiece(DuelPart part, DuelFieldProfile profile, string piece, PspPlacement placement = default)
    {
        for (int i = 0; i < profile.Piece(piece).Length; i++) Field(part, piece, i, placement);
    }

    /// <summary>A right-aligned line is laid out with its width at <paramref name="layoutScaleX"/> (default: the drawing scale).</summary>
    public void FieldText(DuelPart part, string text, float x, float y, float scaleX, float scaleY, int color, int shadowAlpha, bool alignRight = false,
        float? layoutScaleX = null)
    {
        if (text.Length > 0) draws.Add(new DuelFieldText(part, text, x, y, scaleX, scaleY, color, shadowAlpha, alignRight, layoutScaleX));
    }

    /// <summary>Commands rendered into the field layer, in order: measured draws and original-font text.</summary>
    public IEnumerable<DuelDraw> FieldDraws => draws.Where(d => d is DuelFieldDraw or DuelFieldText or DuelFieldMenu or DuelFieldSprites or DuelRichText or DuelChainPrompt or DuelCardPanel or DuelCardDetail);

    public void Add(DuelDraw draw) => draws.Add(draw);

    /// <summary>Replays the sprite commands; measured field draws are rendered separately by <see cref="DuelFieldVisuals"/>.</summary>
    public void Replay(SpriteRenderer renderer) => Replay(renderer, null);

    internal void Replay(SpriteRenderer renderer, DuelFieldVisuals? field)
    {
        foreach (var draw in draws)
            switch (draw)
            {
                case DuelFill f: renderer.Fill(f.Rect, f.Color, f.Layer); break;
                case DuelBorder b: renderer.Border(b.Rect, b.Color, b.Layer); break;
                case DuelText t: renderer.Text(t.Text, t.X, t.Y, t.Color, t.Scale); break;
                case DuelWrapped w: renderer.Wrapped(w.Text, w.X, w.Y, w.Columns, w.Lines, w.Color, w.Ellipsis); break;
                case DuelCardThumbnail preview when field is not null:
                    var face = field.CardFace(preview.CardId);
                    renderer.Sprite(new(face, face.Bounds), preview.Rect, SceneLayer.PanelHighlight);
                    break;
            }
    }
}
