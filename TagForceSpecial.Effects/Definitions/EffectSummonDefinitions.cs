using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterAttributes;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Spells and Traps that Special Summon a monster from the hand, Deck or Graveyard, some after a Tribute or in answer to an opponent's Summon.</summary>
public static class EffectSummonDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.Normal(3518, "Doubulldog", [new SpecialSummonStep(SpecialSummonSources.Deck | SpecialSummonSources.Graveyard, Named(3516))],
            conditions: [Controls(OfType(Beast))]),
        t.Trap(5490, "Hidden Soldiers", [new SpecialSummonStep(SpecialSummonSources.Hand, new AllOfFilter(new MonsterLevelFilter(4), OfAttribute(Dark)))],
            conditions: TrapTiming.OpponentSummons(SummonKinds.Normal | SummonKinds.Flip)),
        t.Trap(6817, "Hunting Instinct", [new SpecialSummonStep(SpecialSummonSources.Hand, OfType(Dinosaur))], conditions: TrapTiming.OpponentSummons(SummonKinds.Special)),
        t.Normal(3741, "Crowning of the Emperor", [new SpecialSummonStep(SpecialSummonSources.Hand | SpecialSummonSources.Deck, Named(3740))],
            new TributeCost(Named(3739), Position: FieldCardFilter.FaceUp)),
        t.Trap(10022, "Evo-Instant", [new SpecialSummonStep(SpecialSummonSources.Deck, ArchetypeMonsters("official:evolsaur"))], new TributeCost(OfType(Reptile))),
        t.Normal(5758, "Sage's Stone", [new SpecialSummonStep(SpecialSummonSources.Hand | SpecialSummonSources.Deck, Named(DarkMagician))],
            conditions: [Controls(Named(DarkMagicianGirl))], exceptions: "The condition is checked only when the card is activated."),
        t.Normal(3759, "D - Mind", [new SpecialSummonStep(SpecialSummonSources.Deck, new AllOfFilter(ArchetypeMonsters("official:destiny-hero"), new MonsterLevelFilter(3)))],
            conditions: [Controls(ArchetypeMonsters("official:destiny-hero"), max: 0)]),
        t.Normal(5909, "Ultra Evolution Pill", [new SpecialSummonStep(SpecialSummonSources.Hand, OfType(Dinosaur))], new TributeCost(OfType(Reptile)))
    ];
}
