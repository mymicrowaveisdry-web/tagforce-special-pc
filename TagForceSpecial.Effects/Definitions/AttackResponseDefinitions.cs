using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Traps activated when an opponent's monster declares an attack, acting on the attacker.</summary>
public static class AttackResponseDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.Trap(3718, "Floral Shield", [new NegateAttackStep(), new DrawCardsStep(1)], targets: TrapTiming.Attacker, conditions: TrapTiming.OpponentAttacks),
        t.Trap(5102, "Mask of Weakness", [new ModifyTargetsUntilEndStep(-700)], targets: TrapTiming.Attacker, conditions: TrapTiming.OpponentAttacks, exceptions: EndsWithEndPhase)
    ];
}
