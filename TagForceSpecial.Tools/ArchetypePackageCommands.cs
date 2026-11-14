using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.Effects.Archetypes;

namespace TagForceSpecial.Tools;

public static class ArchetypePackageCommands
{
    public static int Run(string command, string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            object result;
            if (command == "archetype-classic" && args.Length == 0)
            {
                result = new { status = "pass", ClassicArchetypes.Resolver.Fingerprint,
                    snapshotFingerprint = ClassicArchetypes.Snapshot.Fingerprint,
                    cards = ClassicArchetypes.Snapshot.Cards.Count,
                    authoredCards = TagForceSpecial.Effects.TemplateCardFamily.Family.Cards.Count,
                    groups = ClassicArchetypes.Snapshot.GetAll().Select(d => new { id = d.Id.Value,
                        members = ClassicArchetypes.GetMembers(d.Id.Value),
                        monsters = ClassicArchetypes.GetMembers(d.Id.Value, new(Kind: CardKind.Monster)),
                        levelFourOrLower = ClassicArchetypes.GetMembers(d.Id.Value, new(Kind: CardKind.Monster, MaxLevel: 4)) }) };
            }
            else if (command == "archetype-load" && args.Length == 1)
            {
                var content = ArchetypeContentLoader.LoadFile(args[0]);
                result = new { status = "pass", profile = content.Snapshot.Profile.ToString(), content.Fingerprint,
                    snapshotFingerprint = content.Snapshot.Fingerprint, packages = content.Packages,
                    cards = content.Snapshot.Cards.Count, archetypes = content.Snapshot.GetAll().Count,
                    nativeTables = content.NativeIndexes.Count };
            }
            else if (command == "archetype-package-native" && args.Length == 5)
            {
                string[] inputs = [args[0], args[2], Path.Combine(args[1], "cards.json"), Path.Combine(args[1], "manifest.json")];
                var before = inputs.Select(HashFile).ToArray();
                if (new FileInfo(args[0]).Length > 4 + short.MaxValue * 6) throw new InvalidDataException("Native table exceeds its size limit.");
                var catalog = CardCatalogPackage.Load(args[1]);
                var imported = NativeArchetypeImporter.Import(File.ReadAllBytes(args[0]), args[3], catalog.Catalog,
                    catalog.Manifest.CardsSha256, Read<NativeArchetypeLabelsDocument>(args[2]));
                var evidence = new ArchetypeEvidence("catalog cards.json", catalog.Manifest.CardsSha256,
                    "Explicit original catalog identity and decoded kind/Level/Rank metadata.");
                var cards = catalog.Catalog.Cards.Select(c =>
                {
                    uint kind = c.Codes!.GetterKind;
                    if (kind == 12 || kind > 32) throw new InvalidDataException("Native catalog contains an unresolved kind code.");
                    var category = kind == 13 ? CardKind.Spell : kind == 14 ? CardKind.Trap : CardKind.Monster;
                    var facts = new ArchetypeCardFacts(new("tfs:" + c.GameCardId.ToString(CultureInfo.InvariantCulture)), category,
                        category == CardKind.Monster && kind is not (22 or 23) ? c.Statistics.Level : null,
                        kind is 22 or 23 ? c.Statistics.Level : null, kind == 10);
                    return new ArchetypeCatalogCard(c.GameCardId, facts, ArchetypeReviewStatus.Reviewed, evidence);
                }).ToArray();
                if (!before.SequenceEqual(inputs.Select(HashFile))) throw new InvalidDataException("Native package inputs changed during conversion.");
                var package = new ArchetypePackageDocument(imported.Pack, new(1, cards), new(1, []), new(1, []), imported.NativeIndex.Document);
                result = ArchetypePackageStore.Write(args[4], package, new(cards.Select(c => c.Facts.CardId).ToArray(), []));
            }
            else if (command == "archetype-package" && args.Length == 3)
            {
                string directory = args[0];
                var pack = new ArchetypePackDocument(Read<ArchetypePackManifest>(Path.Combine(directory, "manifest.json")),
                    Read<ArchetypeDefinitionsDocument>(Path.Combine(directory, "definitions.json")),
                    Read<ArchetypeMembershipsDocument>(Path.Combine(directory, "memberships.json")),
                    Read<ArchetypeIdentityRulesDocument>(Path.Combine(directory, "identity-rules.json")),
                    Read<NativeArchetypeMappingsDocument>(Path.Combine(directory, "native-mappings.json")));
                var cards = Read<ArchetypeCardCatalogDocument>(Path.Combine(directory, "catalog.json"));
                if (cards.Cards is null || cards.Cards.Any(c => c is null || c.Facts is null))
                    throw new InvalidDataException("Catalog authoring entries cannot be null.");
                var dependencies = args[1] == "-" ? null : ArchetypeContentLoader.LoadFile(args[1]);
                var known = cards.Cards.Select(c => c.Facts.CardId).Concat(dependencies?.Snapshot.Cards.Select(c => c.CardId) ?? [])
                    .Distinct().ToArray();
                var exports = dependencies?.Snapshot.Packs.Select(p => new ArchetypeDependencyExports(p.Manifest.PackId, p.Manifest.Version,
                    p.Definitions.Definitions.Select(d => d.Id).ToArray())).ToArray() ?? [];
                string native = Path.Combine(directory, "native-table.json");
                result = ArchetypePackageStore.Write(args[2], new(pack, cards,
                    Read<ArchetypeCardMappingsDocument>(Path.Combine(directory, "card-mappings.json")),
                    Read<ArchetypeMappingSourcesDocument>(Path.Combine(directory, "mapping-sources.json")),
                    File.Exists(native) ? Read<NativeArchetypeTableDocument>(native) : null), new(known, exports));
            }
            else
            {
                error.WriteLine("Usage: archetype-load <startup.json>\n" +
                    "       archetype-package <authoring-directory> <dependency-startup.json|-> <new-directory>\n" +
                    "       archetype-package-native <table.bin> <catalog-package> <labels.json> <expected-table-sha256> <new-directory>");
                return 2;
            }
            output.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            error.WriteLine($"ERROR: {ex.Message}"); return 1;
        }
    }

    private static T Read<T>(string path) where T : class
    {
        if (new FileInfo(path).Length > ArchetypePackJson.MaximumDocumentBytes) throw new InvalidDataException("Authoring document exceeds its size limit.");
        return ArchetypePackJson.ReadDocument<T>(File.ReadAllText(path));
    }
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
