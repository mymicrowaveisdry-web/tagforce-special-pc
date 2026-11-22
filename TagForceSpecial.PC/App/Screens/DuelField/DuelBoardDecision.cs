using TagForceSpecial.Core;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

internal static class DuelBoardDecision
{
    public static bool OnField(ListModal modal) => modal.Choices.Count > 0 && modal.Choices.All(choice =>
        choice.FieldTarget?.Location is FieldLocation.MonsterZone or FieldLocation.SpellTrapZone or FieldLocation.PendulumZone);

    public static FieldObject? Target(DuelView view, ListModal modal)
    {
        if (!OnField(modal) || modal.Choices[modal.Index].FieldTarget is not { } target) return null;
        var kind = target.Location switch
        {
            FieldLocation.MonsterZone => FieldObjectKind.Monster,
            FieldLocation.PendulumZone => FieldObjectKind.Pendulum,
            _ => FieldObjectKind.SpellTrap
        };
        foreach (var row in DuelFieldRows.Build(view))
            foreach (var slot in row)
                if (slot.Own == (target.Player == view.Viewer) && slot.Kind == kind && slot.Index == target.Zone) return slot;
        return null;
    }
}
