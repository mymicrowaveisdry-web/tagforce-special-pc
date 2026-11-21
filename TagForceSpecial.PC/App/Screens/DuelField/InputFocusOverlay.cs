using Microsoft.Xna.Framework;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Marks the focused board object: the original cursor mesh on the player's own objects and the pointing-hand sprite on any.</summary>
public static class InputFocusOverlay
{
    /// <summary>Observed: the hand cursor uses the card's reverse-side rotation (x and y negated), 60 × scale, one unit behind the face.</summary>
    private static readonly Matrix HandFlip = Matrix.CreateScale(-60f, -60f, 60f);

    /// <summary>Pointer top-left from the truncated projected centre (GE traces): own hand card (−6, −46); opponent hand card (0, −14);
    /// other objects (0, −11), or (−16, −32) where the pointer would pass x 448 (own Deck and Banished). Piles use their top.</summary>
    public static Vector2 Pointer(DuelFieldProfile field, FieldObject focus, Vector3 centre)
    {
        var p = DuelLayoutProfile.Project(field, centre);
        int x = (int)p.X, y = (int)p.Y;
        if (focus.Kind == FieldObjectKind.Hand) return focus.Own ? new(x - 6, y - 46) : new(x, y - 14);
        return x > 448 ? new(x - 16, y - 32) : new(x, y - 11);
    }

    /// <summary>World centre of a focused object; Deck, Extra Deck and Graveyard use the top of their pile.</summary>
    public static (Vector3 Centre, Matrix Rotation) Place(FocusContext context, FieldObject focus)
    {
        bool own = focus.Own;
        Matrix world = focus.Kind switch
        {
            FieldObjectKind.Hand when own => context.Placed.FirstOrDefault(p => p.Hand && p.Card.InstanceId == focus.Card?.InstanceId)?.World
                ?? DuelLayoutProfile.HandWorld(focus.Index, context.View.Self.Hand.Count, true),
            FieldObjectKind.Hand => DuelLayoutProfile.OpponentHandWorld(focus.Index, context.View.Opponent.Hand.Count),
            FieldObjectKind.Monster => DuelLayoutProfile.SlotWorld(own, false, focus.Index, focus.Card?.Position),
            FieldObjectKind.SpellTrap => DuelLayoutProfile.SlotWorld(own, true, focus.Index, null),
            FieldObjectKind.Pendulum => DuelLayoutProfile.PendulumWorld(own, focus.Index),
            FieldObjectKind.FieldSpell => DuelLayoutProfile.FieldSpellWorld(own),
            _ => Matrix.CreateTranslation(focus.X, 0, (own ? 1 : -1) * (focus.Kind is FieldObjectKind.Deck or FieldObjectKind.Extra
                ? DuelLayoutProfile.DeckZ : DuelLayoutProfile.GraveyardZ))
        };
        var owner = own ? context.View.Self : context.View.Opponent;
        int pile = focus.Kind switch
        {
            FieldObjectKind.Deck => owner.DeckCount,
            FieldObjectKind.Extra => owner.ExtraDeckCount + owner.FaceUpExtraDeck.Count,
            FieldObjectKind.Graveyard => owner.Graveyard.Count,
            _ => 0
        };
        var centre = world.Translation + (pile > 0 ? new Vector3(0, DuelLayoutProfile.PileHeight(pile), 0) : Vector3.Zero);
        // An empty slot keeps the board's rotation; a card's slot turns the cursor with the card (defense position).
        var rotation = focus.Card is not null && focus.Kind is FieldObjectKind.Monster or FieldObjectKind.SpellTrap
            ? world with { M41 = 0, M42 = 0, M43 = 0 } : Matrix.Identity;
        return (centre, rotation);
    }

    public static void DrawOriginal(DuelDrawList list, DuelFieldProfile field, FocusContext context, FieldObject focus)
    {
        var (centre, rotation) = Place(context, focus);
        if (focus.Own && focus.Kind == FieldObjectKind.Hand)
        {
            var tilt = DuelLayoutProfile.HandTilt;
            var world = HandFlip * tilt * Matrix.CreateTranslation(centre - Vector3.TransformNormal(Vector3.Up, tilt));
            for (int i = 0; i < field.Piece("focus.hand").Length; i++) list.Field(DuelPart.Focus, "focus.hand", i, new(World: world));
        }
        else if (focus.Own)
            // Measured: the field cursor stands one unit above the board at the object's centre, also on piles.
            list.Field(DuelPart.Focus, "focus.field", 0, new(World: Matrix.CreateScale(60f) * rotation * Matrix.CreateTranslation(centre.X, 1, centre.Z)));
        var pointer = Pointer(field, focus, centre);
        var template = field.Piece("focus.pointer")[0].Vertices![0];
        list.Field(DuelPart.Focus, "focus.pointer", 0, new(Offset: new(pointer.X - template[0], pointer.Y - template[1])));
    }

    public static void Draw(DuelDrawList list, CardView? card, Rectangle rect, CardView? inspected, double seconds)
    {
        if (inspected?.InstanceId is { } id && card?.InstanceId == id)
            list.Border(DuelPart.Focus, rect, seconds % 1 < .7 ? Color.Cyan : DuelPalette.Gold);
    }
}

/// <summary>The view and the card placements a frame was drawn with.</summary>
public sealed record FocusContext(DuelView View, IReadOnlyList<DuelCardPlacement> Placed);
