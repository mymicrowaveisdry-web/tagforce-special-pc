using System.Collections;
using System.Collections.Frozen;
using System.Reflection;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Raw monster Type codes, checked against well-known monsters in the decoded catalog.</summary>
public static class MonsterTypes
{
    public const uint Dragon = 1, Zombie = 2, Fiend = 3, Pyro = 4, SeaSerpent = 5, Rock = 6, Machine = 7, Fish = 8, Dinosaur = 9, Insect = 10,
        Beast = 11, BeastWarrior = 12, Plant = 13, Aqua = 14, Warrior = 15, WingedBeast = 16, Fairy = 17, Spellcaster = 18, Thunder = 19, Psychic = 21;
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
}

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

/// <summary>Short builders for families of cards. Effect IDs take the table's prefix.</summary>
public sealed class CardTable(string prefix)
{
    private const uint SpellKind = 13, TrapKind = 14, EffectMonsterKind = 1, IconNormal = 0, IconField = 2, IconEquip = 3, IconContinuous = 4, IconQuickPlay = 5, IconCounter = 1;
    private static readonly DuelCardRules SpellRules = new(CardCategory.Spell, AllowsSpellTrapSet: true);
    private static readonly DuelCardRules TrapRules = new(CardCategory.Trap, AllowsSpellTrapSet: true);
    private static readonly DuelCardRules MonsterRules = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
    private static readonly FieldTarget FaceUpMonster = new(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp);

    private string Effect(uint id, string role) => $"{prefix}-{id}-{role}-v1";

    private AuthoredCard Activated(uint id, string name, EffectKind kind, EffectStep[] steps, EffectCost? cost, EffectTarget[]? targets, EffectCondition[]? conditions, string exceptions)
    {
        bool trap = kind is EffectKind.NormalTrap or EffectKind.CounterTrap;
        uint icon = kind switch { EffectKind.QuickPlaySpell => IconQuickPlay, EffectKind.CounterTrap => IconCounter, _ => IconNormal };
        var definition = new EffectDefinition(Effect(id, "effect"), id, kind, steps, conditions, cost, fieldTargets: targets);
        return new(id, name, trap ? CardKind.Trap : CardKind.Spell, trap ? TrapKind : SpellKind, icon, trap ? TrapRules : SpellRules, [definition], exceptions);
    }

    public AuthoredCard Normal(uint id, string name, EffectStep[] steps, EffectCost? cost = null, EffectTarget[]? targets = null, EffectCondition[]? conditions = null, string exceptions = "none") =>
        Activated(id, name, EffectKind.NormalSpell, steps, cost, targets, conditions, exceptions);

    public AuthoredCard QuickPlay(uint id, string name, EffectStep[] steps, EffectCost? cost = null, EffectTarget[]? targets = null, EffectCondition[]? conditions = null, string exceptions = "none") =>
        Activated(id, name, EffectKind.QuickPlaySpell, steps, cost, targets, conditions, exceptions);

    public AuthoredCard Trap(uint id, string name, EffectStep[] steps, EffectCost? cost = null, EffectTarget[]? targets = null, EffectCondition[]? conditions = null, string exceptions = "none") =>
        Activated(id, name, EffectKind.NormalTrap, steps, cost, targets, conditions, exceptions);

    public AuthoredCard Counter(uint id, string name, EffectStep[] steps, EffectCondition[] conditions, EffectCost? cost = null, EffectTarget[]? targets = null, string exceptions = "none") =>
        Activated(id, name, EffectKind.CounterTrap, steps, cost, targets, conditions, exceptions);

    /// <summary>A Continuous Spell or Trap whose modifiers apply while it stays face-up.</summary>
    public AuthoredCard Continuous(uint id, string name, bool trap, params LastingStatModifier[] modifiers) =>
        new(id, name, trap ? CardKind.Trap : CardKind.Spell, trap ? TrapKind : SpellKind, IconContinuous, trap ? TrapRules : SpellRules,
            [new EffectDefinition(Effect(id, "continuous"), id, trap ? EffectKind.ContinuousTrap : EffectKind.ContinuousSpell, [], lastingModifiers: modifiers)]);

