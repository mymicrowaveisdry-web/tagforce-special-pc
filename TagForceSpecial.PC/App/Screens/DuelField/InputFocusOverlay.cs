using Microsoft.Xna.Framework;
using TagForceSpecial.Core.Views;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Marks the focused card: the original cursor mesh around it and the pointing-hand sprite above it.</summary>
public static class InputFocusOverlay
{
    /// <summary>Observed: the hand cursor uses the card's reverse-side rotation (x and y negated), 60 × scale, one unit behind the face.</summary>
    private static readonly Matrix HandFlip = Matrix.CreateScale(-60f, -60f, 60f);
    /// <summary>Single samples: the pointer's top-left from the projected card centre, for a hand card and a field monster.</summary>
    public static readonly Vector2 HandPointer = new(-6, -46), FieldPointer = new(0, -11);

    public static void DrawOriginal(DuelDrawList list, DuelFieldProfile field, IEnumerable<DuelCardPlacement> placed, int? focused)
    {
        if (focused is not { } id || placed.FirstOrDefault(p => p.Card.InstanceId == id) is not { } target) return;
        var centre = target.World.Translation;
        Matrix rotation = target.World with { M41 = 0, M42 = 0, M43 = 0 };
        var normal = Vector3.TransformNormal(Vector3.Up, rotation);
        if (target.Hand)
        {
            var world = HandFlip * rotation * Matrix.CreateTranslation(centre - normal);
            for (int i = 0; i < field.Piece("focus.hand").Length; i++) list.Field(DuelPart.Focus, "focus.hand", i, new(World: world));
        }
        else list.Field(DuelPart.Focus, "focus.field", 0, new(World: Matrix.CreateScale(60f) * rotation * Matrix.CreateTranslation(centre + normal)));
        var screen = DuelLayoutProfile.Project(field, centre) + (target.Hand ? HandPointer : FieldPointer);
        var pointer = field.Piece("focus.pointer")[0].Vertices![0];
        list.Field(DuelPart.Focus, "focus.pointer", 0, new(Offset: new((int)screen.X - pointer[0], (int)screen.Y - pointer[1])));
    }

    public static void Draw(DuelDrawList list, CardView? card, Rectangle rect, CardView? inspected, double seconds)
    {
        if (inspected?.InstanceId is { } id && card?.InstanceId == id)
            list.Border(DuelPart.Focus, rect, seconds % 1 < .7 ? Color.Cyan : DuelPalette.Gold);
    }
}
