using TagForceSpecial.Core;
using TagForceSpecial.Effects.Archetypes;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Naturia monsters that recruit or recover other "Naturia" monsters when sent from the field, referred to by the reviewed archetype ID.</summary>
public static class NaturiaDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t)
    {
        var naturia = ClassicArchetypes.Filter("official:naturia", monstersOnly: true);
        return
        [
            t.SentToGraveyard(8244, "Naturia Cliff", [new SpecialSummonStep(SpecialSummonSources.Deck, new AllOfFilter(naturia, new MonsterLevelFilter(4)),
                BattlePosition.FaceUpAttack)], optional: true, TriggerEventCause.FromField),
            t.Trigger(9440, "Naturia Eggplant", MonsterEventKind.SentToGraveyard, [new AddTargetsToHandStep()], TriggerRequirement.Optional,
                TriggerSourceLocation.GraveyardSelf, TriggerEventCause.FromField,
                targets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new AllOfFilter(naturia, new NotFilter(Named(9440))))])
        ];
    }
}
