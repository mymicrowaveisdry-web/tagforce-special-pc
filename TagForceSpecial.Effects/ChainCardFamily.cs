using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>One reviewed card of a definition-based batch: its definition and manifest contract.</summary>
public sealed record DefinitionCardProfile(uint CardId, string Name, CardKind Kind, EffectDefinition Definition,
    string Operation, string Conditions, string Costs, string Targets, string Choices, string Limits,
    string NegationAndCleanup, string Exceptions, IReadOnlyList<string> StaticEvidenceIds, IReadOnlyList<string> Gaps);

/// <summary>
/// Stage 10C-2 batch: five Spell/Trap cards as consumers of the shared definition pipeline (chain, Tokens, Deck
/// operations). Contracts are authored from the original-era catalog text; synthetic rule tests and decoded
/// metadata only, except the retained PSP projections named in the tests. No card-specific engine path exists.
/// </summary>
public static class ChainCardFamily
{
    public const string FamilyId = "shared-chain-definition";
    public const string BatchId = "tfsp-family-batch-10c-02";
    public const uint Scapegoat = 4818, MagicJammer = 4862, MysticalSpaceTyphoon = 4909, LightningVortex = 5217,
        EmergencyTeleport = 7747;

    /// <summary>Sheep Tokens in the zone order of the retained Scapegoat capture (lowest free zone first).</summary>
    public static IReadOnlyList<uint> SheepTokenIds { get; } = Array.AsReadOnly(new uint[] { 3905, 3903, 3902, 3904 });

    /// <summary>
    /// Emergency Teleport's reviewed targets for cardinfo 6cd21aa6: every main-deck monster (decoded getter kinds 1,
    /// 3, 16, 18, 24, 27) with decoded type code 21 and Level 1–3. Code 21 matched all 12 Psychic monsters checked
    /// by name and a seeded sample of the other 58 (2026-11-07). The two type-21 Xyz monsters (10172, 10466) have a
    /// Rank, not a Level, and are excluded. Reviewed 2026-11-07; the duel never decodes the type at runtime.
    /// </summary>
    public static IReadOnlyList<uint> LevelThreeOrLowerPsychicIds { get; } = Array.AsReadOnly(new uint[]
    {
        7714, 7715, 7716, 7717, 7718, 7891, 8024, 9167, 9494, 9495, 9497, 9498,
        9595, 9641, 9703, 9745, 10523, 10599, 11058, 11206, 11253, 11492, 11493, 11494
    });

    private static readonly string[] SharedStatic = ["stage10c2-shared-definition-pipeline-2026-11-07"];

    public static IReadOnlyList<DefinitionCardProfile> Cards { get; } = Array.AsReadOnly(new DefinitionCardProfile[]
    {
        new(MysticalSpaceTyphoon, "Mystical Space Typhoon", CardKind.Spell,
            new("mystical-space-typhoon-v1", MysticalSpaceTyphoon, EffectKind.QuickPlaySpell, [new DestroyTargetStep()],
                target: new SpellTrapOnFieldTarget()),
            "Quick-Play Spell: target 1 Spell/Trap Card on the field; destroy it.",
            "Own Main Phase from hand or set (not the turn it was set); set card in a chain or the opponent's phase-change window; Spell Speed 2.",
            "none", "1 Spell/Trap on the field other than itself (either side, Pendulum zones included), bound by instance and placement.",
            "Target at activation.", "Default.",
            "A target that left or was replaced is not retargeted (link resolves with no effect); ordinary cleanup.",
            "none", ["static-0x09909E40-typhoon-target-validation", "static-0x09988A90-typhoon-target-validation", .. SharedStatic],
            ["Battle Phase and summon-response windows are not supported yet."]),
        new(MagicJammer, "Magic Jammer", CardKind.Trap,
            new("magic-jammer-v1", MagicJammer, EffectKind.CounterTrap, [new NegateActivationStep(DestroySource: true)],
                conditions: [new RespondsToSpellActivationCondition()], cost: new DiscardCost()),
            "Counter Trap: when a Spell Card is activated, discard 1 card; negate the activation, and if you do, destroy it.",
            "Set card, not the turn it was set; only as a response whose preceding link is a Spell activation (a Pendulum scale activation counts); Spell Speed 3.",
            "Discard 1 card from the hand (paid at activation, never refunded).", "The preceding chain link.",
            "Discard at activation.", "Default.",
            "Negates the activation and destroys the Spell if it is still in place; the negated link resolves with no effect.",
            "none", ["static-0x0990DBA4-magic-jammer-mode-1", .. SharedStatic],
            ["Summon-response windows are not supported yet."]),
        new(LightningVortex, "Lightning Vortex", CardKind.Spell,
            new("lightning-vortex-v1", LightningVortex, EffectKind.NormalSpell, [new DestroyOpponentFaceUpMonstersStep()],
                cost: new DiscardCost()),
            "Normal Spell: discard 1 card; destroy all face-up monsters your opponent controls.",
            "Own Main Phase, open state, from hand or set; Spell Speed 1.",
            "Discard 1 card from the hand (paid at activation, never refunded).", "none (not targeting).",
            "Discard at activation.", "Default.",
            "Destroys face-up monsters at resolution (face-down ones stay); source destruction alone does not negate; ordinary cleanup.",
            "none", SharedStatic, []),
        new(Scapegoat, "Scapegoat", CardKind.Spell,
            new("scapegoat-v1", Scapegoat, EffectKind.QuickPlaySpell,
                [new SummonTokensStep(SheepTokenIds, BattlePosition.FaceUpDefense)],
                conditions: [new FreeMonsterZonesCondition(4), new NoSummonThisTurnCondition()],
                locksOtherSummonsThisTurn: true),
            "Quick-Play Spell: Special Summon 4 Sheep Tokens (Beast/EARTH/Level 1/ATK 0/DEF 0) in Defense Position. They cannot be Tributed for a Tribute Summon. You cannot Summon other monsters the turn you activate this card (but you can Normal Set).",
            "At least 4 free Monster Zones and no Summon by you this turn; Quick-Play timing as above; Spell Speed 2.",
            "none", "none", "none (lowest free zones, deliberate PC policy).",
            "After activation you cannot Normal, Tribute, Flip or Special Summon this turn (Sets remain legal), even if it is negated.",
            "All four Tokens or none at resolution; Tokens cease to exist when they leave the field; ordinary cleanup.",
            "Sheep Tokens cannot be Tributed for a Tribute Summon.", SharedStatic,
            ["Token zone order follows the lowest-free-zone policy (one PSP capture agrees); battle-phase windows are not supported yet."]),
        new(EmergencyTeleport, "Emergency Teleport", CardKind.Spell,
            new("emergency-teleport-v1", EmergencyTeleport, EffectKind.QuickPlaySpell,
                [new SpecialSummonStep(SpecialSummonSources.Hand | SpecialSummonSources.Deck, LevelThreeOrLowerPsychicIds, banishAtEndPhase: true)]),
            "Quick-Play Spell: Special Summon 1 Level 3 or lower Psychic-Type monster from your hand or Deck, but banish it during the End Phase of this turn.",
            "A reviewed eligible monster in hand or Deck with a free zone; Quick-Play timing as above; Spell Speed 2.",
            "none", "none (chosen at resolution, not targeted).",
            "At resolution: one eligible card from hand or Deck, a free zone and face-up Attack or Defense Position.",
            "Default.",
            "Shuffles the Deck after a Deck summon; banishes that placement when the End Phase of this turn starts, or expires if it already left the field.",
            "Eligible cards are the reviewed 24-card Level 1–3 Psychic list; most of them have their own unsupported effects.",
            SharedStatic, ["Summoned monsters only fight in duels where their own rules are supported."])
    });

