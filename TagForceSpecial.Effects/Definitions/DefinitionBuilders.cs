using TagForceSpecial.Core;

namespace TagForceSpecial.Effects.Definitions;

/// <summary>Filters, rules and short builders shared by the definition modules; effect IDs still come from the table passed in.</summary>
internal static class DefinitionBuilders
{
    public const string EndsWithEndPhase = "The change ends when the End Phase begins.";
    public const uint FlipKind = 24, TunerKind = 16;

    public static readonly DuelCardRules HighLevel = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeSummon: true,
        AllowsTributeForSummon: true);
    public static readonly DuelCardRules TunerRules = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true,
        AllowsSummonMaterial: true, IsTuner: true);
    public static readonly FieldTarget FaceUpMonster = new(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp);

    /// <summary>The six catalog records of the printed card "Blue-Eyes White Dragon": same name, statistics, codes and text.</summary>
    public static readonly uint[] BlueEyesWhiteDragon = [3800, 3801, 3802, 3849, 3862, 4007];

    /// <summary>The catalog records of the printed cards "Dark Magician" and "Dark Magician Girl": same name, statistics, codes and text.</summary>
    public static readonly uint[] DarkMagician = [3806, 3808, 3851, 4041, 5165], DarkMagicianGirl = [3832, 3833, 3856, 4766];

    /// <summary>"If [side] control(s) [count] face-up monster(s) matching ...", checked when activating or Summoning.</summary>
    public static ControlsMonstersCondition Controls(CardFilter filter, TargetSide side = TargetSide.Own, int min = 1, int max = int.MaxValue) =>
        new(filter, side, min, max);

    /// <summary>The two catalog records of the printed card "Polymerization": same name, codes and text.</summary>
    public static readonly uint[] Polymerization = [4837, 4867];

    /// <summary>Counter Trap Cards by their decoded icon.</summary>
    public static readonly CardIconFilter CounterTrap = new(CardCategory.Trap, 1);

    /// <summary>Spell Cards by their decoded icon.</summary>
    public static readonly CardIconFilter FieldSpell = new(CardCategory.Spell, 2), EquipSpell = new(CardCategory.Spell, 3);

    public static readonly MonsterKindFilter Fusion = new(MonsterKind.Fusion), Ritual = new(MonsterKind.Ritual), Tuner = new(MonsterKind.Tuner),
        Synchro = new(MonsterKind.Synchro);

    public static MonsterTypeFilter OfType(params uint[] types) => new(types);

    /// <summary>Monster members of a reviewed archetype; the active resolver evaluates membership when the effect runs.</summary>
    public static CardFilter ArchetypeMonsters(string archetypeId) => Archetypes.ClassicArchetypes.Filter(archetypeId, monstersOnly: true);
    public static MonsterAttributeFilter OfAttribute(uint attribute) => new(attribute);
    public static CardIdFilter Named(params uint[] ids) => new(ids);
    public static MonsterLevelFilter Level(uint level) => new(level, minLevel: level);
    public static EffectStep[] Search(CardFilter filter) => [new AddFromDeckToHandStep(filter)];

    private static TriggerRequirement Requirement(bool optional) => optional ? TriggerRequirement.Optional : TriggerRequirement.Mandatory;

    /// <summary>"When this card is destroyed by battle and sent to the Graveyard".</summary>
    public static AuthoredCard BattleDestroyed(this CardTable t, uint id, string name, EffectStep[] steps, bool optional = false, uint getterKind = 1,
        DuelCardRules? rules = null, EffectTarget[]? targets = null, TriggerTiming timing = TriggerTiming.When, int lifePointCost = 0) =>
        t.Trigger(id, name, MonsterEventKind.SentToGraveyard, steps, Requirement(optional), TriggerSourceLocation.GraveyardSelf, TriggerEventCause.ByBattle, timing,
            targets, getterKind: getterKind, rules: rules, lifePointCost: lifePointCost);

    /// <summary>"During your Standby Phase: ..." of this face-up monster.</summary>
    public static AuthoredCard StandbyTrigger(this CardTable t, uint id, string name, EffectStep[] steps, EffectTarget[]? targets = null, DuelCardRules? rules = null) =>
        t.Trigger(id, name, MonsterEventKind.StandbyPhase, steps, targets: targets, rules: rules);

    /// <summary>"When this card is sent to the Graveyard".</summary>
    public static AuthoredCard SentToGraveyard(this CardTable t, uint id, string name, EffectStep[] steps, bool optional = false,
        TriggerEventCause cause = TriggerEventCause.Any, uint getterKind = 1, DuelCardRules? rules = null) =>
        t.Trigger(id, name, MonsterEventKind.SentToGraveyard, steps, Requirement(optional), TriggerSourceLocation.GraveyardSelf, cause, getterKind: getterKind, rules: rules);

    /// <summary>"FLIP: ..."; <paramref name="optional"/> for "FLIP: You can ...".</summary>
    public static AuthoredCard FlipEffect(this CardTable t, uint id, string name, EffectStep[] steps, bool optional = false) =>
        t.Trigger(id, name, MonsterEventKind.FlippedFaceUp, steps, Requirement(optional), getterKind: FlipKind);

    /// <summary>A trigger of this face-up monster on a battle event.</summary>
    public static AuthoredCard BattleTrigger(this CardTable t, uint id, string name, MonsterEventKind when, EffectStep[] steps, bool optional = false,
        uint getterKind = 1, DuelCardRules? rules = null, string exceptions = "none") =>
        t.Trigger(id, name, when, steps, Requirement(optional), getterKind: getterKind, rules: rules, exceptions: exceptions);

    /// <summary>"You can Tribute this card; ...".</summary>
    public static AuthoredCard TributeSelf(this CardTable t, uint id, string name, EffectStep[] steps, EffectTarget[]? targets = null, DuelCardRules? rules = null) =>
        t.Ignition(id, name, steps, new TributeSelfCost(), targets, rules: rules);

    /// <summary>"You can discard this card; add 1 [card] from your Deck to your hand".</summary>
    public static AuthoredCard DiscardToSearch(this CardTable t, uint id, string name, uint target) =>
        t.FromZone(id, name, EffectOrigin.Hand, new EffectOption([new AddFromDeckToHandStep(Named(target))], new SendSelfCost(SelfDestination.Discard)));

    /// <summary>"Equip only to a [Attribute] monster. It gains 400 ATK and loses 200 DEF."</summary>
    public static AuthoredCard AttributeEquip(this CardTable t, uint id, string name, uint attribute) => t.Equip(id, name, 400, -200, OfAttribute(attribute));

    /// <summary>"Target 1 face-up monster; it gains [ATK/DEF] until the end of this turn", as a Trap or a Quick-Play Spell.</summary>
    public static AuthoredCard Boost(this CardTable t, uint id, string name, bool trap, int attack, int defense) => trap
        ? t.Trap(id, name, [new ModifyTargetsUntilEndStep(attack, defense)], targets: [FaceUpMonster], exceptions: EndsWithEndPhase)
        : t.QuickPlay(id, name, [new ModifyTargetsUntilEndStep(attack, defense)], targets: [FaceUpMonster], exceptions: EndsWithEndPhase);
}
