using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Spells/Traps that revive, recover, banish or shuffle back Graveyard and banished cards.</summary>
public static class GraveyardFamily
{
    public const string FamilyId = "graveyard-definition";
    public const string BatchId = "tfsp-family-batch-10c-04c";

    private const string SpellTiming = "Own Main Phase, open state, from hand or set; Spell Speed 1.";
    private const string QuickTiming = "Own Main Phase from hand or set (not the turn it was set); set card in a chain or the opponent's phase-change window; Spell Speed 2.";
    private const string TrapTiming = "Set card, not the turn it was set: own Main Phase, the opponent's phase-change window or a chain response; Spell Speed 2.";
    private const string Cleanup = "Targets no longer in their zone are skipped; with none left, the rest of the effect does not resolve. Ordinary cleanup.";

    private static DefinitionCardProfile Card(uint id, string name, EffectKind kind, string operation, EffectStep[] steps,
        string targets, string requirement, ZoneTarget[]? zoneTargets = null, EffectCost? cost = null, string choices = "Targets at activation.",
        string costs = "none", string negation = Cleanup, string exceptions = "none") =>
        new(id, name, kind == EffectKind.NormalTrap ? CardKind.Trap : CardKind.Spell,
            new($"graveyard-{id}-v1", id, kind, steps, cost: cost, fieldTargets: zoneTargets), operation,
            $"{kind switch { EffectKind.QuickPlaySpell => QuickTiming, EffectKind.NormalTrap => TrapTiming, _ => SpellTiming }} {requirement}",
            costs, targets, choices, "Default.", negation, exceptions, [], []);

    public static IReadOnlyList<DefinitionCardProfile> Cards { get; } = Array.AsReadOnly(new[]
    {
        Card(4842, "Monster Reborn", EffectKind.NormalSpell, "Normal Spell: target 1 monster in either Graveyard; Special Summon it.",
            [new SpecialSummonTargetStep()], "1 monster in either Graveyard that could be Special Summoned to your field.",
            "Needs such a monster and a free zone.",
            [new(CardZone.Graveyard, TargetSide.Either, new AnyMonsterFilter(), mustBeSummonable: true)],
            choices: "Target at activation; zone and face-up position at resolution.",
            exceptions: "A monster from the opponent's Graveyard stays theirs and returns to their zones when it leaves the field."),
        Card(6213, "Monster Reincarnation", EffectKind.NormalSpell, "Normal Spell: discard 1 card, then target 1 monster in your Graveyard; add it to your hand.",
            [new AddTargetsToHandStep()], "1 monster in your Graveyard (the discarded card included).", "Needs a card to discard and a target.",
            [new(CardZone.Graveyard, TargetSide.Own, new AnyMonsterFilter())], new DiscardCost(),
            "Discard, then target, at activation.", "Discard 1 card (never refunded).",
            exceptions: "Activation needs a monster in your Graveyard before the discard."),
        Card(6129, "Dark Factory of Mass Production", EffectKind.NormalSpell, "Normal Spell: target 2 Normal Monsters in your Graveyard; add them to your hand.",
            [new AddTargetsToHandStep()], "2 reviewed Normal Monsters in your Graveyard.", "Needs two targets.",
            [new(CardZone.Graveyard, TargetSide.Own, new CardIdFilter(TfspNormalMonsters.Ids), 2, 2)]),
        Card(6511, "Pot of Avarice", EffectKind.NormalSpell, "Normal Spell: target 5 monsters in your Graveyard; shuffle all 5 into the Deck, then draw 2 cards.",
            [new ShuffleTargetsIntoDeckStep(StopUnlessAll: true), new DrawCardsStep(2)], "5 monsters in your Graveyard.", "Needs five targets.",
            [new(CardZone.Graveyard, TargetSide.Own, new AnyMonsterFilter(), 5, 5)],
            negation: "If any target is no longer in the Graveyard, the rest are shuffled in but no cards are drawn. Ordinary cleanup."),
        Card(7010, "The Transmigration Prophecy", EffectKind.NormalTrap, "Normal Trap: target 2 cards in the Graveyards; shuffle them into their owners' Decks.",
            [new ShuffleTargetsIntoDeckStep()], "2 cards in either or both Graveyards.", "Needs two targets.",
            [new(CardZone.Graveyard, TargetSide.Either, new AnyCardFilter(), 2, 2)]),
        Card(4857, "Soul Release", EffectKind.NormalSpell, "Normal Spell: target up to 5 cards in any Graveyards; banish them.",
            [new BanishTargetsStep()], "1 to 5 cards in either or both Graveyards.", "Needs at least one target.",
            [new(CardZone.Graveyard, TargetSide.Either, new AnyCardFilter(), 1, 5)], choices: "1–5 targets at activation (Done ends early)."),
        Card(6941, "Burial from a Different Dimension", EffectKind.QuickPlaySpell, "Quick-Play Spell: target up to 3 banished monsters; return them to the Graveyard.",
            [new ReturnTargetsToGraveyardStep()], "1 to 3 banished monsters of either player.", "Needs at least one target.",
            [new(CardZone.Banished, TargetSide.Either, new AnyMonsterFilter(), 1, 3)], choices: "1–3 targets at activation (Done ends early)."),
        Card(6995, "Ancient Rules", EffectKind.NormalSpell, "Normal Spell: Special Summon 1 Level 5 or higher Normal Monster from your hand.",
            [new SpecialSummonStep(SpecialSummonSources.Hand, DeckOperationFamily.HighLevelNormalMonsterIds)], "none (not targeting).",
            "Needs a reviewed Level 5+ Normal Monster in hand and a free zone.",
            choices: "At resolution: the monster, a free zone and face-up position.",
            negation: "No eligible card at resolution: no summon. Ordinary cleanup.")
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
