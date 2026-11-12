using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>
/// Monsters with one activated effect: from the field (often by Tributing themselves), or by discarding or banishing themselves
/// from the hand or Graveyard.
/// </summary>
public static class MonsterActivatedEffectDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.Ignition(5953, "The Agent of Creation - Venus", [new SpecialSummonStep(SpecialSummonSources.Hand | SpecialSummonSources.Deck, Named(5949))], new PayLifePointsCost(500)),
        t.Ignition(8376, "Beast Striker", [new SpecialSummonStep(SpecialSummonSources.Deck, Named(8375))], new DiscardCost(), oncePerTurn: true),
        t.Ignition(5205, "Hysteric Fairy", [new GainLifePointsStep(1000)], new TributeCost(Count: 2)),
        t.TributeSelf(6298, "Kaibaman", [new SpecialSummonStep(SpecialSummonSources.Hand, Named(BlueEyesWhiteDragon))]),
        t.TributeSelf(6177, "The Creator Incarnate", [new SpecialSummonStep(SpecialSummonSources.Hand, Named(6176))]),
        t.TributeSelf(9815, "Evilswarm Ketos", [new DestroyTargetsStep()], [new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Opponent)]),
        t.TributeSelf(9816, "Evilswarm O'lantern", [new DestroyTargetsStep()], [new FieldTarget(FieldCardKind.Monster, TargetSide.Opponent, FieldCardFilter.FaceUp)]),
        t.TributeSelf(10229, "Deep Sweeper", [new DestroyTargetsStep()], [new FieldTarget(FieldCardKind.SpellTrap)]),
        t.TributeSelf(8782, "Second Booster", [new ModifyTargetsUntilEndStep(1500)], [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUpAttack)]),
        t.FromZone(6949, "Destiny HERO - Malicious", EffectOrigin.Graveyard, new EffectOption([new SpecialSummonStep(SpecialSummonSources.Deck, Named(6949))],
            new SendSelfCost(SelfDestination.Banish)), rules: HighLevel),
        t.DiscardToSearch(7107, "Warrior of Atlantis", 5387),
        t.DiscardToSearch(8022, "Assault Beast", 8058),
        t.DiscardToSearch(7106, "Gravekeeper's Commandant", 5533),
        t.FromZone(7209, "Neo Space Pathfinder", EffectOrigin.Hand, new EffectOption([new AddFromDeckOrGraveyardStep(Named(6775))], new SendSelfCost(SelfDestination.Discard)))
    ];
}
