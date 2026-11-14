using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Destroy, return and position-change Spells/Traps by target, by "all matching" or by highest/lowest stat.</summary>
public static class RemovalFamily
{
    public const string FamilyId = "field-removal-definition";
    public const string BatchId = "tfsp-family-batch-10c-04b";

    private const string SpellTiming = "Own Main Phase, open state, from hand or set; Spell Speed 1.";
    private const string QuickTiming = "Own Main Phase from hand or set (not the turn it was set); set card in a chain or the opponent's phase-change window; Spell Speed 2.";
    private const string TrapTiming = "Set card, not the turn it was set: own Main Phase, the opponent's phase-change window or a chain response; Spell Speed 2.";
    private const string TargetCleanup = "Targets that left, were replaced or no longer qualify are skipped; with none left, the rest of the effect does not resolve. Ordinary cleanup.";
    private const string AllCleanup = "Affects matching cards at resolution, never the resolving card itself. Ordinary cleanup.";

    private static DefinitionCardProfile Card(uint id, string name, EffectKind kind, string operation, EffectStep[] steps,
        string targets, string requirement, string negation, EffectCost? cost = null, FieldTarget[]? fieldTargets = null,
        string choices = "none", string costs = "none") =>
        new(id, name, kind is EffectKind.NormalTrap or EffectKind.CounterTrap ? CardKind.Trap : CardKind.Spell,
            new($"removal-{id}-v1", id, kind, steps, cost: cost, fieldTargets: fieldTargets), operation,
            $"{kind switch { EffectKind.QuickPlaySpell => QuickTiming, EffectKind.NormalTrap => TrapTiming, _ => SpellTiming }} {requirement}",
            costs, targets, choices, "Default.", negation, "none", [], []);

    private static FieldTarget Monster(TargetSide side = TargetSide.Either, FieldCardFilter filter = FieldCardFilter.Any, int count = 1) =>
        new(FieldCardKind.Monster, side, filter, count);

