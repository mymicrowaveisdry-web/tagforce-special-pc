using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterAttributes;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Monsters that trigger from the Graveyard when destroyed by battle or sent there: searchers, recruiters, draws, damage and LP.</summary>
public static class GraveyardTriggerDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.BattleDestroyed(8812, "Key Mouse", Search(new MonsterLevelFilter(3, Beast)), optional: true, TunerKind, TunerRules),
        t.BattleDestroyed(8784, "Dread Dragon", Search(new MonsterLevelFilter(3, Dragon)), optional: true, TunerKind, TunerRules),
        t.BattleDestroyed(8082, "Rai-Mei", Search(new AllOfFilter(new MonsterLevelFilter(2), OfAttribute(Light))), optional: true),
        t.BattleDestroyed(8614, "Tricular", [new SpecialSummonStep(SpecialSummonSources.Hand | SpecialSummonSources.Deck, Named(8613))], optional: true),
        t.BattleDestroyed(8613, "Bicular", [new SpecialSummonStep(SpecialSummonSources.Hand | SpecialSummonSources.Deck, Named(8612))], optional: true),
        t.BattleDestroyed(8738, "Stygian Security", [new SpecialSummonStep(SpecialSummonSources.Deck, new MonsterLevelFilter(1, Fiend))], getterKind: TunerKind,
            rules: TunerRules),
        t.BattleDestroyed(6388, "UFOroid", [new SpecialSummonStep(SpecialSummonSources.Deck, new AllOfFilter(OfType(Machine), new MonsterTraitFilter(MonsterStat.Attack, 1500)),
            BattlePosition.FaceUpAttack)], optional: true, rules: HighLevel),
        t.BattleDestroyed(8965, "Elephun", [new AddTargetsToHandStep()], optional: true, TunerKind, TunerRules,
            [new ZoneTarget(CardZone.Banished, TargetSide.Own, new AllOfFilter(new MonsterLevelFilter(3), OfType(Beast, BeastWarrior, WingedBeast)))]),
        t.BattleDestroyed(6327, "Mine Golem", [new DealDamageStep(500)]),
        t.BattleDestroyed(7527, "Blizzed, Defender of the Ice Barrier", [new DrawCardsStep(1)]),
        t.BattleDestroyed(8088, "Violet Witch", Search(new AllOfFilter(OfType(Plant), new MonsterTraitFilter(MonsterStat.Defense, 1500))), timing: TriggerTiming.If),
        t.SentToGraveyard(7892, "Botanical Girl", Search(new AllOfFilter(OfType(Plant), new MonsterTraitFilter(MonsterStat.Defense, 1000))), optional: true,
            TriggerEventCause.FromField),
        t.SentToGraveyard(5877, "Burning Algae", [new GainLifePointsStep(1000, EffectPlayers.Opponent)]),
        t.SentToGraveyard(4770, "Skull-Mark Ladybug", [new GainLifePointsStep(1000)]),
        t.SentToGraveyard(7850, "The White Stone of Legend", Search(Named(BlueEyesWhiteDragon)), getterKind: TunerKind, rules: TunerRules)
    ];
}
