using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterAttributes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Spells and Traps that draw, search, send Deck cards, deal damage or change LP in one step, some after a cost.</summary>
public static class ResourceSpellDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.Normal(9664, "Thunder Short", [new DealDamageStep(400, EffectCount.OpponentMonsters)]),
        t.Modal(4344, "Mooyan Curry", EffectKind.NormalSpell, "The player who gains LP is chosen at activation.",
            new EffectOption([new GainLifePointsStep(200)]), new EffectOption([new GainLifePointsStep(200, EffectPlayers.Opponent)])),
        t.Normal(8337, "Advance Draw", [new DrawCardsStep(2)], new TributeCost(new MonsterLevelFilter(12, minLevel: 8), Position: FieldCardFilter.FaceUp)),
        t.Normal(9933, "Creeping Darkness", Search(new AllOfFilter(Level(4), OfAttribute(Dark))), new BanishFromGraveyardCost(OfAttribute(Dark), 2)),
        t.Normal(8656, "Cards of Consonance", [new DrawCardsStep(2)], new DiscardCost(new AllOfFilter(OfType(MonsterTypes.Dragon), Tuner,
            new MonsterTraitFilter(MonsterStat.Attack, 1000)))),
        t.Trap(5739, "Arsenal Robber", [new OpponentSendsFromDeckStep(EquipSpell)]),
        t.Normal(9122, "Tuning", [new AddFromDeckToHandStep(new AllOfFilter(ArchetypeMonsters("official:synchron"), Tuner)), new MillTopOfDeckStep(1)]),
        t.Normal(5474, "Toon Table of Contents", Search(new AnyOfFilter(Archetypes.ClassicArchetypes.Filter("official:toon"), Named(4733)))),
        t.Normal(10414, "Gagagadraw", [new DrawCardsStep(2)], new BanishFromGraveyardCost(ArchetypeMonsters("official:gagaga"), 3)),
        t.Trap(7767, "Psychic Rejuvenation", [new GainLifePointsStep(1000, PerCount: EffectCount.OwnFaceUpMonsters) { CountFilter = OfType(MonsterTypes.Psychic) }]),
        t.Trap(6676, "Elemental Recharge", [new GainLifePointsStep(1000, PerCount: EffectCount.OwnFaceUpMonsters)
            { CountFilter = Archetypes.ClassicArchetypes.Filter("official:elemental-hero", monstersOnly: true) }])
    ];
}
