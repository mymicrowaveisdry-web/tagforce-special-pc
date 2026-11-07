using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Draw, Deck search, Deck-to-Graveyard and mill Spells/Traps as shared effect definitions.</summary>
public static class DeckOperationFamily
{
    public const string FamilyId = "deck-operation-definition";
    public const string BatchId = "tfsp-family-batch-10c-04a";

    /// <summary>Both artwork IDs of "Polymerization".</summary>
    public static IReadOnlyList<uint> PolymerizationIds { get; } = Array.AsReadOnly(new uint[] { 4837, 4867 });

    /// <summary>"Cat Girl" (3503) and "Stray Cat" (3500).</summary>
    public static IReadOnlyList<uint> CatIds { get; } = Array.AsReadOnly(new uint[] { 3500, 3503 });

    /// <summary>Field Spells: every Spell (getter kind 13) with icon 2.</summary>
    public static IReadOnlyList<uint> FieldSpellIds { get; } = Array.AsReadOnly(new uint[]
    {
        3506, 3514, 3523, 3599, 3712, 3713, 3720, 3723, 3767, 3768, 3788, 4336, 4337, 4338, 4339, 4340, 4341, 4899, 4932,
        4933, 4934, 4935, 4936, 4937, 5182, 5187, 5276, 5329, 5387, 5533, 5791, 5982, 6207, 6271, 6399, 6642, 6668, 6758,
        6759, 6775, 6823, 7003, 7004, 7079, 7129, 7164, 7307, 7457, 7624, 7626, 7627, 7683, 7857, 7904, 7916, 8045, 8135,
        8266, 8338, 8341, 8347, 8480, 8482, 8528, 8539, 8585, 8666, 8982, 8984, 9032, 9049, 9123, 9127, 9232, 9267, 9342,
        9350, 9456, 9520, 9715, 9871, 9934, 9958, 10191, 10194, 10214, 10279, 10282, 10327, 10455, 10549, 10664, 10669,
        10793, 10795, 10949, 10950, 10954, 10976, 11092, 11095, 11146, 11170, 11202, 11269, 11298, 11394, 11395, 11397,
        11578, 11586, 11607, 11610, 11625
    });

    /// <summary>The Normal Monsters (<see cref="TfspNormalMonsters"/>) at Level 5 or higher.</summary>
    public static IReadOnlyList<uint> HighLevelNormalMonsterIds { get; } = Array.AsReadOnly(new uint[]
    {
        3624, 3625, 3800, 3801, 3802, 3804, 3805, 3806, 3808, 3809, 3815, 3816, 3817, 3818, 3821, 3822, 3849, 3850, 3851,
        3853, 3854, 3862, 4007, 4017, 4020, 4028, 4039, 4041, 4044, 4045, 4048, 4058, 4074, 4079, 4085, 4088, 4091, 4093,
        4094, 4096, 4099, 4100, 4112, 4117, 4130, 4132, 4133, 4134, 4137, 4156, 4210, 4292, 4361, 4363, 4364, 4368, 4372,
        4381, 4382, 4384, 4388, 4389, 4394, 4396, 4397, 4402, 4406, 4407, 4409, 4410, 4411, 4414, 4419, 4425, 4429, 4430,
        4432, 4440, 4444, 4445, 4447, 4449, 4454, 4455, 4459, 4466, 4470, 4474, 4478, 4489, 4497, 4500, 4504, 4523, 4524,
        4526, 4528, 4531, 4532, 4534, 4535, 4548, 4561, 4563, 4577, 4581, 4583, 4584, 4588, 4599, 4620, 4625, 4627, 4628,
        4633, 4638, 4639, 4643, 4644, 4651, 4654, 4709, 4711, 4712, 4714, 4717, 4718, 4720, 4729, 4732, 4741, 4743, 4778,
        4787, 4789, 4790, 4971, 4972, 4974, 5029, 5038, 5066, 5092, 5147, 5165, 5203, 5229, 5349, 5559, 5642, 5814, 5871,
        5875, 5946, 5950, 6094, 6172, 6653, 6961, 6962, 7540, 9372, 9720, 10024, 10025, 10131, 11212
    });

    private const string SpellTiming = "Own Main Phase, open state, from hand or set; Spell Speed 1.";
    private const string TrapTiming = "Set card, not the turn it was set: own Main Phase, the opponent's phase-change window or a chain response; Spell Speed 2.";
    private const string Cleanup = "Both chain negation kinds skip the effect; source destruction alone does not; ordinary cleanup.";
    private static readonly string[] NoEvidence = [];

    private static DefinitionCardProfile Card(uint id, string name, CardKind kind, string operation, EffectStep[] steps,
        string requirement, string choices = "none", string exceptions = "none", string[]? gaps = null) =>
        new(id, name, kind, new($"deck-operation-{id}-v1", id, kind == CardKind.Trap ? EffectKind.NormalTrap : EffectKind.NormalSpell, steps),
            operation, $"{(kind == CardKind.Trap ? TrapTiming : SpellTiming)} {requirement}", "none", "none (not targeting).",
            choices, "Default.", Cleanup, exceptions, NoEvidence, gaps ?? []);

    private const string SearchChoice = "At resolution: 1 matching card in your Deck (revealed), then the Deck is shuffled.";