    /// <summary>A trigger on one event; the default is a mandatory "when" effect on this face-up monster.</summary>
    public AuthoredCard Trigger(uint id, string name, MonsterEventKind when, EffectStep[] steps, TriggerRequirement requirement = TriggerRequirement.Mandatory,
        TriggerSourceLocation location = TriggerSourceLocation.FaceUpMonster, TriggerEventCause cause = TriggerEventCause.Any, TriggerTiming timing = TriggerTiming.When,
        EffectTarget[]? targets = null, MonsterTargetPolicy? targetPolicy = null, DuelCardRules? rules = null, uint getterKind = EffectMonsterKind, string exceptions = "none")
    {
        var definition = new EffectDefinition(Effect(id, "trigger"), id, EffectKind.MonsterTrigger, steps, fieldTargets: targets);
        return new(id, name, CardKind.Monster, getterKind, IconNormal, rules ?? MonsterRules, [definition], exceptions)
        {
            Triggers = [new TriggerEffectProfile(definition, when, EffectRelation.Self, requirement, location, timing, cause, target: targetPolicy)]
        };
    }

    /// <summary>"When this card is Summoned": separate triggers for Normal, Flip and Special Summons.</summary>
    public AuthoredCard WhenSummoned(uint id, string name, EffectStep[] steps, TriggerRequirement requirement = TriggerRequirement.Mandatory, string exceptions = "none")
    {
        (MonsterEventKind Event, string Role)[] events = [(MonsterEventKind.NormalSummoned, "normal"), (MonsterEventKind.FlipSummoned, "flip"), (MonsterEventKind.SpecialSummoned, "special")];
        var definitions = events.Select(e => new EffectDefinition(Effect(id, e.Role), id, EffectKind.MonsterTrigger, steps)).ToArray();
        return new(id, name, CardKind.Monster, EffectMonsterKind, IconNormal, MonsterRules, definitions, exceptions)
        {
            Triggers = definitions.Zip(events, (d, e) => new TriggerEffectProfile(d, e.Event, EffectRelation.Self, requirement, timing: TriggerTiming.When)).ToArray()
        };
    }

    /// <summary>"When this card is destroyed by battle and sent to the Graveyard".</summary>
    public AuthoredCard WhenBattleDestroyed(uint id, string name, EffectStep[] steps, TriggerRequirement requirement = TriggerRequirement.Mandatory, string exceptions = "none") =>
        Trigger(id, name, MonsterEventKind.SentToGraveyard, steps, requirement, TriggerSourceLocation.GraveyardSelf, TriggerEventCause.ByBattle, exceptions: exceptions);

    /// <summary>A face-up monster's Main Phase effect.</summary>
    public AuthoredCard Ignition(uint id, string name, EffectStep[] steps, EffectCost? cost = null, EffectTarget[]? targets = null, bool oncePerTurn = false, DuelCardRules? rules = null, string exceptions = "none") =>
        ActivatedMonster(id, name, EffectKind.MonsterIgnition, steps, cost, targets, oncePerTurn, rules, exceptions);

    /// <summary>A face-up monster's Spell Speed 2 effect.</summary>
    public AuthoredCard Quick(uint id, string name, EffectStep[] steps, EffectCost? cost = null, EffectTarget[]? targets = null, bool oncePerTurn = false, DuelCardRules? rules = null, string exceptions = "none") =>
        ActivatedMonster(id, name, EffectKind.MonsterQuick, steps, cost, targets, oncePerTurn, rules, exceptions);

    private AuthoredCard ActivatedMonster(uint id, string name, EffectKind kind, EffectStep[] steps, EffectCost? cost, EffectTarget[]? targets, bool oncePerTurn, DuelCardRules? rules, string exceptions) =>
        new(id, name, CardKind.Monster, EffectMonsterKind, IconNormal, rules ?? MonsterRules,
            [new EffectDefinition(Effect(id, "effect"), id, kind, steps, cost: cost, fieldTargets: targets, oncePerTurn: oncePerTurn)], exceptions);

    public AuthoredCard Field(uint id, string name, params LastingStatModifier[] modifiers) =>
        new(id, name, CardKind.Spell, SpellKind, IconField, SpellRules, [new EffectDefinition(Effect(id, "field"), id, EffectKind.FieldSpell, [], lastingModifiers: modifiers)],
            "A new Field Spell sends its controller's old one to the Graveyard; that card is not destroyed.");

