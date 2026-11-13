using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Face-up monsters whose effect resolves during their controller's Standby Phase: damage, LP, stat changes and recruiting.</summary>
public static class PhaseTriggerDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.StandbyTrigger(5687, "Bowganian", [new DealDamageStep(600)]),
        t.StandbyTrigger(6429, "Ebon Magician Curran", [new DealDamageStep(300, EffectCount.OpponentMonsters)]),
        t.StandbyTrigger(5204, "Cure Mermaid", [new GainLifePointsStep(800)]),
        t.StandbyTrigger(5453, "Legendary Fiend", [new ModifyTargetsStep(700)], [new TriggerSourceMonster()], HighLevel),
        t.StandbyTrigger(5646, "Amazoness Blowpiper", [new ModifyTargetsUntilEndStep(-500)],
            [new FieldTarget(FieldCardKind.Monster, TargetSide.Opponent, FieldCardFilter.FaceUp)]),
        t.StandbyTrigger(3739, "Toy Soldier", [new SpecialSummonStep(SpecialSummonSources.Deck, Named(3739), BattlePosition.FaceUpAttack)])
    ];
}
