using TagForceSpecial.Core;
using TagForceSpecial.Effects.Archetypes;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Gravekeeper's monsters that recruit or search for other "Gravekeeper's" monsters, referred to by the reviewed archetype ID.</summary>
public static class GravekeeperDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t)
    {
        var gravekeeper = ClassicArchetypes.Filter("official:gravekeepers", monstersOnly: true);
        return
        [
            t.FlipEffect(5509, "Gravekeeper's Spy", [new SpecialSummonStep(SpecialSummonSources.Deck,
                new AllOfFilter(gravekeeper, new MonsterTraitFilter(MonsterStat.Attack, 1500)))]),
            t.SentToGraveyard(9282, "Gravekeeper's Recruiter", Search(new AllOfFilter(gravekeeper, new MonsterTraitFilter(MonsterStat.Defense, 1500))),
                cause: TriggerEventCause.FromOwnField)
        ];
    }
}