    public static IReadOnlyList<DefinitionCardProfile> Cards { get; } = Array.AsReadOnly(new[]
    {
        Card(4844, "Pot of Greed", CardKind.Spell, "Normal Spell: draw 2 cards.", [new DrawCardsStep(2)],
            "Activation needs 2 cards in your Deck."),
        Card(5210, "Jar of Greed", CardKind.Trap, "Normal Trap: draw 1 card.", [new DrawCardsStep(1)],
            "Activation needs 1 card in your Deck."),
        Card(4895, "Upstart Goblin", CardKind.Spell, "Normal Spell: draw 1 card, then your opponent gains 1000 LP.",
            [new DrawCardsStep(1), new GainLifePointsStep(1000, EffectPlayers.Opponent)], "Activation needs 1 card in your Deck."),
        Card(7234, "Gift Card", CardKind.Trap, "Normal Trap: your opponent gains 3000 LP.",
            [new GainLifePointsStep(3000, EffectPlayers.Opponent)], "No further requirement."),
        Card(5236, "Foolish Burial", CardKind.Spell, "Normal Spell: send 1 monster from your Deck to the Graveyard.",
            [new SendFromDeckToGraveyardStep(new AnyMonsterFilter())], "Activation needs a monster with explicit rules in your Deck.",
            "At resolution: 1 monster in your Deck, then the Deck is shuffled.",
            gaps: ["Deck cards without explicit card rules are never offered."]),
        Card(7769, "Needlebug Nest", CardKind.Trap, "Normal Trap: send the top 5 cards of your Deck to the Graveyard.",
            [new MillTopOfDeckStep(5)], "Activation needs 5 cards in your Deck.",
            gaps: ["Activation with fewer than 5 cards follows the original-era ruling reading; the PSP behaviour is unobserved."]),
        Card(4872, "Fusion Sage", CardKind.Spell, "Normal Spell: add 1 \"Polymerization\" from your Deck to your hand.",
            [new AddFromDeckToHandStep(new CardIdFilter(PolymerizationIds))], "Activation needs a \"Polymerization\" in your Deck.", SearchChoice),
        Card(7220, "Summoner's Art", CardKind.Spell, "Normal Spell: add 1 Level 5 or higher Normal Monster from your Deck to your hand.",
            [new AddFromDeckToHandStep(new CardIdFilter(HighLevelNormalMonsterIds))],
            "Activation needs a reviewed Level 5+ Normal Monster in your Deck.", SearchChoice),
        Card(3505, "Inviting Cat", CardKind.Spell, "Normal Spell: add 1 \"Cat Girl\" or \"Stray Cat\" from your Deck to your hand.",
            [new AddFromDeckToHandStep(new CardIdFilter(CatIds))], "Activation needs one of them in your Deck.", SearchChoice),
        Card(5537, "Terraforming", CardKind.Spell, "Normal Spell: add 1 Field Spell from your Deck to your hand.",
            [new AddFromDeckToHandStep(new CardIdFilter(FieldSpellIds))], "Activation needs a reviewed Field Spell (with explicit rules) in your Deck.",
            SearchChoice, gaps: ["Field Spells themselves are not yet playable (no Field Zone); they can only be searched."])
    });

    public static IReadOnlyList<EffectDefinition> Definitions { get; } = Array.AsReadOnly(Cards.Select(c => c.Definition).ToArray());

    public static IReadOnlyDictionary<uint, DuelCardRules> Rules { get; } = Cards.ToFrozenDictionary(c => c.CardId,
        c => new DuelCardRules(c.Kind == CardKind.Trap ? CardCategory.Trap : CardCategory.Spell, AllowsSpellTrapSet: true));

    public static IReadOnlyDictionary<uint, CardStaticMetadata> Reviewed { get; } = Cards.ToFrozenDictionary(c => c.CardId,
        c => new CardStaticMetadata(c.Kind, new(CardEffectState.Referenced, [c.Definition.EffectId])));

    /// <summary>Throws when the catalog's metadata or lists differ from this family.</summary>
    public static void ValidateCatalog(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 is { } hash && hash != TfspCardMetadata.CardInfoSha256)
            throw new InvalidDataException("The Stage 10C-4a batch has not been reviewed against this source build.");
        var byId = catalog.Cards.ToDictionary(c => c.GameCardId);
        foreach (var card in Cards)
            if (!byId.TryGetValue(card.CardId, out var actual) || actual.Name != card.Name || actual.Kind != card.Kind ||
                actual.Effects is not { State: CardEffectState.Referenced } effects || !effects.References.SequenceEqual([card.Definition.EffectId]) ||
                actual.Codes is { } codes && (codes.GetterKind != (card.Kind == CardKind.Trap ? 14u : 13u) || codes.Icon != 0))
                throw new InvalidDataException($"Card {card.CardId} metadata differs from the reviewed batch.");
        void Require(IEnumerable<uint> ids, Func<CardDefinition, bool> valid, string list)
        {
            foreach (uint id in ids)
                if (!byId.TryGetValue(id, out var c) || !valid(c))
                    throw new InvalidDataException($"{list} entry {id} differs from the reviewed list.");
        }
        Require(PolymerizationIds, c => c.Name == "Polymerization", "Polymerization");
        Require(CatIds, c => c.Name is "Cat Girl" or "Stray Cat", "Cat");
        Require(FieldSpellIds, c => c.Codes is not { } k || k.GetterKind == 13 && k.Icon == 2, "Field Spell");
        Require(HighLevelNormalMonsterIds, c => TfspNormalMonsters.Ids.Contains(c.GameCardId) && c.Statistics.Level >= 5, "Normal Monster");
    }
}
