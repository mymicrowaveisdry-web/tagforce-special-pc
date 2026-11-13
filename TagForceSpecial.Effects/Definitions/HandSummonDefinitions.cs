using TagForceSpecial.Core;
using static TagForceSpecial.Effects.Definitions.DefinitionBuilders;
using static TagForceSpecial.Effects.MonsterAttributes;
using static TagForceSpecial.Effects.MonsterTypes;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Monsters that can Special Summon themselves from the hand when a field condition holds; they can also be Normal Summoned.</summary>
public static class HandSummonDefinitions
{
    /// <summary>The cards, with procedure IDs from the registering family's table.</summary>
    public static AuthoredCard[] Cards(CardTable t)
    {
        BattlePosition[] attack = [BattlePosition.FaceUpAttack], defense = [BattlePosition.FaceUpDefense];
        var faceUpSynchro = Synchro;
        return
        [
            t.HandSummon(9302, "Guldfaxe of the Nordic Beasts", [Controls(faceUpSynchro, TargetSide.Opponent)], emptyOwnField: true, getterKind: TunerKind,
                rules: TunerRules),
            t.HandSummon(3522, "Cosmos", [Controls(OfType(Plant), min: 2)]),
            t.HandSummon(10311, "Noble Knight Gawayn", [Controls(new AllOfFilter(OfAttribute(Light), new NormalMonsterFilter(TfspNormalMonsters.Ids)))], defense),
            t.HandSummon(10373, "Heraldic Beast Eale", [Controls(ArchetypeMonsters("official:heraldic-beast"), min: 2)]),
            t.HandSummon(11192, "Prominence Hand", [Controls(Named(11189, 11190, 11191))]),
            t.HandSummon(10144, "Chronomaly Moai", [Controls(ArchetypeMonsters("official:chronomaly"))], defense),
            t.HandSummon(6570, "Ancient Gear", [Controls(Named(6570))], attack),
            t.HandSummon(9090, "Creation Resonator", [Controls(new AllOfFilter(faceUpSynchro, new MonsterLevelFilter(12, minLevel: 8)))], getterKind: TunerKind,
                rules: TunerRules),
            t.HandSummon(10335, "Photon Slasher", [Controls(new XyzMonsterFilter(), TargetSide.Either)], defense),
            t.HandSummon(10227, "Grandram", [Controls(new XyzMonsterFilter(), TargetSide.Opponent)], attack),
            t.HandSummon(9080, "Reed Butterfly", [Controls(faceUpSynchro, TargetSide.Opponent), Controls(faceUpSynchro, max: 0)]),
            t.HandSummon(9890, "Earth Armor Ninja", [], emptyOwnField: true, opposingMonster: true),
            t.HandSummon(9499, "Elder of the Six Samurai", [], emptyOwnField: true, opposingMonster: true),
            t.HandSummon(9013, "Junk Forward", [], emptyOwnField: true)
        ];
    }
}
