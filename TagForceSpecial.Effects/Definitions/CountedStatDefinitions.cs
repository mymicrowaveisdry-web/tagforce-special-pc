using TagForceSpecial.Core;
using TagForceSpecial.Effects.Archetypes;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Monsters whose only effect gains ATK (and DEF) for each matching card their controller has face-up or in the Graveyard, or for each banished monster.</summary>
public static class CountedStatDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.CountedStats(4560, "Lava Battleguard", 500, 0, StatCountZone.OwnFaceUpMonsters, Named(4018)),
        t.CountedStats(4018, "Swamp Battleguard", 500, 0, StatCountZone.OwnFaceUpMonsters, Named(4560)),
        t.CountedStats(5645, "Amazoness Paladin", 100, 0, StatCountZone.OwnFaceUpMonsters, ClassicArchetypes.Filter("official:amazoness", monstersOnly: true)),
        t.CountedStats(6927, "Elemental HERO Heat", 200, 0, StatCountZone.OwnFaceUpMonsters, ClassicArchetypes.Filter("official:elemental-hero", monstersOnly: true)),
        t.CountedStats(9568, "Tenshin", 400, 0, StatCountZone.OwnFaceUpMonsters, Level(2)),
        t.CountedStats(4374, "Shadow Ghoul", 100, 0, StatCountZone.OwnGraveyard),
        t.CountedStats(5356, "Mudora", 200, 0, StatCountZone.OwnGraveyard, OfType(Fairy)),
        t.CountedStats(9581, "Rage of the Deep Sea", 500, 500, StatCountZone.OwnGraveyard, OfType(Fish, SeaSerpent, Aqua)),
        t.CountedStats(6609, "Grass Phantom", 500, 0, StatCountZone.OwnGraveyard, Named(6609)),
        t.CountedStats(6604, "Beelze Frog", 300, 0, StatCountZone.OwnGraveyard, Named(6425)),
        t.CountedStats(6646, "Helios - The Primordial Sun", 100, 100, StatCountZone.BanishedMonsters, unknownIsZero: true)
    ];
}
