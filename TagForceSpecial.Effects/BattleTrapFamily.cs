using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Normal Traps activated when an opponent's monster declares an attack.</summary>
public static class BattleTrapFamily
{
    public const string FamilyId = "battle-trap-definition";
    public const string BatchId = "tfsp-family-batch-10c-04d-1";

    private const string Timing = "Set card, not the turn it was set: the attack declaration window of an opponent's attack, or a chain in that window; Spell Speed 2. Not during the Damage Step.";
    private const string TargetCleanup = "If the attacker left the field, the effect does not resolve. Ordinary cleanup.";
    private const string AllCleanup = "Affects matching cards at resolution, never the resolving card itself. Ordinary cleanup.";
    private const string Attacker = "The attacking monster (bound automatically).";

    private static readonly EffectCondition[] Conditions = [new OpponentAttackDeclaredCondition()];

    private static DefinitionCardProfile Card(uint id, string name, string operation, EffectStep[] steps, string targets,
        string requirement, string negation, bool targetsAttacker = false, string choices = "none") =>
        new(id, name, CardKind.Trap,
            new($"battle-trap-{id}-v1", id, EffectKind.NormalTrap, steps, Conditions,
                fieldTargets: targetsAttacker ? [new AttackingMonsterTarget()] : null),
            operation, $"{Timing} {requirement}", "none", targets, choices, "Default.", negation, "none", [], []);

    public static IReadOnlyList<DefinitionCardProfile> Cards { get; } = Array.AsReadOnly(new[]
    {
        Card(4887, "Mirror Force", "Normal Trap: destroy all Attack Position monsters your opponent controls.",
            [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Opponent, FieldCardFilter.AttackPosition)], "none (not targeting).",
            "An opponent's monster declared an attack.", AllCleanup),
        Card(5124, "Magic Cylinder", "Normal Trap: target the attacker; negate the attack, then inflict damage equal to its ATK.",
            [new NegateAttackStep(), new DamageByTargetAttackStep()], Attacker, "An opponent's monster declared an attack.",
            "If the attack cannot be negated, no damage. " + TargetCleanup, true),
        Card(5994, "Draining Shield", "Normal Trap: target the attacker; negate the attack, then gain LP equal to its ATK.",
            [new NegateAttackStep(), new GainByTargetAttackStep()], Attacker, "An opponent's monster declared an attack.",
            "If the attack cannot be negated, no LP gain. " + TargetCleanup, true),
        Card(5799, "Sakuretsu Armor", "Normal Trap: target the attacker; destroy it.",
            [new DestroyTargetsStep()], Attacker, "An opponent's monster declared an attack.", TargetCleanup, true),
        Card(7400, "Dimensional Prison", "Normal Trap: target the attacker; banish it.",
            [new BanishFieldTargetsStep()], Attacker, "An opponent's monster declared an attack.", TargetCleanup, true),
        Card(4692, "Widespread Ruin", "Normal Trap: destroy the opponent's Attack Position monster with the highest ATK.",
            [new DestroyExtremeMonsterStep(TargetSide.Opponent, MonsterStat.Attack, StatExtreme.Highest, FieldCardFilter.AttackPosition)],
            "none (not targeting).", "An opponent's monster declared an attack.", AllCleanup, choices: "At resolution, choose among tied monsters."),
        Card(3829, "Widespread Ruin", "Normal Trap: destroy the opponent's Attack Position monster with the highest ATK.",
            [new DestroyExtremeMonsterStep(TargetSide.Opponent, MonsterStat.Attack, StatExtreme.Highest, FieldCardFilter.AttackPosition)],
            "none (not targeting).", "An opponent's monster declared an attack.", AllCleanup, choices: "At resolution, choose among tied monsters."),
        Card(5493, "Malevolent Catastrophe", "Normal Trap: destroy all Spell and Trap Cards on the field.",
            [new DestroyAllStep(FieldCardKind.SpellTrap)], "none (not targeting).",
            "An opponent's monster declared an attack; needs another Spell/Trap on the field.", AllCleanup),
        Card(5858, "Dark Mirror Force", "Normal Trap: banish all Defense Position monsters your opponent controls.",
            [new BanishAllStep(TargetSide.Opponent, FieldCardFilter.DefensePosition)], "none (not targeting).",
            "An opponent's monster declared an attack; needs an opponent Defense Position monster.", AllCleanup)
    });

    public static IReadOnlyList<EffectDefinition> Definitions { get; } = Array.AsReadOnly(Cards.Select(c => c.Definition).ToArray());

    public static IReadOnlyDictionary<uint, DuelCardRules> Rules { get; } = Cards.ToFrozenDictionary(c => c.CardId,
        _ => new DuelCardRules(CardCategory.Trap, AllowsSpellTrapSet: true));

    public static IReadOnlyDictionary<uint, CardStaticMetadata> Reviewed { get; } = Cards.ToFrozenDictionary(c => c.CardId,
        c => new CardStaticMetadata(c.Kind, new(CardEffectState.Referenced, [c.Definition.EffectId])));

    /// <summary>Throws when the catalog's metadata or decoded kind codes differ from this family.</summary>
    public static void ValidateCatalog(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 is { } hash && hash != TfspCardMetadata.CardInfoSha256)
            throw new InvalidDataException("This family has not been reviewed against this source build.");
        var byId = catalog.Cards.ToDictionary(c => c.GameCardId);
        foreach (var card in Cards)
        {
            if (!byId.TryGetValue(card.CardId, out var actual) || actual.Name != card.Name || actual.Kind != card.Kind ||
                actual.Effects is not { State: CardEffectState.Referenced } effects || !effects.References.SequenceEqual([card.Definition.EffectId]) ||
                actual.Codes is { } codes && (codes.GetterKind != 14u || codes.Icon != 0u))
                throw new InvalidDataException($"Card {card.CardId} metadata differs from this family.");
        }
    }
}