    /// <summary>An Equip Spell for monsters matching <paramref name="only"/>, or any face-up monster.</summary>
    public AuthoredCard Equip(uint id, string name, int attack, int defense, CardFilter? only = null)
    {
        var target = only is null ? FaceUpMonster : new FieldTarget(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp, cardFilter: only);
        return new(id, name, CardKind.Spell, SpellKind, IconEquip, SpellRules,
            [new EffectDefinition(Effect(id, "equip"), id, EffectKind.EquipSpell, [new EquipTargetStep()], fieldTargets: [target],
                lastingModifiers: [new LastingStatModifier(TargetSide.Either, attack, defense, equippedOnly: true)])],
            "Destroyed when the equipped monster leaves the field or is flipped face-down.");
    }

    public static CardFamily Family(string familyId, string batchId, IReadOnlyList<string> tests, IReadOnlyList<AuthoredCard> cards)
    {
        var profiles = cards.Select(Profile).ToArray();
        // Trigger definitions travel inside their trigger profiles; the engine rejects them as standalone definitions.
        var definitions = cards.SelectMany(c => c.Definitions).Where(d => d.Kind != EffectKind.MonsterTrigger).ToArray();
        return new CardFamily(familyId, batchId, Array.AsReadOnly(profiles), cards.ToFrozenDictionary(c => c.Id, c => c.Rules),
            cards.ToFrozenDictionary(c => c.Id, c => new CardStaticMetadata(c.Kind, new(CardEffectState.Referenced, c.Definitions.Select(d => d.EffectId).ToArray()))),
            catalog => Validate(catalog, cards), tests)
        {
            Definitions = Array.AsReadOnly(definitions),
            Triggers = Array.AsReadOnly(cards.SelectMany(c => c.Triggers).ToArray())
        };
    }

    private static void Validate(CardCatalog catalog, IReadOnlyList<AuthoredCard> cards)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 is { } hash && hash != TfspCardMetadata.CardInfoSha256)
            throw new InvalidDataException("This family has not been reviewed against this source build.");
        var byId = catalog.Cards.ToDictionary(c => c.GameCardId);
        foreach (var card in cards)
            if (!byId.TryGetValue(card.Id, out var actual) || actual.Name != card.Name || actual.Kind != card.Kind ||
                actual.Effects is not { State: CardEffectState.Referenced } effects || !effects.References.SequenceEqual(card.Definitions.Select(d => d.EffectId)) ||
                actual.Codes is { } codes && (codes.GetterKind != card.GetterKind || codes.Icon != card.Icon))
                throw new InvalidDataException($"Card {card.Id} metadata differs from this family.");
    }

    private static DefinitionCardProfile Profile(AuthoredCard card)
    {
        var effect = card.Definitions[0];
        bool targets = effect.FieldTargets.Count != 0;
        string detail = effect.Kind == EffectKind.FieldSpell || effect.LastingModifiers.Count != 0 ? $" Lasting: {Describe(effect.LastingModifiers)}." : "";
        return new(card.Id, card.Name, card.Kind, effect,
            $"{effect.Kind}: {Describe(effect.Steps)}.{detail}",
            card.Triggers.Count != 0 ? string.Join("; ", card.Triggers.Select(t => $"{t.Requirement} {t.Timing} on {t.EventKind} from {t.SourceLocation}, cause {t.Cause}")) + "."
                : effect.Conditions.Count == 0 ? $"Default timing for {effect.Kind}." : $"Default timing for {effect.Kind}, with {Describe(effect.Conditions)}.",
            effect.Cost is null ? "none" : $"{Describe(effect.Cost)} (never refunded).",
            targets ? Describe(effect.FieldTargets) : "none",
            targets ? "Target at activation." : "none", "Default.",
            targets ? "Negation skips the effect. Targets that no longer qualify are skipped; ordinary cleanup." : "Negation skips the effect; ordinary cleanup.",
            card.Exceptions, [], []);
    }

    /// <summary>Plain text for a Core record, read from its public properties so new steps need no extra code.</summary>
    internal static string Describe(object? value) => value switch
    {
        null => "none",
        string text => text,
        Enum or ValueType => value.ToString()!,
        IEnumerable list => string.Join(", ", list.Cast<object?>().Select(Describe)),
        _ => DescribeRecord(value)
    };

    private static string DescribeRecord(object value)
    {
        var type = value.GetType();
        string name = type.Name.EndsWith("Step", StringComparison.Ordinal) ? type.Name[..^4] : type.Name;
        var parts = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => $"{p.Name}={Describe(p.GetValue(value))}").ToArray();
        return parts.Length == 0 ? name : $"{name}({string.Join("; ", parts)})";
    }
}