    public static IReadOnlyList<EffectDefinition> Definitions { get; } =
        Array.AsReadOnly(Cards.Select(c => c.Definition).ToArray());

    /// <summary>The five cards' rules plus the Sheep Tokens (basic battle; never Tributed for a Tribute Summon).</summary>
    public static IReadOnlyDictionary<uint, DuelCardRules> Rules { get; } = Cards
        .Select(c => new KeyValuePair<uint, DuelCardRules>(c.CardId, new(c.Kind == CardKind.Trap ? CardCategory.Trap : CardCategory.Spell,
            AllowsSpellTrapSet: true)))
        .Concat(SheepTokenIds.Select(id => new KeyValuePair<uint, DuelCardRules>(id,
            new(CardCategory.Monster, AllowsBasicBattle: true, IsToken: true))))
        .ToFrozenDictionary();

    /// <summary>Reviewed static metadata for the five cards, overlaid in Effects (never baked into Cards).</summary>
    public static IReadOnlyDictionary<uint, CardStaticMetadata> Reviewed { get; } = Cards.ToFrozenDictionary(c => c.CardId,
        c => new CardStaticMetadata(c.Kind, new(CardEffectState.Referenced, [c.Definition.EffectId])));

    /// <summary>Fails closed for an unreviewed build or metadata that differs from the reviewed batch.</summary>
    public static void ValidateCatalog(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 is { } hash && hash != TfspCardMetadata.CardInfoSha256)
            throw new InvalidDataException("The Stage 10C-2 batch has not been reviewed against this source build.");
        var byId = catalog.Cards.ToDictionary(c => c.GameCardId);
        foreach (var card in Cards)
            if (!byId.TryGetValue(card.CardId, out var actual) || actual.Name != card.Name || actual.Kind != card.Kind ||
                actual.Effects is not { State: CardEffectState.Referenced } effects || !effects.References.SequenceEqual([card.Definition.EffectId]))
                throw new InvalidDataException($"Card {card.CardId} metadata differs from the reviewed batch.");
        foreach (uint id in SheepTokenIds)
            if (!byId.TryGetValue(id, out var token) || token.Codes is { } codes && codes.GetterKind != 10)
                throw new InvalidDataException($"Sheep Token {id} differs from the reviewed batch.");
        foreach (uint id in LevelThreeOrLowerPsychicIds)
            if (!byId.TryGetValue(id, out var monster) || monster.Statistics.Level is < 1 or > 3 ||
                monster.Codes is { } codes && codes.Type != 21)
                throw new InvalidDataException($"Emergency Teleport target {id} differs from the reviewed list.");
    }
}
