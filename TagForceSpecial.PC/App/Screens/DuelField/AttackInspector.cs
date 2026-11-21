using Microsoft.Xna.Framework;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Screens.DuelField;

internal static class AttackInspector
{
    private const DuelPart Part = DuelPart.Inspector;
    private static readonly Color Surface = new(22, 29, 44);
    private static readonly Color Divider = new(55, 68, 87);
    private static readonly Color AttackerAccent = new(119, 197, 238);

    public static bool TryDraw(DuelDrawList list, DuelView view, TargetModal? attack, FieldObject? focus, Func<uint, DuelCardFacts?>? facts)
    {
        if (attack is null || focus is not { Own: false, Kind: FieldObjectKind.Monster, Index: var zone, Card: { } target }) return false;
        var choice = attack.Choices[attack.Index];
        if (DuelFieldCommands.AttackTarget(choice) != zone || choice.FocusInstanceId is not { } id) return false;
        var attacker = view.Self.Monsters.FirstOrDefault(card => card?.InstanceId == id);
        if (attacker is null) return false;

        DrawCard(list, target, "Target", 5, DuelPalette.Gold, facts);
        DrawCard(list, attacker, "Attacker", 138, AttackerAccent, facts);
        return true;
    }

    private static void DrawCard(DuelDrawList list, CardView card, string role, int top, Color accent, Func<uint, DuelCardFacts?>? facts)
    {
        list.Fill(Part, new Rectangle(4, top, 136, 129), Surface, SceneLayer.Panel);
        list.Fill(Part, new Rectangle(4, top, 3, 129), accent, SceneLayer.PanelHighlight);
        list.Text(Part, role, 57, top + 9, accent);

        var thumbnail = new Rectangle(12, top + 9, 38, 44);
        if (!CardSpriteRenderer.ShowsFace(card, out uint id))
        {
            list.Fill(Part, thumbnail, Divider, SceneLayer.PanelHighlight);
            list.Text(Part, "?", 27, top + 27);
            list.Text(Part, "Face-down", 57, top + 24);
            return;
        }

        list.Add(new DuelCardThumbnail(Part, id, thumbnail));
        list.Add(new DuelWrapped(Part, card.Name, 57, top + 21, 13, 2, Color.White) { Ellipsis = true });
        if (facts?.Invoke(id) is { Level: > 0 } cardFacts)
            list.Text(Part, $"LV {cardFacts.Level}", 57, top + 42, DuelPalette.Muted);

        list.Text(Part, $"ATK {card.Attack?.ToString() ?? "?"}", 12, top + 59);
        list.Text(Part, $"DEF {card.Defense?.ToString() ?? "?"}", 76, top + 59);
        list.Fill(Part, new Rectangle(12, top + 72, 120, 1), Divider, SceneLayer.PanelHighlight);
        list.Add(new DuelWrapped(Part, card.Description, 12, top + 79, 20, 5, DuelPalette.Muted) { Ellipsis = true });
    }
}
