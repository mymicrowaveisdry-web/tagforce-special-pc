using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Spells and Traps that destroy every matching card on a side without targeting.</summary>
public static class BoardRemovalDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.Normal(4343, "Raigeki", [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Opponent)]),
        t.Normal(4678, "Harpie's Feather Duster", [new DestroyAllStep(FieldCardKind.SpellTrap, TargetSide.Opponent)]),
        t.Normal(3827, "Harpie's Feather Duster", [new DestroyAllStep(FieldCardKind.SpellTrap, TargetSide.Opponent)]),
        t.Trap(6519, "Weed Out", [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUpAttack, Level(3))], new PayLifePointsCost(500)),
        t.Normal(6057, "Dark Magic Attack", [new DestroyAllStep(FieldCardKind.SpellTrap, TargetSide.Opponent)], conditions: [Controls(Named(DarkMagician))],
            exceptions: "The condition is checked only when the card is activated (OCG ruling)."),
        t.Normal(7629, "Short Circuit", [new DestroyAllStep(FieldCardKind.Any, TargetSide.Opponent)],
            conditions: [Controls(Archetypes.ClassicArchetypes.Filter("official:batteryman", monstersOnly: true), min: 3)]),
        t.Normal(9935, "Xyz Burst", [new DestroyAllStep(FieldCardKind.Any, TargetSide.Opponent, FieldCardFilter.FaceDown)],
            conditions: [Controls(new XyzMonsterFilter(MinRank: 6))], exceptions: "The condition is checked only when the card is activated.")
    ];
}
