using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterAttributes;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Equip, Field and Continuous Spells whose only effect is a lasting ATK/DEF change.</summary>
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
        t.Continuous(5327, "The A. Forces", trap: false, new LastingStatModifier(TargetSide.Own, 200, 0, OfType(Warrior), perCount: LastingCount.OwnFaceUpMonsters)
            { CountFilter = OfType(Warrior, Spellcaster) }),
        t.Equip(6139, "Ritual Weapon", 1500, 1500, new AllOfFilter(Ritual, new MonsterLevelFilter(6))),
        t.Equip(6138, "Fusion Weapon", 1500, 1500, new AllOfFilter(Fusion, new MonsterLevelFilter(6))),
        t.Field(4340, "Umi", new LastingStatModifier(TargetSide.Either, 200, 200, OfType(Fish, SeaSerpent, Thunder, Aqua)),
            new LastingStatModifier(TargetSide.Either, -200, -200, OfType(Machine, Pyro)))
    ];
}
