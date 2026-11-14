using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Spells and Traps that change a targeted monster's ATK or DEF, permanently or until the end of the turn.</summary>
public static class StatChangeDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.Normal(9665, "Aqua Jet", [new ModifyTargetsStep(1000)], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp,
            cardFilter: OfType(Fish, SeaSerpent, Aqua))]),
        t.Trap(6445, "Rising Energy", [new ModifyTargetsUntilEndStep(1500)], new DiscardCost(), [FaceUpMonster], exceptions: EndsWithEndPhase),
        t.Boost(7230, "Shield Spear", trap: true, 400, 400),
        t.Boost(4849, "Reinforcements", trap: true, 500, 0),
        t.Boost(4850, "Castle Walls", trap: true, 0, 500),
        t.Boost(4912, "Snake Fang", trap: true, 0, -500),
        t.Boost(4906, "The Reliable Guardian", trap: false, 0, 700)
    ];
}
