using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>
/// Stage 10C-3 batch: Normal Spells and Normal Traps whose whole effect is an authored LP change: fixed amounts
/// (four corroborated by the signed LP table at historical 0x09c98ec4, the rest authored from the original-era
/// text), both-player effects and amounts counted from public zones at resolution. Synthetic rule tests and
/// decoded metadata only; no PSP observation is claimed.
/// </summary>
public static class LifePointFamily
{
    public const string FamilyId = "authored-life-point-effect";
    public const string BatchId = "tfsp-family-batch-10c-03";

    private static readonly string[] TableEvidence =
        ["stage10-toolkit-lp-table-2026-11-01", "stage10-toolkit-lp-family-2026-11-01", "stage10c3-lp-table-188-2026-11-07"];
    private static readonly string[] TextOnly = [];

    private const string FixedConditions = "Own Main Phase, open state, from hand or set; Spell Speed 1.";
    private const string TrapConditions = "Set card, not the turn it was set: own Main Phase, the opponent's phase-change window or a chain response; Spell Speed 2.";
    private const string Cleanup = "Both chain negation kinds skip the effect; source destruction alone does not; ordinary cleanup; zero LP ends the duel at once.";
    private const string CountedCleanup = "Counted at resolution (0 counted: no LP change). " + Cleanup;

    private static DefinitionCardProfile Spell(uint id, string name, string operation, EffectStep[] steps, string[] evidence,
        string conditions = FixedConditions, string negation = Cleanup, string exceptions = "none", string[]? gaps = null) =>
        new(id, name, CardKind.Spell, new($"life-point-{id}-v1", id, EffectKind.NormalSpell, steps), operation, conditions,
            "none", "none", "none", "Default.", negation, exceptions, evidence, gaps ?? []);

    private static DefinitionCardProfile Trap(uint id, string name, string operation, EffectStep[] steps, string counted) =>
        new(id, name, CardKind.Trap, new($"life-point-{id}-v1", id, EffectKind.NormalTrap, steps), operation,
            $"{TrapConditions} Activation needs at least 1 {counted}.", "none", "none (not targeting).", "none", "Default.",
            CountedCleanup, "none", TextOnly,
            ["Activation with nothing to count follows the original-era ruling reading; the PSP behaviour is unobserved."]);

    public static IReadOnlyList<DefinitionCardProfile> Cards { get; } = Array.AsReadOnly(new[]
    {
        Spell(4346, "Goblin's Secret Remedy", "Normal Spell: gain 600 LP.", [new GainLifePointsStep(600)], TableEvidence),
        Spell(4347, "Soul of the Pure", "Normal Spell: gain 800 LP.", [new GainLifePointsStep(800)], TableEvidence),
        Spell(4351, "Final Flame", "Normal Spell: inflict 600 damage to your opponent.", [new DealDamageStep(600)], TableEvidence),
        Spell(4841, "Raimei", "Normal Spell: inflict 300 damage to your opponent.", [new DealDamageStep(300)], TableEvidence),
        Spell(4353, "Tremendous Fire", "Normal Spell: inflict 1000 damage to your opponent and you take 500 damage (simultaneous).",
            [new DamageBothPlayersStep(1000, 500)], TextOnly, exceptions: "Both players at 0 LP is a draw.",
            gaps: ["The simultaneous-zero draw is the original-era rule; the PSP result screen is unobserved."]),
        Spell(5151, "Rain of Mercy", "Normal Spell: both players gain 1000 LP (activating player first).",
            [new GainLifePointsStep(1000, EffectPlayers.Both)], TextOnly),
        Spell(5985, "Goblin Thief", "Normal Spell: inflict 500 damage to your opponent, then gain 500 LP.",
            [new DealDamageStep(500), new GainLifePointsStep(500)], TextOnly,
            exceptions: "If the damage reduces the opponent to 0 LP, the duel ends before the gain."),
        Spell(4871, "Restructer Revolution", "Normal Spell: inflict 200 damage to your opponent for each card in their hand.",
            [new DealDamageStep(200, EffectCount.OpponentHand)], TextOnly,
            conditions: FixedConditions + " Activation needs at least 1 card in the opponent's hand.", negation: CountedCleanup,
            gaps: ["Activation with an empty opponent hand follows the original-era ruling reading; the PSP behaviour is unobserved."]),
        Trap(4865, "Just Desserts", "Normal Trap: inflict 500 damage to your opponent for each monster they control.",
            [new DealDamageStep(500, EffectCount.OpponentMonsters)], "monster the opponent controls"),
        Trap(4986, "Gift of the Mystical Elf", "Normal Trap: gain 300 LP for each monster on the field.",
            [new GainLifePointsStep(300, PerCount: EffectCount.AllMonsters)], "monster on the field"),
        Trap(5620, "Secret Barrel", "Normal Trap: inflict 200 damage to your opponent for each card in their hand and each card they control.",
            [new DealDamageStep(200, EffectCount.OpponentHandAndField)], "card in the opponent's hand or under their control"),
        Trap(6148, "Cemetary Bomb", "Normal Trap: inflict 100 damage to your opponent for each card in their Graveyard.",
            [new DealDamageStep(100, EffectCount.OpponentGraveyard)], "card in the opponent's Graveyard"),
        Trap(6283, "D.D. Dynamite", "Normal Trap: inflict 300 damage to your opponent for each of their banished cards.",
            [new DealDamageStep(300, EffectCount.OpponentBanished)], "banished card of the opponent")
    });

    /// <summary>Signed LP table values (static PRX data) for the table-backed cards, used to cross-check the authored amounts.</summary>
    public static IReadOnlyDictionary<uint, int> TableAmounts { get; } = new Dictionary<uint, int>
    {
        [4346] = 600, [4347] = 800, [4351] = -600, [4841] = -300
    }.ToFrozenDictionary();

    public static IReadOnlyList<EffectDefinition> Definitions { get; } = Array.AsReadOnly(Cards.Select(c => c.Definition).ToArray());

    public static IReadOnlyDictionary<uint, DuelCardRules> Rules { get; } = Cards.ToFrozenDictionary(c => c.CardId,
        c => new DuelCardRules(c.Kind == CardKind.Trap ? CardCategory.Trap : CardCategory.Spell, AllowsSpellTrapSet: true));

    public static IReadOnlyDictionary<uint, CardStaticMetadata> Reviewed { get; } = Cards.ToFrozenDictionary(c => c.CardId,
        c => new CardStaticMetadata(c.Kind, new(CardEffectState.Referenced, [c.Definition.EffectId])));

    /// <summary>
    /// Fails closed for an unreviewed build or differing metadata. Decoded kind codes (getter 13 Spell, 14 Trap; icon 0)
    /// must agree with the authored Normal kinds wherever codes are present.
    /// </summary>
    public static void ValidateCatalog(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 is { } hash && hash != TfspCardMetadata.CardInfoSha256)
            throw new InvalidDataException("The Stage 10C-3 batch has not been reviewed against this source build.");
        var byId = catalog.Cards.ToDictionary(c => c.GameCardId);
        foreach (var card in Cards)
            if (!byId.TryGetValue(card.CardId, out var actual) || actual.Name != card.Name || actual.Kind != card.Kind ||
                actual.Effects is not { State: CardEffectState.Referenced } effects || !effects.References.SequenceEqual([card.Definition.EffectId]) ||
                actual.Codes is { } codes && (codes.GetterKind != (card.Kind == CardKind.Trap ? 14u : 13u) || codes.Icon != 0))
                throw new InvalidDataException($"Card {card.CardId} metadata differs from the reviewed batch.");
    }
}
