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
        t.Continuous(5161, "Yellow Luster Shield", trap: false, new LastingStatModifier(TargetSide.Own, 0, 300)),
        t.Field(4899, "Chorus of Sanctuary", new LastingStatModifier(TargetSide.Either, 0, 500, new NotFilter(new FaceUpAttackFilter()))),
        t.Field(7457, "Acidic Downpour", new LastingStatModifier(TargetSide.Either, -500, 400, OfAttribute(Earth))),
        t.Field(7627, "Wetlands", new LastingStatModifier(TargetSide.Either, 1200, 0, new AllOfFilter(OfType(Aqua), OfAttribute(Water), new MonsterLevelFilter(2)))),
        t.Field(4337, "Wasteland", new LastingStatModifier(TargetSide.Either, 200, 200, OfType(Dinosaur, Zombie, Rock))),
        t.AttributeEquip(4308, "Sword of Dark Destruction", Dark),
        t.AttributeEquip(4330, "Invigoration", Earth),
        t.AttributeEquip(4313, "Elf's Light", Light),
        t.AttributeEquip(4315, "Steel Shell", Water),
        t.AttributeEquip(4846, "Burning Spear", Fire),
        t.AttributeEquip(4847, "Gust Fan", Wind),
        t.Continuous(5327, "The A. Forces", trap: false, new LastingStatModifier(TargetSide.Own, 200, 0, OfType(Warrior), perCount: LastingCount.OwnFaceUpMonsters)
            { CountFilter = OfType(Warrior, Spellcaster) }),
        t.Continuous(9525, "TGX300", trap: false, new LastingStatModifier(TargetSide.Own, 300, 0, perCount: LastingCount.OwnFaceUpMonsters)
            { CountFilter = ArchetypeMonsters("official:tg") }),
        t.Continuous(10185, "Chronomaly Pyramid Eye Tablet", trap: false, new LastingStatModifier(TargetSide.Own, 800, 0, ArchetypeMonsters("official:chronomaly"))),
        t.Equip(6139, "Ritual Weapon", 1500, 1500, new AllOfFilter(Ritual, new MonsterLevelFilter(6))),
        t.Equip(6138, "Fusion Weapon", 1500, 1500, new AllOfFilter(Fusion, new MonsterLevelFilter(6))),
        t.Field(4340, "Umi", new LastingStatModifier(TargetSide.Either, 200, 200, OfType(Fish, SeaSerpent, Thunder, Aqua)),
            new LastingStatModifier(TargetSide.Either, -200, -200, OfType(Machine, Pyro)))
    ];
}
