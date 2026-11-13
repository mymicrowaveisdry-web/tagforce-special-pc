using TagForceSpecial.Core;
using TagForceSpecial.Effects.Archetypes;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>
/// Cards whose costs or targets name a reviewed archetype. They refer to it only by archetype ID; membership comes from the active
/// archetype content when the effect runs.
/// </summary>
public static class ArchetypeInteractionDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t)
    {
        static CardFilter Monsters(string archetype) => ClassicArchetypes.Filter(archetype, monstersOnly: true);
        static ZoneTarget Revive(CardFilter filter) => new(CardZone.Graveyard, TargetSide.Own, filter, mustBeSummonable: true);
        var normal = new NormalMonsterFilter(TfspNormalMonsters.Ids);
        var spellbookSearch = Search(new AllOfFilter(ClassicArchetypes.Filter("official:spellbook"), new CardCategoryFilter(CardCategory.Spell)));
        return
        [
            t.Trap(10094, "Hieratic Seal of Banishment", [new BanishFieldTargetsStep()], new TributeCost(Monsters("official:hieratic")),
                [new FieldTarget(FieldCardKind.Any, TargetSide.Opponent)]),
            t.Trap(6516, "The Forces of Darkness", [new AddTargetsToHandStep()], targets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own,
                Monsters("official:dark-world"), 2, 2)]),
            t.WithTrigger(t.Trigger(10145, "Spellbook Magician of Prophecy", MonsterEventKind.NormalSummoned, spellbookSearch), "flip",
                [MonsterEventKind.FlippedFaceUp], EffectRelation.Self, spellbookSearch),
            t.Normal(10416, "Heraldry Reborn", [new SpecialSummonTargetStep()], targets: [Revive(Monsters("official:heraldic-beast"))]),
            t.Normal(9339, "Shien's Smoke Signal", Search(new AllOfFilter(Monsters("official:six-samurai"), new MonsterLevelFilter(3)))),
            t.TributeSelf(8896, "Gem-Knight Alexandrite", [new SpecialSummonStep(SpecialSummonSources.Deck, new AllOfFilter(Monsters("official:gem-knight"), normal))]),
            t.Normal(6674, "O - Oversoul", [new SpecialSummonTargetStep()], targets: [Revive(new AllOfFilter(Monsters("official:elemental-hero"), normal))]),
            t.Normal(6950, "Destiny Draw", [new DrawCardsStep(2)], new DiscardCost(ClassicArchetypes.Filter("official:destiny-hero"))),
            t.Trap(9400, "Infestation Ripples", [new SpecialSummonTargetStep()], new PayLifePointsCost(500),
                [Revive(new AllOfFilter(Monsters("official:steelswarm"), new MonsterLevelFilter(4)))]),
            t.Normal(10080, "Hieratic Seal of Supremacy", [new SpecialSummonStep(SpecialSummonSources.Hand, Monsters("official:hieratic"))]),
            t.QuickPlay(9124, "Golden Gearbox", [new ModifyTargetsUntilEndStep(500, 1500)], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Either,
                FieldCardFilter.FaceUp, cardFilter: Monsters("official:karakuri"))], exceptions: EndsWithEndPhase)
        ];
    }
}