    public static IReadOnlyList<DefinitionCardProfile> Cards { get; } = Array.AsReadOnly(new[]
    {
        Card(4342, "Dark Hole", EffectKind.NormalSpell, "Normal Spell: destroy all monsters on the field.",
            [new DestroyAllStep(FieldCardKind.Monster)], "none (not targeting).", "Needs a monster on the field.", AllCleanup),
        Card(4891, "Heavy Storm", EffectKind.NormalSpell, "Normal Spell: destroy all Spell and Trap Cards on the field.",
            [new DestroyAllStep(FieldCardKind.SpellTrap)], "none (not targeting).", "Needs another Spell/Trap on the field.", AllCleanup),
        Card(5544, "Raigeki Break", EffectKind.NormalTrap, "Normal Trap: discard 1 card, then target 1 card on the field; destroy it.",
            [new DestroyTargetsStep()], "1 card on the field other than itself.", "Needs a card to discard and a target.", TargetCleanup,
            new DiscardCost(), [new(FieldCardKind.Any)], "Discard, then target, at activation.", "Discard 1 card (never refunded)."),
        Card(6003, "Shield Crush", EffectKind.NormalSpell, "Normal Spell: target 1 Defense Position monster on the field; destroy it.",
            [new DestroyTargetsStep()], "1 Defense Position monster (face-up or face-down).", "Needs a target.", TargetCleanup,
            fieldTargets: [Monster(filter: FieldCardFilter.DefensePosition)], choices: "Target at activation."),
        Card(4856, "Tribute to The Doomed", EffectKind.NormalSpell, "Normal Spell: discard 1 card, then target 1 monster on the field; destroy it.",
            [new DestroyTargetsStep()], "1 monster on the field.", "Needs a card to discard and a target.", TargetCleanup,
            new DiscardCost(), [Monster()], "Discard, then target, at activation.", "Discard 1 card (never refunded)."),
        Card(6999, "Twister", EffectKind.QuickPlaySpell, "Quick-Play Spell: pay 500 LP, then target 1 face-up Spell/Trap on the field; destroy it.",
            [new DestroyTargetsStep()], "1 face-up Spell/Trap other than itself.", "Needs more than 500 LP and a target.", TargetCleanup,
            new PayLifePointsCost(500), [new(FieldCardKind.SpellTrap, TargetSide.Either, FieldCardFilter.FaceUp)],
            "Target at activation.", "Pay 500 LP (never refunded)."),
        Card(5743, "Soul Taker", EffectKind.NormalSpell, "Normal Spell: target 1 face-up monster your opponent controls; destroy it, then your opponent gains 1000 LP.",
            [new DestroyTargetsStep(), new GainLifePointsStep(1000, EffectPlayers.Opponent)], "1 face-up monster the opponent controls.",
            "Needs a target.", TargetCleanup, fieldTargets: [Monster(TargetSide.Opponent, FieldCardFilter.FaceUp)], choices: "Target at activation."),
        Card(4835, "Fissure", EffectKind.NormalSpell, "Normal Spell: destroy the 1 face-up monster your opponent controls with the lowest ATK.",
            [new DestroyExtremeMonsterStep(TargetSide.Opponent, MonsterStat.Attack, StatExtreme.Lowest)], "none (not targeting).",
            "Needs a face-up opponent monster.", AllCleanup, choices: "At resolution, choose among tied monsters."),
        Card(5905, "Smashing Ground", EffectKind.NormalSpell, "Normal Spell: destroy the 1 face-up monster your opponent controls with the highest DEF.",
            [new DestroyExtremeMonsterStep(TargetSide.Opponent, MonsterStat.Defense, StatExtreme.Highest)], "none (not targeting).",
            "Needs a face-up opponent monster.", AllCleanup, choices: "At resolution, choose among tied monsters."),
        Card(6130, "Hammer Shot", EffectKind.NormalSpell, "Normal Spell: destroy 1 face-up Attack Position monster with the highest ATK.",
            [new DestroyExtremeMonsterStep(TargetSide.Either, MonsterStat.Attack, StatExtreme.Highest, FieldCardFilter.FaceUpAttack)],
            "none (not targeting).", "Needs a face-up Attack Position monster on the field.", AllCleanup,
            choices: "At resolution, choose among tied monsters."),
        Card(5914, "Compulsory Evacuation Device", EffectKind.NormalTrap, "Normal Trap: target 1 monster on the field; return it to the hand.",
            [new ReturnTargetsToHandStep()], "1 monster on the field.", "Needs a target.",
            TargetCleanup + " Extra Deck monsters return to the Extra Deck; Tokens cease to exist.", fieldTargets: [Monster()],
            choices: "Target at activation."),
        Card(4910, "Giant Trunade", EffectKind.NormalSpell, "Normal Spell: return all Spell and Trap Cards on the field to the hand.",
            [new ReturnAllToHandStep(FieldCardKind.SpellTrap)], "none (not targeting).", "Needs another Spell/Trap on the field.", AllCleanup),
        Card(4874, "Block Attack", EffectKind.NormalSpell, "Normal Spell: target 1 Attack Position monster your opponent controls; change it to face-up Defense Position.",
            [new ChangeTargetsToDefenseStep()], "1 Attack Position monster the opponent controls.", "Needs a target.", TargetCleanup,
            fieldTargets: [Monster(TargetSide.Opponent, FieldCardFilter.AttackPosition)], choices: "Target at activation."),
        Card(6746, "No Entry!!", EffectKind.NormalTrap, "Normal Trap: change all Attack Position monsters to Defense Position.",
            [new ChangeAllToDefenseStep()], "none (not targeting).", "Needs an Attack Position monster on the field.", AllCleanup),
        Card(4839, "Two-Pronged Attack", EffectKind.NormalTrap, "Normal Trap: target 2 of your monsters and 1 of your opponent's monsters; destroy them.",
            [new DestroyTargetsStep()], "2 of your monsters, then 1 of your opponent's.", "Needs all three targets.", TargetCleanup,
            fieldTargets: [Monster(TargetSide.Own, count: 2), Monster(TargetSide.Opponent)], choices: "Three targets at activation.")
    });

    public static IReadOnlyList<EffectDefinition> Definitions { get; } = Array.AsReadOnly(Cards.Select(c => c.Definition).ToArray());

    public static IReadOnlyDictionary<uint, DuelCardRules> Rules { get; } = Cards.ToFrozenDictionary(c => c.CardId,
        c => new DuelCardRules(c.Kind == CardKind.Trap ? CardCategory.Trap : CardCategory.Spell, AllowsSpellTrapSet: true));

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
            uint icon = card.Definition.Kind == EffectKind.QuickPlaySpell ? 5u : 0u;
            if (!byId.TryGetValue(card.CardId, out var actual) || actual.Name != card.Name || actual.Kind != card.Kind ||
                actual.Effects is not { State: CardEffectState.Referenced } effects || !effects.References.SequenceEqual([card.Definition.EffectId]) ||
                actual.Codes is { } codes && (codes.GetterKind != (card.Kind == CardKind.Trap ? 14u : 13u) || codes.Icon != icon))
                throw new InvalidDataException($"Card {card.CardId} metadata differs from this family.");
        }
    }
}
