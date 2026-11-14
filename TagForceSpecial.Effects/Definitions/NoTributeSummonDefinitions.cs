using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterAttributes;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Level 5 or higher monsters that can be Normal Summoned without Tributing while a field condition holds.</summary>
public static class NoTributeSummonDefinitions
{
    /// <summary>The cards, with procedure and effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.WithoutTributing(t.ProcedureMonster(10496, "Parsec, the Interstellar Dragon"), Controls(new MonsterLevelFilter(8, minLevel: 8))),
        t.WithoutTributing(t.ProcedureMonster(7852, "Cyber Shark"), Controls(OfAttribute(Water))),
        t.WithoutTributing(t.ProcedureMonster(7948, "Genex Furnace"), Controls(Named(7807))),
        t.WithoutTributing(t.ProcedureMonster(8620, "Power Invader"), new ControlsMonstersCondition(new AnyMonsterFilter(), TargetSide.Opponent, 2, FaceUpOnly: false)),
        t.WithoutTributing(t.Ignition(10255, "Mecha Sea Dragon Plesion", [new DestroyTargetsStep()], new TributeCost(OfAttribute(Water)),
            [new FieldTarget(FieldCardKind.Any, TargetSide.Opponent, FieldCardFilter.FaceUp)], oncePerTurn: true, rules: HighLevel), Controls(OfType(SeaSerpent)))
    ];
}
