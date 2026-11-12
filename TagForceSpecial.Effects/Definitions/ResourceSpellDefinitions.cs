using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterAttributes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Spells that draw, search, deal damage or change LP in one step, some after a cost.</summary>
public static class ResourceSpellDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.Normal(9664, "Thunder Short", [new DealDamageStep(400, EffectCount.OpponentMonsters)]),
        t.Modal(4344, "Mooyan Curry", EffectKind.NormalSpell, "The player who gains LP is chosen at activation.",
            new EffectOption([new GainLifePointsStep(200)]), new EffectOption([new GainLifePointsStep(200, EffectPlayers.Opponent)])),
        t.Normal(8337, "Advance Draw", [new DrawCardsStep(2)], new TributeCost(new MonsterLevelFilter(12, minLevel: 8), Position: FieldCardFilter.FaceUp)),
        t.Normal(9933, "Creeping Darkness", Search(new AllOfFilter(Level(4), OfAttribute(Dark))), new BanishFromGraveyardCost(OfAttribute(Dark), 2))
    ];
}
