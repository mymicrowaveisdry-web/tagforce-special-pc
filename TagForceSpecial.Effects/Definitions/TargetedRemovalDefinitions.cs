using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Spells and Traps that destroy, banish or return targeted cards on the field.</summary>
public static class TargetedRemovalDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.Normal(6512, "Dark World Lightning", [new DestroyTargetsStep(StopUnlessAnyDestroyed: true), new DiscardCardsStep(1)],
            targets: [new FieldTarget(FieldCardKind.Any, TargetSide.Either, FieldCardFilter.FaceDown)]),
        t.Trap(3695, "Twin Vortex", [new DestroyTargetsStep()], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp, cardFilter: OfType(Machine)),
            new FieldTarget(FieldCardKind.Monster, TargetSide.Opponent)]),
        t.Trap(7476, "Offering to the Snake Deity", [new DestroyTargetsStep()], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp,
            cardFilter: OfType(Reptile)), new FieldTarget(FieldCardKind.Any, TargetSide.Opponent, count: 2)]),
        t.Trap(8682, "Lair Wire", [new DestroyTargetsStep()], new BanishFromGraveyardCost(OfType(Insect)), [new FieldTarget(FieldCardKind.Monster, TargetSide.Opponent)]),
        t.Trap(9697, "Gravelstorm", [new ReturnTargetsToHandStep()], targets: [new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Own),
            new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Opponent)]),
        t.QuickPlay(8977, "Double Cyclone", [new DestroyTargetsStep()], targets: [new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Own),
            new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Opponent)]),
        t.Normal(4838, "Remove Trap", [new DestroyTargetsStep()], targets: [new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Either, FieldCardFilter.FaceUp,
            cardFilter: new CardCategoryFilter(CardCategory.Trap))]),
        t.QuickPlay(9924, "Galaxy Storm", [new DestroyTargetsStep()], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp,
            cardFilter: new XyzMonsterFilter()) { MaxXyzMaterials = 0 }]),
        t.Normal(5609, "Dark Core", [new BanishFieldTargetsStep()], new DiscardCost(), [FaceUpMonster]),
        t.Trap(7012, "Ritual Sealing", [new DestroyTargetsStep()], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp, cardFilter: Ritual)]),
        t.Trap(7011, "Anti-Fusion Device", [new DestroyTargetsStep()], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp, cardFilter: Fusion)])
    ];
}
