using Microsoft.Xna.Framework;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Draws the backdrop, the board surface, the Deck, Extra Deck and Graveyard piles, and the zone labels.</summary>
public static class DuelBoardRenderer
{
    /// <summary>Original: the backdrop scene, a depth clear, then the eight translucent zone meshes (Pendulum, Graveyard, monster and
    /// Spell/Trap slots, Field Spell, Extra Deck, banished, side pads) with depth testing.</summary>
    public static void DrawOriginal(DuelDrawList list, DuelFieldProfile field, string backdrop)
    {
        list.FieldPiece(DuelPart.Board, field, "backdrop." + backdrop);
        list.FieldPiece(DuelPart.Board, field, "board");
    }

    /// <summary>Original piles in GE order per player (Extra Deck, Deck, Graveyard): top, side strip, then the count. A Graveyard shows its
    /// top card's face; empty piles are not drawn.</summary>
    public static void DrawPilesOriginal(DuelDrawList list, DuelFieldProfile field, DuelView view)
    {
        foreach (var (player, owner) in new[] { (true, view.Self), (false, view.Opponent) })
        {
            Pile(list, field, player, DuelPileKind.Extra, owner.ExtraDeckCount + owner.FaceUpExtraDeck.Count, null);
            Pile(list, field, player, DuelPileKind.Deck, owner.DeckCount, null);
            Pile(list, field, player, DuelPileKind.Graveyard, owner.Graveyard.Count, owner.Graveyard.Count > 0 ? owner.Graveyard[^1] : null);
        }
    }

    private static void Pile(DuelDrawList list, DuelFieldProfile field, bool player, DuelPileKind pile, int count, CardView? top)
    {
        if (count <= 0) return;
        var world = DuelLayoutProfile.PileBone(count) * DuelLayoutProfile.PileWorld(player, pile);
        if (top is not null && CardSpriteRenderer.ShowsFace(top, out uint id))
            list.Field(DuelPart.Board, "pile.top.face", 0, new(World: world, Texture: CardSpriteRenderer.FaceKey(id)));
        else list.Field(DuelPart.Board, "pile.top", 0, new(World: world));
        list.Field(DuelPart.Board, "pile.side", 0, new(World: world));
        FieldCounters.Draw(list, DuelPart.Board, field, count, DuelLayoutProfile.PileTop(player, pile, count), FieldCounters.White);
    }

    public static void DrawBackdrop(DuelDrawList list) =>
        list.Fill(DuelPart.Board, DuelSceneLayout.Screen, new(9, 15, 27), SceneLayer.Background);

    public static void DrawSurface(DuelDrawList list)
    {
        list.Fill(DuelPart.Board, DuelSceneLayout.Board, new(18, 36, 49));
        list.Border(DuelPart.Board, DuelSceneLayout.Board, new(48, 78, 94), SceneLayer.Field);
        list.Fill(DuelPart.Board, DuelSceneLayout.Divider, new(48, 78, 94));
    }

    public static void DrawPiles(DuelDrawList list, DuelView view)
    {
        foreach (var pile in DuelSceneLayout.Piles(view))
        {
            var at = pile.At;
            list.Fill(DuelPart.Board, new(at.X, at.Y, DuelSceneLayout.PileSize.X, DuelSceneLayout.PileSize.Y), new(32, 44, 58));
            list.Text(DuelPart.Board, pile.Label, at.X + 3, at.Y + 3, DuelPalette.Muted);
            list.Text(DuelPart.Board, pile.Count.ToString(), at.X + 3, at.Y + 15);
        }
    }

    public static void DrawZoneLabels(DuelDrawList list)
    {
        list.Text(DuelPart.Board, "P", 23, 229, DuelPalette.Muted);
        list.Text(DuelPart.Board, "P", 309, 229, DuelPalette.Muted);
    }
}
