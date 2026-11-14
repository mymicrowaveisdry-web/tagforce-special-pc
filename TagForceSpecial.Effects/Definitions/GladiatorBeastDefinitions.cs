using TagForceSpecial.Core;
using TagForceSpecial.Effects.Archetypes;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Cards that search for or recruit "Gladiator Beast" monsters, referred to by the reviewed archetype ID.</summary>
public static class GladiatorBeastDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t)
    {
        var lowLevel = new AllOfFilter(ClassicArchetypes.Filter("official:gladiator-beast", monstersOnly: true), new MonsterLevelFilter(4));
        return
        [
            t.Normal(7460, "Gladiator Proving Ground", Search(lowLevel)),
            t.BattleDestroyed(7290, "Test Ape", [new SpecialSummonStep(SpecialSummonSources.Deck, lowLevel)], optional: true)
        ];
    }
}
