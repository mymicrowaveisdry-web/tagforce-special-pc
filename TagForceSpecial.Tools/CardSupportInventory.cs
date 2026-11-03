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
        var researchCards = new Dictionary<uint, string>
        {
            [7747] = "Emergency Teleport: bounded Deck Krebons lifecycle; hand targets, shuffle and general continuation remain incomplete.",
            [5217] = "Lightning Vortex: bounded discard/face-up destruction research; full effect interactions not accepted.",
            [4909] = "Mystical Space Typhoon: bounded targeting and set response; general Quick-Play windows not accepted.",
            [4818] = "Scapegoat: retained targeted-response experiment; general activation and token lifecycle incomplete.",
            [4862] = "Magic Jammer: explicit discard allowlist; full card contract and comparison not accepted."
        };
        object Entry(CardDefinition c)
        {
            preview.TryGetValue(c.GameCardId, out var profile);
            return new
            {
                c.GameCardId, c.Name, c.Source, c.SourceRow, c.Statistics,
                originalEraText = c.Description,
                rulingBasis = "Imported local source text; default original-era basic duel rules. No later errata inferred.",
                behaviorFamily = profile?.Family ?? (researchCards.ContainsKey(c.GameCardId) ? "bounded-research-profile" : "unclassified"),
                status = profile is not null ? "supported" : researchCards.ContainsKey(c.GameCardId) ? "partial" : "unsupported",
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
                gaps = profile is null ? new[] { researchCards.GetValueOrDefault(c.GameCardId,
                    "Complete executable card contract and behavior tests are not reviewed for this release.") } : []
            };
        }
        return new
        {
            schemaVersion = 1, source = catalog.Source,
            reconciliation = new { propertyRows, zeroSentinelRows = 1, catalogIds = catalog.Count,
                countingRule = "Distinct nonzero game-local IDs; retain artwork variants; exclude row-zero sentinel." },
            scope = "Engine-level preview pool only; presentation, AI, tag/campaign and full PSP fidelity have separate gates.",
            playablePreview = new { id = PlayablePreview.ManifestId, cards = catalog.Cards.Where(c => preview.ContainsKey(c.GameCardId)).Select(Entry).ToArray() },
            pspFidelity10 = new { id = "tfsp-psp-fidelity-1.0", ready = false, cards = catalog.Cards.Select(Entry).ToArray() },
            families = PlayablePreview.Cards.GroupBy(c => c.Family).Select(g => new { family = g.Key, cardIds = g.Select(c => c.CardId).ToArray() }),
            unclassifiedCardCount = catalog.Count - preview.Count,
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
            var catalog = CardCatalogImporter.Read(stream, minimum, maximum);
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
