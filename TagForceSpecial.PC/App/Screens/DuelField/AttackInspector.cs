using Microsoft.Xna.Framework;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>The original two-card left panel while an attack target is chosen (aki-attack draws 210–289): the target above, the attacker
/// below, each a compact standard panel over its dimmed 78 × 78 art. A hidden target contributes no identity: its panel shows the back tint
/// and no text (inferred; a face-down target was not captured).</summary>
public sealed record DuelAttackPanel(DuelPart Part, uint? TargetId, Color TargetTint, uint? AttackerId, Color AttackerTint) : DuelDraw(Part);

internal static class AttackInspector
{
    public static bool TryDraw(DuelDrawList list, DuelView view, TargetModal? attack, FieldObject? focus, Func<uint, DuelCardFacts?>? facts)
    {
        if (attack is null || focus is not { Own: false, Kind: FieldObjectKind.Monster, Index: var zone, Card: { } target }) return false;
        var choice = attack.Choices[attack.Index];
        if (DuelFieldCommands.AttackTarget(choice) != zone || choice.FocusInstanceId is not { } id) return false;
        var attacker = view.Self.Monsters.FirstOrDefault(card => card?.InstanceId == id);
        if (attacker is null) return false;
        var (targetId, targetTint) = Identity(target, facts);
        var (attackerId, attackerTint) = Identity(attacker, facts);
        list.Add(new DuelAttackPanel(DuelPart.Inspector, targetId, targetTint, attackerId, attackerTint));
        return true;
    }

    private static (uint? Id, Color Tint) Identity(CardView card, Func<uint, DuelCardFacts?>? facts)
    {
        if (!CardSpriteRenderer.ShowsFace(card, out uint id)) return (null, CardInspector.BackTint);
        var kind = facts?.Invoke(id);
        return (id, kind is null || kind.Monster ? CardInspector.MonsterTint : kind.Trap ? CardInspector.TrapTint : CardInspector.SpellTint);
    }
}
