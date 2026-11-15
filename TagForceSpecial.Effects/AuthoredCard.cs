using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Raw monster Type codes, checked against well-known monsters in the decoded catalog.</summary>
public static class MonsterTypes
{
    public const uint Dragon = 1, Zombie = 2, Fiend = 3, Pyro = 4, SeaSerpent = 5, Rock = 6, Machine = 7, Fish = 8, Dinosaur = 9, Insect = 10,
        Beast = 11, BeastWarrior = 12, Plant = 13, Aqua = 14, Warrior = 15, WingedBeast = 16, Fairy = 17, Spellcaster = 18, Thunder = 19, Reptile = 20, Psychic = 21;
}

/// <summary>Raw monster Attribute codes, checked against well-known monsters in the decoded catalog.</summary>
public static class MonsterAttributes
{
    public const uint Light = 1, Dark = 2, Water = 3, Fire = 4, Earth = 5, Wind = 6;
}

/// <summary>One card as authored: its identity, rules and effects. Manifest descriptions are generated from the effects.</summary>
public sealed record AuthoredCard(uint Id, string Name, CardKind Kind, uint GetterKind, uint Icon, DuelCardRules Rules,
    IReadOnlyList<EffectDefinition> Definitions, string Exceptions = "none")
{
    public IReadOnlyList<TriggerEffectProfile> Triggers { get; init; } = [];
    /// <summary>Printed Pendulum Scales; checked against the catalog.</summary>
    public IReadOnlyList<PendulumCardProfile> Scales { get; init; } = [];
    public IReadOnlyList<TributeProcedureProfile> TributeProcedures { get; init; } = [];
    public IReadOnlyList<RuleLockProfile> Locks { get; init; } = [];
    public IReadOnlyList<BattleAbilityProfile> BattleAbilities { get; init; } = [];
    public IReadOnlyList<DestructionReplacementProfile> Replacements { get; init; } = [];
    public IReadOnlyList<GraveyardRedirectProfile> Redirects { get; init; } = [];
    public IReadOnlyList<DamageRuleProfile> DamageRules { get; init; } = [];
    /// <summary>The monster's own rules for its ATK, DEF and Level.</summary>
    public MonsterStatProfile? Stats { get; init; }
    /// <summary>How effects may Special Summon the card, for monsters that cannot be Normal Summoned.</summary>
    public IReadOnlyList<SpecialSummonCardProfile> SpecialSummons { get; init; } = [];
    /// <summary>Its own Special Summon procedures ("you can Special Summon this card from your hand" with conditions or costs).</summary>
    public IReadOnlyList<SpecialSummonProcedureProfile> Procedures { get; init; } = [];
    /// <summary>Everything the card's reviewed metadata refers to.</summary>
    public IEnumerable<string> References => Definitions.Select(d => d.EffectId).Concat(Locks.Select(l => l.EffectId)).Concat(BattleAbilities.Select(p => p.EffectId)).Concat(Replacements.Select(r => r.EffectId))
        .Concat(Redirects.Select(r => r.EffectId)).Concat(DamageRules.Select(r => r.EffectId)).Concat(Stats is { } stats ? [stats.EffectId] : []).Concat(Procedures.Select(p => p.ProcedureId)).Concat(TributeProcedures.Select(p => p.ProcedureId));
}

/// <summary>One option of a card whose effects are chosen at activation.</summary>
public sealed record EffectOption(EffectStep[] Steps, EffectCost? Cost = null, EffectTarget[]? Targets = null, EffectCondition[]? Conditions = null);

/// <summary>Activation timings and automatic targets shared by Trap Cards.</summary>
public static class TrapTiming
{
    public static EffectCondition[] OpponentAttacks => [new OpponentAttackDeclaredCondition()];
    public static EffectTarget[] Attacker => [new AttackingMonsterTarget()];
    public static EffectTarget[] Summoned => [new SummonedMonsterTarget()];
    public static EffectCondition[] Responding(CardCategory? category = null) => [new RespondsToActivationCondition(category)];

    /// <summary>The opponent just Summoned a monster whose ATK is in range.</summary>
    public static EffectCondition[] OpponentSummons(SummonKinds kinds, int minAttack = 0, int maxAttack = int.MaxValue, bool single = false) =>
        [new MonsterSummonedCondition(kinds, Single: single, Min: minAttack, Max: maxAttack)];
}
