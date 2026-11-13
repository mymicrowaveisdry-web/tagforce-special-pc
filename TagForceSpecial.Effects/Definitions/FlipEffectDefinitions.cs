using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterAttributes;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Flip monsters whose "FLIP:" effect is one step: search, recruit, send, damage or destroy-all.</summary>
public static class FlipEffectDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.FlipEffect(8755, "Super-Nimble Mega Hamster", [new SpecialSummonStep(SpecialSummonSources.Deck, new MonsterLevelFilter(3, Beast), BattlePosition.FaceDownDefense)],
            optional: true),
        t.FlipEffect(4970, "Bubonic Vermin", [new SpecialSummonStep(SpecialSummonSources.Deck, Named(4970), BattlePosition.FaceDownDefense)], optional: true),
        t.FlipEffect(9441, "Gem-Turtle", Search(Named(8918)), optional: true),
        t.FlipEffect(6303, "Machina Defender", Search(Named(6307))),
        t.FlipEffect(10817, "Tour Bus To Forbidden Realms", Search(new AllOfFilter(OfType(Fiend), new NotFilter(OfAttribute(Dark)), new NotFilter(OfAttribute(Light))))),
        t.FlipEffect(10746, "Baby Raccoon Tantan", [new SpecialSummonStep(SpecialSummonSources.Deck,
            new AllOfFilter(new MonsterLevelFilter(2, Beast, minLevel: 2), new NotFilter(Named(10746))))]),
        t.FlipEffect(10164, "Dust Knight", [new SendFromDeckToGraveyardStep(OfAttribute(Earth))]),
        t.FlipEffect(5769, "Iron Blacksmith Kotetsu", Search(EquipSpell)),
        t.FlipEffect(10640, "Cheepcheepcheep", [new SpecialSummonStep(SpecialSummonSources.Deck, new AllOfFilter(Tuner, new MonsterLevelFilter(12, minLevel: 5)))],
            optional: true),
        t.FlipEffect(5661, "Des Koala", [new DealDamageStep(400, EffectCount.OpponentHand)]),
        t.FlipEffect(5139, "4-Starred Ladybug of Doom", [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Opponent, FieldCardFilter.FaceUp, Level(4))]),
        t.FlipEffect(7439, "Magnetic Mosquito", [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp, OfType(Machine))])
    ];
}
