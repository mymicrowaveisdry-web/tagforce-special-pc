using TagForceSpecial.Core;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Spells and Traps that destroy every matching card on a side without targeting.</summary>
public static class BoardRemovalDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.Normal(4343, "Raigeki", [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Opponent)]),
        t.Normal(4678, "Harpie's Feather Duster", [new DestroyAllStep(FieldCardKind.SpellTrap, TargetSide.Opponent)]),
        t.Normal(3827, "Harpie's Feather Duster", [new DestroyAllStep(FieldCardKind.SpellTrap, TargetSide.Opponent)])
    ];
}
