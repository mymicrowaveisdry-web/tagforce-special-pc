using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>One reviewed group of cards and everything an engine, the manifest and the metadata overlay need from it.</summary>
public sealed record CardFamily(string FamilyId, string BatchId, IReadOnlyList<DefinitionCardProfile> Cards,
    IReadOnlyDictionary<uint, DuelCardRules> Rules, IReadOnlyDictionary<uint, CardStaticMetadata> Reviewed,
    Action<CardCatalog> ValidateCatalog, IReadOnlyList<string> Tests)
{
    /// <summary>Catalog IDs this family reads besides its own cards; it is only claimed when all of them exist.</summary>
    public IReadOnlyList<uint> RequiredCardIds { get; init; } = [];
    public Func<DefinitionCardProfile, string> EvidenceClass { get; init; } = _ => "decoded-metadata; synthetic-rule-test";
    public IReadOnlyList<EffectDefinition> Definitions { get; init; } = [];
    public IReadOnlyList<TriggerEffectProfile> Triggers { get; init; } = [];
    public IReadOnlyList<ContinuousEffectProfile> ContinuousEffects { get; init; } = [];
    public IReadOnlyList<SpecialSummonCardProfile> SpecialSummonCards { get; init; } = [];
    public IReadOnlyList<SpecialSummonProcedureProfile> Procedures { get; init; } = [];
    public IReadOnlyList<ExtraDeckMaterialProcedureProfile> MaterialProcedures { get; init; } = [];
    public IReadOnlyList<SummonEffectProfile> SummonEffects { get; init; } = [];
    public IReadOnlyList<PendulumCardProfile> PendulumCards { get; init; } = [];
    public IReadOnlyList<RuleLockProfile> RuleLocks { get; init; } = [];
    public IReadOnlyList<BattleAbilityProfile> BattleAbilities { get; init; } = [];
    public IReadOnlyList<DestructionReplacementProfile> DestructionReplacements { get; init; } = [];
}

/// <summary>Every effect family. Adding a family here adds it to the overlay, the manifest and the full card pool.</summary>
public static class CardFamilies
{
    public static IReadOnlyList<CardFamily> All { get; } = Array.AsReadOnly(new[]
    {
        new CardFamily(ChainCardFamily.FamilyId, ChainCardFamily.BatchId, ChainCardFamily.Cards, ChainCardFamily.Rules, ChainCardFamily.Reviewed, ChainCardFamily.ValidateCatalog,
            ["ChainCardFamilyTests", "ChainInteractionTests", "DuelChainTests", "EmergencyTeleportTests", "EmergencyTeleportLifecycleTests", "ChainCardFamilyLocalReferenceTests"])
        {
            RequiredCardIds = [.. ChainCardFamily.SheepTokenIds, .. ChainCardFamily.LevelThreeOrLowerPsychicIds],
            EvidenceClass = _ => "decoded-metadata; synthetic-rule-test; retained-psp-projection",
            Definitions = ChainCardFamily.Definitions
        },
        new CardFamily(LifePointFamily.FamilyId, LifePointFamily.BatchId, LifePointFamily.Cards, LifePointFamily.Rules, LifePointFamily.Reviewed, LifePointFamily.ValidateCatalog,
            ["LifePointFamilyTests", "LifePointFamilyLocalReferenceTests", "NormalSpellTests"])
        {
            EvidenceClass = c => c.StaticEvidenceIds.Count != 0 ? "decoded-metadata; static-psp-interpretation; synthetic-rule-test" : "decoded-metadata; synthetic-rule-test",
            Definitions = LifePointFamily.Definitions
        },
        new CardFamily(DeckOperationFamily.FamilyId, DeckOperationFamily.BatchId, DeckOperationFamily.Cards, DeckOperationFamily.Rules, DeckOperationFamily.Reviewed, DeckOperationFamily.ValidateCatalog,
            ["DeckOperationFamilyTests", "DeckOperationFamilyLocalReferenceTests"])
        {
            RequiredCardIds = [.. DeckOperationFamily.PolymerizationIds, .. DeckOperationFamily.CatIds, .. DeckOperationFamily.FieldSpellIds, .. DeckOperationFamily.HighLevelNormalMonsterIds],
            Definitions = DeckOperationFamily.Definitions
        },
        new CardFamily(RemovalFamily.FamilyId, RemovalFamily.BatchId, RemovalFamily.Cards, RemovalFamily.Rules, RemovalFamily.Reviewed, RemovalFamily.ValidateCatalog,
            ["RemovalFamilyTests", "RemovalFamilyLocalReferenceTests"]) { Definitions = RemovalFamily.Definitions },
        new CardFamily(GraveyardFamily.FamilyId, GraveyardFamily.BatchId, GraveyardFamily.Cards, GraveyardFamily.Rules, GraveyardFamily.Reviewed, GraveyardFamily.ValidateCatalog,
            ["GraveyardFamilyTests", "GraveyardFamilyLocalReferenceTests"])
        {
            RequiredCardIds = DeckOperationFamily.HighLevelNormalMonsterIds,
            Definitions = GraveyardFamily.Definitions
        },
        new CardFamily(BattleTrapFamily.FamilyId, BattleTrapFamily.BatchId, BattleTrapFamily.Cards, BattleTrapFamily.Rules, BattleTrapFamily.Reviewed, BattleTrapFamily.ValidateCatalog,
            ["BattleTrapFamilyTests", "BattleTrapFamilyLocalReferenceTests"]) { Definitions = BattleTrapFamily.Definitions },
        new CardFamily(SummonTrapFamily.FamilyId, SummonTrapFamily.BatchId, SummonTrapFamily.Cards, SummonTrapFamily.Rules, SummonTrapFamily.Reviewed, SummonTrapFamily.ValidateCatalog,
            ["SummonTrapFamilyTests", "SummonTrapFamilyLocalReferenceTests"]) { Definitions = SummonTrapFamily.Definitions },
        new CardFamily(SummonNegationFamily.FamilyId, SummonNegationFamily.BatchId, SummonNegationFamily.Cards, SummonNegationFamily.Rules, SummonNegationFamily.Reviewed, SummonNegationFamily.ValidateCatalog,
            ["SummonNegationFamilyTests", "SummonNegationFamilyLocalReferenceTests"]) { Definitions = SummonNegationFamily.Definitions },
        new CardFamily(MonsterTriggerFamily.FamilyId, MonsterTriggerFamily.BatchId, MonsterTriggerFamily.Cards, MonsterTriggerFamily.Rules, MonsterTriggerFamily.Reviewed, MonsterTriggerFamily.ValidateCatalog,
            ["MonsterTriggerFamilyTests", "MonsterTriggerFamilyLocalReferenceTests"]) { Triggers = MonsterTriggerFamily.Triggers },
        new CardFamily(MonsterSubtypeFamily.FamilyId, MonsterSubtypeFamily.BatchId, MonsterSubtypeFamily.Cards, MonsterSubtypeFamily.CardRules, MonsterSubtypeFamily.Reviewed, MonsterSubtypeFamily.ValidateCatalog,
            ["MonsterSubtypeFamilyTests", "MonsterSubtypeFamilyLocalReferenceTests"])
        {
            RequiredCardIds = MonsterSubtypeFamily.SpiritIds,
            Triggers = MonsterSubtypeFamily.Triggers
        },
        new CardFamily(LastingCardFamily.FamilyId, LastingCardFamily.BatchId, LastingCardFamily.Cards, LastingCardFamily.CardRules, LastingCardFamily.Reviewed, LastingCardFamily.ValidateCatalog,
            ["LastingCardFamilyTests", "LastingSpellTrapTests", "LastingCardFamilyLocalReferenceTests"])
        {
            Definitions = LastingCardFamily.Definitions,
            Triggers = LastingCardFamily.Triggers,
            SpecialSummonCards = LastingCardFamily.SpecialSummonCards
        },
        new CardFamily(SummonProcedureFamily.FamilyId, SummonProcedureFamily.BatchId, SummonProcedureFamily.Cards, SummonProcedureFamily.CardRules, SummonProcedureFamily.Reviewed, SummonProcedureFamily.ValidateCatalog,
            ["SummonProcedureFamilyTests", "SummonProcedureFamilyLocalReferenceTests"])
        {
            Definitions = SummonProcedureFamily.Definitions,
            Triggers = SummonProcedureFamily.Triggers,
            ContinuousEffects = SummonProcedureFamily.ContinuousEffects,
            SpecialSummonCards = SummonProcedureFamily.SpecialSummonCards,
            Procedures = SummonProcedureFamily.Procedures,
            MaterialProcedures = SummonProcedureFamily.MaterialProcedures,
            SummonEffects = SummonProcedureFamily.SummonEffects,
            PendulumCards = SummonProcedureFamily.PendulumCards
        },
        TemplateCardFamily.Family
    });
}

