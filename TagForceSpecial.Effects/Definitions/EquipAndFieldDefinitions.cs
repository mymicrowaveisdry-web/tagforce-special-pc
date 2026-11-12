using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterAttributes;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Equip Spells and Field Spells whose only effect is a lasting ATK/DEF change.</summary>
public static class EquipAndFieldDefinitions
{
    /// <summary>The cards, with effect IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t) =>
    [
        t.AttributeEquip(4308, "Sword of Dark Destruction", Dark),
        t.AttributeEquip(4330, "Invigoration", Earth),
        t.AttributeEquip(4313, "Elf's Light", Light),
        t.AttributeEquip(4315, "Steel Shell", Water),
        t.AttributeEquip(4846, "Burning Spear", Fire),
        t.AttributeEquip(4847, "Gust Fan", Wind),
        t.Field(4340, "Umi", new LastingStatModifier(TargetSide.Either, 200, 200, OfType(Fish, SeaSerpent, Thunder, Aqua)),
            new LastingStatModifier(TargetSide.Either, -200, -200, OfType(Machine, Pyro)))
    ];
}
