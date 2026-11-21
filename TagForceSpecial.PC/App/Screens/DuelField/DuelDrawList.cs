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
public sealed record DuelWrapped(DuelPart Part, string Text, int X, int Y, int Columns, int Lines, Color Color) : DuelDraw(Part);
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

    public IEnumerable<DuelFieldDraw> FieldDraws => draws.OfType<DuelFieldDraw>();

    /// <summary>Replays the sprite commands; measured field draws are rendered separately by <see cref="DuelFieldVisuals"/>.</summary>
    public void Replay(SpriteRenderer renderer)
    {
        foreach (var draw in draws)
            switch (draw)
            {
                case DuelFill f: renderer.Fill(f.Rect, f.Color, f.Layer); break;
                case DuelBorder b: renderer.Border(b.Rect, b.Color, b.Layer); break;
                case DuelText t: renderer.Text(t.Text, t.X, t.Y, t.Color, t.Scale); break;
                case DuelWrapped w: renderer.Wrapped(w.Text, w.X, w.Y, w.Columns, w.Lines, w.Color); break;
            }
    }
}
