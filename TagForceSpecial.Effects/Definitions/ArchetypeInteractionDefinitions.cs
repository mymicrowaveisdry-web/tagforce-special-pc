using TagForceSpecial.Core;
using TagForceSpecial.Effects.Archetypes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>
/// Cards whose costs or targets name a reviewed archetype. They refer to it only by archetype ID; membership comes from the active
/// archetype content when the effect runs.
/// </summary>
public static class ArchetypeInteractionDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.Trap(10094, "Hieratic Seal of Banishment", [new BanishFieldTargetsStep()], new TributeCost(ClassicArchetypes.Filter("official:hieratic", monstersOnly: true)),
            [new FieldTarget(FieldCardKind.Any, TargetSide.Opponent)]),
        t.Trap(6516, "The Forces of Darkness", [new AddTargetsToHandStep()], targets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own,
            ClassicArchetypes.Filter("official:dark-world", monstersOnly: true), 2, 2)])
    ];
}
