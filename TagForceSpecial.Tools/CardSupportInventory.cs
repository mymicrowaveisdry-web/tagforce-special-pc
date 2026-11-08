using System.Text.Json;
using TagForceSpecial.Assets;
using TagForceSpecial.Cards;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tools;

/// <summary>Private per-source manifests; imported card descriptions are never bundled in the application.</summary>
public static class CardSupportInventory
{
    public static object Build(CardCatalog catalog, int propertyRows)
    {
        PlayablePreview.ValidateCatalog(catalog);
        if (propertyRows != catalog.Count + 1 || catalog.Cards.Any(c => c.SourceRow is null) ||
            !catalog.Cards.Select(c => c.SourceRow!.InternalIndex).Order().SequenceEqual(Enumerable.Range(1, catalog.Count)))
            throw new InvalidDataException("Inventory requires one zero sentinel and a complete unique source-row mapping.");
        var preview = PlayablePreview.Cards.ToDictionary(c => c.CardId);
        var normalMonsters = NormalMonsterFamily.CardIds.ToHashSet();
        var tokens = TfspTokens.Ids.ToHashSet();
        // Family support is claimed only for a catalog holding the whole reviewed family (never a partial subset).
        var catalogIds = catalog.Cards.Select(c => c.GameCardId).ToHashSet();
        bool fullFamilyCatalog = normalMonsters.IsSubsetOf(catalogIds);
        if (fullFamilyCatalog) NormalMonsterFamily.ValidateCatalog(catalog);
        // A family counts as supported only when the catalog holds all its cards and listed IDs.
        var chainCards = ChainCardFamily.Cards.ToDictionary(c => c.CardId);
        bool fullChainCatalog = chainCards.Keys.Concat(ChainCardFamily.SheepTokenIds)
            .Concat(ChainCardFamily.LevelThreeOrLowerPsychicIds).All(catalogIds.Contains);
        if (fullChainCatalog) ChainCardFamily.ValidateCatalog(catalog);
        var lifePointCards = LifePointFamily.Cards.ToDictionary(c => c.CardId);
        bool fullLifePointCatalog = lifePointCards.Keys.All(catalogIds.Contains);
        if (fullLifePointCatalog) LifePointFamily.ValidateCatalog(catalog);
        var deckCards = DeckOperationFamily.Cards.ToDictionary(c => c.CardId);
        bool fullDeckCatalog = deckCards.Keys.Concat(DeckOperationFamily.PolymerizationIds).Concat(DeckOperationFamily.CatIds)
            .Concat(DeckOperationFamily.FieldSpellIds).Concat(DeckOperationFamily.HighLevelNormalMonsterIds).All(catalogIds.Contains);
        if (fullDeckCatalog) DeckOperationFamily.ValidateCatalog(catalog);
        var removalCards = RemovalFamily.Cards.ToDictionary(c => c.CardId);
        bool fullRemovalCatalog = removalCards.Keys.All(catalogIds.Contains);
        if (fullRemovalCatalog) RemovalFamily.ValidateCatalog(catalog);
        var graveCards = GraveyardFamily.Cards.ToDictionary(c => c.CardId);
        bool fullGraveCatalog = graveCards.Keys.Concat(DeckOperationFamily.HighLevelNormalMonsterIds).All(catalogIds.Contains);
        if (fullGraveCatalog) GraveyardFamily.ValidateCatalog(catalog);
        var battleCards = BattleTrapFamily.Cards.ToDictionary(c => c.CardId);
        bool fullBattleCatalog = battleCards.Keys.All(catalogIds.Contains);
        if (fullBattleCatalog) BattleTrapFamily.ValidateCatalog(catalog);
        var summonCards = SummonTrapFamily.Cards.ToDictionary(c => c.CardId);
        bool fullSummonCatalog = summonCards.Keys.All(catalogIds.Contains);
        if (fullSummonCatalog) SummonTrapFamily.ValidateCatalog(catalog);
        var negationCards = SummonNegationFamily.Cards.ToDictionary(c => c.CardId);
        bool fullNegationCatalog = negationCards.Keys.All(catalogIds.Contains);
        if (fullNegationCatalog) SummonNegationFamily.ValidateCatalog(catalog);
        string[] chainTests = ["ChainCardFamilyTests", "ChainInteractionTests", "DuelChainTests", "EmergencyTeleportTests",
            "EmergencyTeleportLifecycleTests", "ChainCardFamilyLocalReferenceTests"];
        string[] negationTests = ["SummonNegationFamilyTests", "SummonNegationFamilyLocalReferenceTests"];
        string[] summonTests = ["SummonTrapFamilyTests", "SummonTrapFamilyLocalReferenceTests"];
        string[] battleTests = ["BattleTrapFamilyTests", "BattleTrapFamilyLocalReferenceTests"];
        string[] graveTests = ["GraveyardFamilyTests", "GraveyardFamilyLocalReferenceTests"];
        string[] removalTests = ["RemovalFamilyTests", "RemovalFamilyLocalReferenceTests"];
        string[] deckTests = ["DeckOperationFamilyTests", "DeckOperationFamilyLocalReferenceTests"];
        string[] lifePointTests = ["LifePointFamilyTests", "LifePointFamilyLocalReferenceTests", "NormalSpellTests"];
        // Preview manifest entries keep the preview contract; the release manifest uses the wider family contracts.
        object Entry(CardDefinition c) =>
            fullFamilyCatalog && normalMonsters.Contains(c.GameCardId) ? FamilyEntry(c)
            : fullChainCatalog && chainCards.TryGetValue(c.GameCardId, out var chain)
                ? DefinitionEntry(c, chain, ChainCardFamily.FamilyId, ChainCardFamily.BatchId, chainTests,
                    "decoded-metadata; synthetic-rule-test; retained-psp-projection")
            : fullLifePointCatalog && lifePointCards.TryGetValue(c.GameCardId, out var life)
                ? DefinitionEntry(c, life, LifePointFamily.FamilyId, LifePointFamily.BatchId, lifePointTests,
                    life.StaticEvidenceIds.Count != 0 ? "decoded-metadata; static-psp-interpretation; synthetic-rule-test" : "decoded-metadata; synthetic-rule-test")
            : fullDeckCatalog && deckCards.TryGetValue(c.GameCardId, out var deck)
                ? DefinitionEntry(c, deck, DeckOperationFamily.FamilyId, DeckOperationFamily.BatchId, deckTests, "decoded-metadata; synthetic-rule-test")
            : fullRemovalCatalog && removalCards.TryGetValue(c.GameCardId, out var removal)
                ? DefinitionEntry(c, removal, RemovalFamily.FamilyId, RemovalFamily.BatchId, removalTests, "decoded-metadata; synthetic-rule-test")
            : fullGraveCatalog && graveCards.TryGetValue(c.GameCardId, out var grave)
                ? DefinitionEntry(c, grave, GraveyardFamily.FamilyId, GraveyardFamily.BatchId, graveTests, "decoded-metadata; synthetic-rule-test")
            : fullBattleCatalog && battleCards.TryGetValue(c.GameCardId, out var battle)
                ? DefinitionEntry(c, battle, BattleTrapFamily.FamilyId, BattleTrapFamily.BatchId, battleTests, "decoded-metadata; synthetic-rule-test")
            : fullSummonCatalog && summonCards.TryGetValue(c.GameCardId, out var summon)
                ? DefinitionEntry(c, summon, SummonTrapFamily.FamilyId, SummonTrapFamily.BatchId, summonTests, "decoded-metadata; synthetic-rule-test")
            : fullNegationCatalog && negationCards.TryGetValue(c.GameCardId, out var negator)
                ? DefinitionEntry(c, negator, SummonNegationFamily.FamilyId, SummonNegationFamily.BatchId, negationTests, "decoded-metadata; synthetic-rule-test")
            : PreviewEntry(c);
        object DefinitionEntry(CardDefinition c, DefinitionCardProfile p, string family, string batch, string[] tests, string evidenceClass) => new
        {
            c.GameCardId, c.Name, c.Source, c.SourceRow, c.Statistics,
            originalEraText = c.Description,
            rulingBasis = "Imported local source text; default original-era chain and timing rules. No later errata inferred.",
            behaviorFamily = family,
            status = "supported",
            scope = batch,
            operation = p.Operation,
            conditions = p.Conditions,
            costs = p.Costs,
            targets = p.Targets,
            choices = p.Choices,
            limits = p.Limits,
            exceptions = p.Exceptions,
            negationAndCleanup = p.NegationAndCleanup,
            tests,
            pspVerified = false,
            pspEvidenceIds = Array.Empty<string>(),
            evidenceClass,
            staticEvidenceIds = p.StaticEvidenceIds,
            missingEvidence = "Independent original-game runtime observations for each interaction scope.",
            gaps = p.Gaps
        };
        object FamilyEntry(CardDefinition c) => new
        {
            c.GameCardId, c.Name, c.Source, c.SourceRow, c.Statistics,
            originalEraText = c.Description,
            rulingBasis = "Imported local source text; default original-era basic duel rules. No later errata inferred.",
            behaviorFamily = NormalMonsterFamily.FamilyId,
            status = "supported",
            scope = NormalMonsterFamily.BatchId,
            operation = NormalMonsterFamily.Contract,
            conditions = c.Statistics.Level switch
            {
                <= 4 => "Ordinary Level 1–4 Normal Summon/Set.",
                <= 6 => "Level 5–6: Tribute Summon/Set with exactly one eligible Tribute.",
                _ => "Level 7–8: Tribute Summon/Set with exactly two eligible Tributes."
            },
            costs = c.Statistics.Level <= 4 ? "none" : "Tributes for the summon procedure only.",
            targets = "none",
            choices = "Ordinary placement, position, Tribute selection and battle target commands only.",
            limits = "Default turn, zone and summon/attack limits; no card-specific limits.",
            exceptions = NormalMonsterFamily.Exception(c.GameCardId) ?? "none",
            negationAndCleanup = "Ordinary battle destruction and Tribute departure; no effect to negate.",
            tests = new[] { "NormalMonsterFamilyTests", "NormalMonsterFamilyLocalReferenceTests", "TributeSummonTests", "DuelBattleTests" },
            pspVerified = false,
            pspEvidenceIds = Array.Empty<string>(),
            evidenceClass = "decoded-metadata; synthetic-rule-test",
            staticEvidenceIds = new[] { "stage10-native-inventory-2026-11-01:no-handler-binding" },
            missingEvidence = "Independent original-game runtime observations for this card and interaction scope.",
            gaps = Array.Empty<string>()
        };
        object PreviewEntry(CardDefinition c)
        {
            preview.TryGetValue(c.GameCardId, out var profile);
            return new
            {
                c.GameCardId, c.Name, c.Source, c.SourceRow, c.Statistics,
                originalEraText = c.Description,
                rulingBasis = "Imported local source text; default original-era basic duel rules. No later errata inferred.",
                behaviorFamily = profile?.Family ?? "unclassified",
                status = profile is not null ? "supported" : "unsupported",
                scope = PlayablePreview.ManifestId,
                operation = profile?.Contract,
                conditions = profile?.Spell is not null ? "Own Main Phase, open state, valid source and back-row slot." : profile is not null ? "Ordinary level 1–4 monster procedures." : null,
                costs = profile is not null ? "none" : "unknown",
                targets = profile is not null ? "none" : "unknown",
                choices = profile is not null ? "Ordinary placement/position and battle target commands only." : "unknown",
                limits = profile is not null ? "Default turn, zone and summon/attack limits; no card-specific limits." : "unknown",
                exceptions = profile is not null ? "No extra card-specific branches in this pool." : "not inventoried",
                negationAndCleanup = profile?.Spell is not null ? "Both chain negation kinds skip effect; source destruction alone does not; cleanup once, except immediate terminal defeat." : profile is not null ? "Ordinary battle destruction; no effect to negate." : "unknown",
                tests = profile is not null ? new[] { "PlayablePreviewTests", "NormalSpellTests", "DuelBattleTests" } : [],
                pspVerified = false,
                pspEvidenceIds = Array.Empty<string>(),
                evidenceClass = profile is not null ? "decoded-metadata; synthetic-rule-test" : "decoded-metadata",
                staticEvidenceIds = profile?.Spell is not null ? new[] { "stage10-toolkit-lp-table-2026-11-01", "stage10-toolkit-handler-2026-11-01" } : [],
                missingEvidence = "Independent original-game runtime observations for this card and interaction scope.",
                gaps = profile is null ? new[] { "Complete executable card contract and behavior tests are not reviewed for this release." } : []
            };
        }
        return new
        {
            schemaVersion = 1, source = catalog.Source,
            reconciliation = new { propertyRows, zeroSentinelRows = 1, catalogIds = catalog.Count,
                countingRule = "Distinct nonzero game-local IDs; retain artwork variants; exclude row-zero sentinel." },
            scope = "Engine-level preview pool only; presentation, AI, tag/campaign and full PSP fidelity have separate gates.",
            playablePreview = new { id = PlayablePreview.ManifestId, cards = catalog.Cards.Where(c => preview.ContainsKey(c.GameCardId)).Select(PreviewEntry).ToArray() },
            pspFidelity10 = new
            {
                id = "tfsp-psp-fidelity-1.0", ready = false,
                denominatorRule = "All distinct nonzero catalog IDs except reviewed effect-generated Tokens; unobtainable cards remain included.",
                excludedTokens = catalog.Cards.Where(c => tokens.Contains(c.GameCardId))
                    .Select(c => new { c.GameCardId, c.Name, reason = "effect-generated token" }).ToArray(),
                cards = catalog.Cards.Where(c => !tokens.Contains(c.GameCardId)).Select(Entry).ToArray()
            },
            families = PlayablePreview.Cards.GroupBy(c => c.Family).Select(g => new { family = g.Key, cardIds = g.Select(c => c.CardId).ToArray() }),
            familyBatches = new[]
                {
                    fullFamilyCatalog ? new { id = NormalMonsterFamily.BatchId, family = NormalMonsterFamily.FamilyId, cardIds = NormalMonsterFamily.CardIds.ToArray() } : null,
                    fullChainCatalog ? new { id = ChainCardFamily.BatchId, family = ChainCardFamily.FamilyId, cardIds = chainCards.Keys.Order().ToArray() } : null,
                    fullLifePointCatalog ? new { id = LifePointFamily.BatchId, family = LifePointFamily.FamilyId, cardIds = lifePointCards.Keys.Order().ToArray() } : null,
                    fullDeckCatalog ? new { id = DeckOperationFamily.BatchId, family = DeckOperationFamily.FamilyId, cardIds = deckCards.Keys.Order().ToArray() } : null,
                    fullRemovalCatalog ? new { id = RemovalFamily.BatchId, family = RemovalFamily.FamilyId, cardIds = removalCards.Keys.Order().ToArray() } : null,
                    fullGraveCatalog ? new { id = GraveyardFamily.BatchId, family = GraveyardFamily.FamilyId, cardIds = graveCards.Keys.Order().ToArray() } : null,
                    fullBattleCatalog ? new { id = BattleTrapFamily.BatchId, family = BattleTrapFamily.FamilyId, cardIds = battleCards.Keys.Order().ToArray() } : null,
                    fullSummonCatalog ? new { id = SummonTrapFamily.BatchId, family = SummonTrapFamily.FamilyId, cardIds = summonCards.Keys.Order().ToArray() } : null,
                    fullNegationCatalog ? new { id = SummonNegationFamily.BatchId, family = SummonNegationFamily.FamilyId, cardIds = negationCards.Keys.Order().ToArray() } : null
                }.OfType<object>().ToArray(),
            unclassifiedCardCount = catalog.Cards.Count(c => !tokens.Contains(c.GameCardId) && !preview.ContainsKey(c.GameCardId) &&
                !(fullFamilyCatalog && normalMonsters.Contains(c.GameCardId)) && !(fullChainCatalog && chainCards.ContainsKey(c.GameCardId)) &&
                !(fullLifePointCatalog && lifePointCards.ContainsKey(c.GameCardId)) &&
                !(fullDeckCatalog && deckCards.ContainsKey(c.GameCardId)) &&
                !(fullRemovalCatalog && removalCards.ContainsKey(c.GameCardId)) &&
                !(fullGraveCatalog && graveCards.ContainsKey(c.GameCardId)) &&
                !(fullBattleCatalog && battleCards.ContainsKey(c.GameCardId)) &&
                !(fullSummonCatalog && summonCards.ContainsKey(c.GameCardId)) &&
                !(fullNegationCatalog && negationCards.ContainsKey(c.GameCardId))),
            exampleDeck = PlayablePreview.ExampleDeck
        };
    }

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 3 || !uint.TryParse(args[1], out var minimum) || !uint.TryParse(args[2], out var maximum))
        {
            error.WriteLine("Usage: card-support <cardinfo.ehp> <minimum-card-id> <maximum-card-id>");
            return 2;
        }
        try
        {
            using var stream = File.OpenRead(args[0]);
            var catalog = ReviewedCardMetadata.Overlay(CardCatalogImporter.Read(stream, minimum, maximum));
            var properties = new EhpReader().ReadIndex(stream).Entries.Single(e => e.Name == "CARD_Prop.bin");
            var manifest = Build(catalog, checked((int)(properties.Length / 8)));
            output.WriteLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or OverflowException)
        {
            error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }
}
