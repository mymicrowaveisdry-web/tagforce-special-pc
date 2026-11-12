using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Spells and Traps that target cards in a Graveyard or banished, to add, banish or shuffle them.</summary>
public static class GraveyardAndBanishedDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.Trap(5397, "Disappear", [new BanishTargetsStep()], targets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Opponent, new AnyCardFilter())]),
        t.Normal(4845, "Gravedigger Ghoul", [new BanishTargetsStep()], targets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Opponent, new AnyMonsterFilter(), 1, 2)]),
        t.Normal(7751, "Recycling Batteries", [new AddTargetsToHandStep()], targets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own,
            new AllOfFilter(OfType(Thunder), new MonsterTraitFilter(MonsterStat.Attack, 1500)), 2, 2)]),
        t.Trap(10561, "Dragoncarnation", [new AddTargetsToHandStep()], targets: [new ZoneTarget(CardZone.Banished, TargetSide.Own, OfType(Dragon))]),
        t.Normal(8210, "Psychic Path", [new AddTargetsToHandStep()], new PayLifePointsCost(800), [new ZoneTarget(CardZone.Banished, TargetSide.Own, OfType(Psychic), 1, 2)]),
        t.Normal(9847, "Fish and Swaps", [new AddTargetsToHandStep()], new DiscardCost(), [new ZoneTarget(CardZone.Banished, TargetSide.Own, OfType(Fish, SeaSerpent, Aqua), 2, 2)]),
        t.Trap(7789, "Psychic Overload", [new ShuffleTargetsIntoDeckStep(StopUnlessAll: true), new DrawCardsStep(2)],
            targets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, OfType(Psychic), 3, 3)], exceptions: "Draws only if all 3 targets are shuffled into the Deck.")
    ];
}
