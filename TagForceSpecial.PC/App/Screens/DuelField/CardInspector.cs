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

    /// <summary>Measured strip tints of the left panel by card kind (one captured sample each; the strip brightness also pulses): monster,
    /// Spell, Trap, and a back.</summary>
    public static readonly Microsoft.Xna.Framework.Color MonsterTint = new(1f, 0.859f, 0.624f), SpellTint = new(0.71f, 1f, 0.992f),
        TrapTint = new(1f, 0.749f, 0.969f), BackTint = new(0.918f, 0.918f, 0.918f);

    /// <summary>The original left panel: a public card's face, stats and text, or a back (hidden, face-down to the viewer, piles and empty
    /// objects; measured for a hidden hand card and the Deck).</summary>
    public static void DrawOriginal(DuelDrawList list, CardView? detail, Func<uint, DuelCardFacts?>? facts)
    {
        if (detail is { Hidden: false, CardId: { } id })
        {
            var kind = facts?.Invoke(id);
            var tint = kind is null ? MonsterTint : kind.Monster ? MonsterTint : kind.Trap ? TrapTint : SpellTint;
            list.Add(new DuelCardPanel(DuelPart.Inspector, id, tint));
        }
        else list.Add(new DuelCardPanel(DuelPart.Inspector, null, BackTint));
    }
}
