using TagForceSpecial.Core;
using TagForceSpecial.Effects.Archetypes;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterAttributes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Genex monsters that search for, recruit or Summon other "Genex" monsters, referred to by the reviewed archetype ID.</summary>
public static class GenexDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t)
    {
        var genex = ClassicArchetypes.Filter("official:genex", monstersOnly: true);
        EffectStep[] overseer = [new SpecialSummonStep(SpecialSummonSources.Hand, new AllOfFilter(genex, new MonsterLevelFilter(3)))];
        return
        [
            t.Trigger(8104, "Genex Blastfan", MonsterEventKind.SpecialSummoned, Search(new AllOfFilter(genex, OfAttribute(Dark))), TriggerRequirement.Optional),
            t.BattleDestroyed(7810, "Genex Searcher", [new SpecialSummonStep(SpecialSummonSources.Deck,
                new AllOfFilter(genex, new MonsterTraitFilter(MonsterStat.Attack, 1500)), BattlePosition.FaceUpAttack)], optional: true),
            t.TributeSelf(7811, "Genex Worker", [new SpecialSummonStep(SpecialSummonSources.Hand, genex)]),
            t.Trigger(8246, "R-Genex Turbo", MonsterEventKind.NormalSummoned, Search(new AllOfFilter(genex, new MonsterLevelFilter(1))), TriggerRequirement.Optional),
            t.WithTrigger(t.Trigger(8247, "R-Genex Overseer", MonsterEventKind.NormalSummoned, overseer, TriggerRequirement.Optional, rules: TunerRules,
                getterKind: TunerKind), "special", [MonsterEventKind.SpecialSummoned], EffectRelation.Self, overseer, TriggerRequirement.Optional)
        ];
    }
}
