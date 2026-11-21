using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Chooses the inspected public card and draws its details. Hidden cards are never inspection targets.</summary>
public static class CardInspector
{
    /// <summary>The card cycled to with Inspect (<paramref name="inspectedCard"/> of zero or more), otherwise the selected choice's card.</summary>
    public static CardView? Target(DuelView view, int choice, int inspectedCard)
    {
        var selected = view.Choices.Count > 0 ? view.Choices[Math.Clamp(choice, 0, view.Choices.Count - 1)] : null;
        int? focus = selected?.FocusInstanceId;
        var known = PublicCards(view);
        return inspectedCard >= 0 && known.Length > 0 ? known[inspectedCard % known.Length]
            : known.FirstOrDefault(c => c.InstanceId == focus);
    }

    /// <summary>Public cards in inspection order: own hand, own field, own Graveyard and face-up Extra Deck, then the opponent's, then Field Spells.</summary>
    public static CardView[] PublicCards(DuelView view) => view.Self.Hand.Concat(view.Self.Monsters.OfType<CardView>())
        .Concat(view.Self.SpellTraps.OfType<CardView>()).Concat(view.Self.Pendulum.OfType<CardView>())
        .Concat(view.Self.Graveyard).Concat(view.Self.FaceUpExtraDeck)
        .Concat(view.Opponent.Monsters.OfType<CardView>()).Concat(view.Opponent.SpellTraps.OfType<CardView>())
        .Concat(view.Opponent.Pendulum.OfType<CardView>()).Concat(view.Opponent.Graveyard)
        .Concat(view.Opponent.FaceUpExtraDeck).Concat(new[] { view.Self.FieldSpell, view.Opponent.FieldSpell }.OfType<CardView>())
        .Where(c => !c.Hidden).ToArray();

    public static void Draw(DuelDrawList list, CardView? detail)
    {
        list.Fill(DuelPart.Inspector, DuelSceneLayout.InspectorPanel, new(22, 29, 44));
        list.Text(DuelPart.Inspector, "CARD DETAILS", 350, 37, DuelPalette.Gold);
        if (detail is null)
        {
            list.Wrapped(DuelPart.Inspector, "Select an action or press TAB to inspect visible cards.", 350, 53, 20, 5, DuelPalette.Muted);
            return;
        }
        list.Wrapped(DuelPart.Inspector, detail.Name, 350, 51, 20, 2);
        list.Text(DuelPart.Inspector, $"ATK {detail.Attack?.ToString() ?? "?"}", 350, 73, DuelPalette.Muted);
        list.Text(DuelPart.Inspector, $"DEF {detail.Defense?.ToString() ?? "?"}", 350, 83, DuelPalette.Muted);
        list.Wrapped(DuelPart.Inspector, detail.Description, 350, 96, 20, 5, DuelPalette.Muted);
    }

    /// <summary>Interim text in the original inspector column (x 0–144), until the measured card panel replaces it.</summary>
    public static void DrawInterim(DuelDrawList list, CardView? detail)
    {
        list.Fill(DuelPart.Inspector, new(0, 0, 144, 272), new(22, 29, 44), Rendering.SceneLayer.Field);
        if (detail is null) return;
        list.Wrapped(DuelPart.Inspector, detail.Name.ToUpperInvariant(), 6, 8, 22, 3, DuelPalette.Gold);
        if (detail.Attack is not null || detail.Defense is not null)
            list.Text(DuelPart.Inspector, $"ATK {detail.Attack?.ToString() ?? "?"} DEF {detail.Defense?.ToString() ?? "?"}", 6, 40, DuelPalette.Muted);
        list.Wrapped(DuelPart.Inspector, detail.Description, 6, 56, 22, 22, Microsoft.Xna.Framework.Color.White);
    }
}
