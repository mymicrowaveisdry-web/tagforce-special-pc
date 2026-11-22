using TagForceSpecial.Cards;
using TagForceSpecial.PC.App.Screens.Album;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Host side of <see cref="DuelAttackPanel"/>, in the captured order (aki-attack 210–289): both cards' art, the two translucent
/// bands between the panels, then the target's and the attacker's compact panels.</summary>
public static class AttackPanels
{
    public static void Draw(CardInfoPanel panel, CardCatalog catalog, DuelAttackPanel attack, int glintFrame)
    {
        CardDefinition? target = attack.TargetId is { } t ? catalog[t] : null, attacker = attack.AttackerId is { } a ? catalog[a] : null;
        if (target is not null) panel.DrawCompactArt(target, CardInfoCompactLayout.Target);
        if (attacker is not null) panel.DrawCompactArt(attacker, CardInfoCompactLayout.Attacker);
        panel.DrawBand(1, 125, 143, 152);
        panel.DrawBand(1, 107, 136, 110);
        panel.DrawCompact(target, CardInfoCompactLayout.Target, glintFrame, attack.TargetTint);
        panel.DrawCompact(attacker, CardInfoCompactLayout.Attacker, glintFrame, attack.AttackerTint);
    }
}
