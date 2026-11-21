using Microsoft.Xna.Framework;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

public readonly record struct DuelSlot(CardView? Card, Rectangle Rect);
public readonly record struct DuelPile(Point At, string Label, int Count);

/// <summary>Shared colours of the PC test visualization.</summary>
public static class DuelPalette
{
    public static readonly Color Gold = new(238, 196, 104);
    public static readonly Color Muted = new(143, 162, 187);
}

/// <summary>Geometry of the PC test visualization, in one place for the scene parts. It is not original layout.</summary>
public static class DuelSceneLayout
{
    public static readonly Rectangle Screen = new(0, 0, 480, 272);
    public static readonly Rectangle HudBar = new(0, 0, 480, 28);
    public static readonly Rectangle Board = new(6, 31, 332, 219);
    public static readonly Rectangle Divider = new(12, 127, 320, 1);
    public static readonly Rectangle InspectorPanel = new(344, 31, 130, 219);
    public static readonly Point PileSize = new(30, 28);

    /// <summary>Hands and the monster and Spell/Trap rows, top of the screen to bottom.</summary>
    public static IEnumerable<DuelSlot> RowSlots(DuelView view) =>
        Hand(view.Opponent, 38).Concat(Row(view.Opponent.SpellTraps, 63)).Concat(Row(view.Opponent.Monsters, 95))
            .Concat(Row(view.Self.Monsters, 134)).Concat(Row(view.Self.SpellTraps, 166)).Concat(Hand(view.Self, 207));

    /// <summary>Pendulum and Field Spell slots beside the rows.</summary>
    public static IEnumerable<DuelSlot> SideSlots(DuelView view)
    {
        yield return new(view.Opponent.Pendulum[0], new(12, 33, 30, 25));
        yield return new(view.Opponent.Pendulum[1], new(298, 33, 30, 25));
        yield return new(view.Self.Pendulum[0], new(12, 201, 30, 25));
        yield return new(view.Self.Pendulum[1], new(298, 201, 30, 25));
        yield return new(view.Opponent.FieldSpell, new(298, 63, 30, 25));
        yield return new(view.Self.FieldSpell, new(298, 166, 30, 25));
    }

    public static IEnumerable<DuelPile> Piles(DuelView view)
    {
        yield return new(new(12, 96), "EX", view.Opponent.ExtraDeckCount + view.Opponent.FaceUpExtraDeck.Count);
        yield return new(new(12, 134), "DK", view.Self.DeckCount);
        yield return new(new(298, 96), "GY", view.Opponent.Graveyard.Count);
        yield return new(new(298, 134), "GY", view.Self.Graveyard.Count);
        yield return new(new(12, 63), "DK", view.Opponent.DeckCount);
        yield return new(new(12, 166), "EX", view.Self.ExtraDeckCount + view.Self.FaceUpExtraDeck.Count);
    }

    private static IEnumerable<DuelSlot> Row(IReadOnlyList<CardView?> cards, int y)
    {
        for (int i = 0; i < 5; i++) yield return new(cards[i], new(47 + i * 49, y, 44, 28));
    }

    private static IEnumerable<DuelSlot> Hand(PlayerView player, int y)
    {
        int step = Math.Min(36, 242 / Math.Max(1, player.Hand.Count));
        for (int i = 0; i < player.Hand.Count; i++) yield return new(player.Hand[i], new(47 + i * step, y, step - 2, 23));
    }
}