/// <summary>All supported cards loaded into one engine, which is how real duels mix families.</summary>
public static class CardPool
{
    public static IReadOnlyDictionary<uint, DuelCardRules> Rules { get; } = BuildRules();

    private static Dictionary<uint, DuelCardRules> BuildRules()
    {
        var rules = new Dictionary<uint, DuelCardRules>();
        foreach (var (id, rule) in CardFamilies.All.SelectMany(f => f.Rules))
            if (!rules.TryAdd(id, rule) && rules[id] != rule)
                throw new InvalidOperationException($"Card {id} has conflicting rules in two families.");
        // Earlier pools repeat some family cards; the family's wider contract wins.
        foreach (var (id, rule) in NormalMonsterFamily.Rules.Concat(PlayablePreview.Rules)) rules.TryAdd(id, rule);
        return rules;
    }

    /// <summary>Decks are top-first. The seed drives every in-duel shuffle.</summary>
    public static DuelEngine CreateEngine(CardCatalog catalog, IEnumerable<uint> first, IEnumerable<uint> second,
        IEnumerable<uint>? firstExtra, IEnumerable<uint>? secondExtra, ulong shuffleSeed, PlayerId startingPlayer = PlayerId.One)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var families = CardFamilies.All;
        var covered = families.SelectMany(f => f.Definitions).Select(d => d.CardId).ToHashSet();
        var definitions = families.SelectMany(f => f.Definitions).Concat(PlayablePreview.Definitions.Where(d => !covered.Contains(d.CardId)));
        return new(catalog, Rules, first, second, startingPlayer,
            experimentalTriggerEffects: families.SelectMany(f => f.Triggers),
            experimentalContinuousEffects: families.SelectMany(f => f.ContinuousEffects),
            experimentalBattleFlow: true,
            experimentalSpecialSummonCards: families.SelectMany(f => f.SpecialSummonCards),
            experimentalSpecialSummonProcedures: families.SelectMany(f => f.Procedures),
            playerOneExtraDeck: firstExtra, playerTwoExtraDeck: secondExtra,
            experimentalMaterialProcedures: families.SelectMany(f => f.MaterialProcedures),
            experimentalSummonEffects: families.SelectMany(f => f.SummonEffects),
            experimentalPendulumCards: families.SelectMany(f => f.PendulumCards),
            effectDefinitions: definitions, shuffleSeed: shuffleSeed, ruleLocks: families.SelectMany(f => f.RuleLocks),
            battleAbilities: families.SelectMany(f => f.BattleAbilities),
            destructionReplacements: families.SelectMany(f => f.DestructionReplacements));
    }
}
