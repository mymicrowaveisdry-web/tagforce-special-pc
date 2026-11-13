using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Monsters that trigger on the field when Summoned, when they destroy a monster by battle, or when they inflict battle damage.</summary>
public static class SummonAndBattleTriggerDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.Trigger(5883, "Sacred Crane", MonsterEventKind.SpecialSummoned, [new DrawCardsStep(1)], timing: TriggerTiming.If),
        t.Trigger(8249, "R-Genex Magma", MonsterEventKind.NormalSummoned, Search(new AllOfFilter(ArchetypeMonsters("official:r-genex"), Level(2))),
            TriggerRequirement.Optional),
        t.Trigger(8248, "R-Genex Crusher", MonsterEventKind.NormalSummoned, Search(new AllOfFilter(ArchetypeMonsters("official:r-genex"), Level(4))),
            TriggerRequirement.Optional),
        t.Trigger(8264, "Ojama Red", MonsterEventKind.NormalSummoned, [new SpecialSummonStep(SpecialSummonSources.Hand, ArchetypeMonsters("official:ojama"),
            BattlePosition.FaceUpAttack, anyNumber: true)], TriggerRequirement.Optional,
            exceptions: "Up to 4 is the number of free Monster Zones after this card is Summoned."),
        t.WhenSummoned(3611, "Crystal Skull", [new TakeDamageStep(1000)]),
        t.BattleTrigger(3740, "Toy Emperor", MonsterEventKind.DestroyedOpponentByBattle, Search(new CardCategoryFilter(CardCategory.Trap)), optional: true, rules: HighLevel,
            exceptions: "Triggers only when the destroyed monster is sent to the Graveyard."),
        t.BattleTrigger(7837, "Chaos-End Master", MonsterEventKind.DestroyedOpponentByBattle, [new SpecialSummonStep(SpecialSummonSources.Deck,
            new AllOfFilter(new MonsterLevelFilter(12, minLevel: 5), new MonsterTraitFilter(MonsterStat.Attack, 1600)))], optional: true, TunerKind, TunerRules),
        t.BattleTrigger(7338, "Fossil Tusker", MonsterEventKind.DestroyedOpponentByBattle, [new DealDamageStep(400)]),
        t.BattleTrigger(4529, "The Bistro Butcher", MonsterEventKind.InflictedBattleDamage, [new DrawCardsStep(2, EffectPlayers.Opponent)]),
        t.BattleTrigger(4226, "Masked Sorcerer", MonsterEventKind.InflictedBattleDamage, [new DrawCardsStep(1)]),
        t.BattleTrigger(8077, "Rose, Warrior of Revenge", MonsterEventKind.InflictedBattleDamage, [new DealDamageStep(300)], getterKind: TunerKind, rules: TunerRules),
        t.BattleTrigger(5425, "Dark Scorpion Burglars", MonsterEventKind.InflictedBattleDamage, [new OpponentSendsFromDeckStep(new CardCategoryFilter(CardCategory.Spell))])
    ];
}
