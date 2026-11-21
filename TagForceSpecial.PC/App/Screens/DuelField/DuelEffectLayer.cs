using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Draws transient duel effects over the field; currently only the outcome banner.</summary>
public static class DuelEffectLayer
{
    public static void Draw(DuelDrawList list, DuelView view)
    {
        if (view.Outcome is not null) list.Text(DuelPart.Effects, view.Prompt, 122, 118, DuelPalette.Gold, 2);
    }
}
