using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using TagForceSpecial.Cards;
using TagForceSpecial.Cards.CatalogStorage;

namespace TagForceSpecial.Tests;

public sealed class CardCatalogPackageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tagforce-catalog-tests-" + Guid.NewGuid().ToString("N"));
    private static CardCatalogPackageInfo Identity => new("synthetic", "1", "en", "authored-test-inputs");
    private static CardCatalog Catalog() => new(new(null, "authored-test-v1"),
    [
        new(2, "Same name", "Unknown effects; 日本語", new(null, 1150, 4),
            new(2, 3, 4, 5, 6, 7, 8), new(3, uint.MaxValue, 123, 456), new(new string('a', 64), "import-test")),
        new(1, "Same name", "Confirmed ordinary monster", new(300, 200, 1), Kind: CardKind.Monster, Effects: CardEffectBinding.None),
        new(uint.MaxValue, "Authored spell", "Custom definition", new(0, 0, 0), Kind: CardKind.Spell,
            Effects: new(CardEffectState.Referenced, ["authored-gain-v1"]))
    ]);

    public void Dispose()
    {
        // All paths here are generated under a unique test-owned temporary directory.
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private string Package(string name = "v1")
    {
        string path = Path.Combine(root, name);
        CardCatalogPackage.Write(path, Catalog(), Identity);
        return path;
    }

    private static void ChangeManifest(string path, Action<JsonObject> edit)
    {
        string file = Path.Combine(path, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        edit(manifest);
        File.WriteAllText(file, manifest.ToJsonString());
    }

    private static void ChangeCards(string path, Action<JsonObject> edit)
    {
        string file = Path.Combine(path, "cards.json");
        var doc = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        edit(doc);
        File.WriteAllText(file, doc.ToJsonString());
        ChangeManifest(path, m => m["cardsSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
    }

    [Fact]
    public void RoundTripPreservesOriginalIdsVariantsRawProvenanceAndThreeEffectStates()
    {
        var original = Catalog();
        var loaded = CardCatalogPackage.Load(Package());
        Assert.Equal(Identity, loaded.Manifest.Identity);
        Assert.Equal(3, loaded.Manifest.CardCount);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(loaded.Catalog));
        Assert.Equal(new uint[] { 1, 2, uint.MaxValue }, loaded.Catalog.Cards.Select(c => c.GameCardId));
        Assert.Equal(CardEffectState.None, loaded.Catalog[1].Effects!.State);
        Assert.Equal(CardEffectState.Unknown, loaded.Catalog[2].Effects!.State);
        Assert.Equal(CardEffectState.Referenced, loaded.Catalog[uint.MaxValue].Effects!.State);
        Assert.Null(loaded.Catalog[2].Statistics.Attack);
        Assert.Equal(1150, loaded.Catalog[2].Statistics.Defense);
        Assert.Equal(original[2].SourceRow, loaded.Catalog[2].SourceRow);
        Assert.Equal(original[2].Source, loaded.Catalog[2].Source);
        Assert.Null(loaded.Catalog[uint.MaxValue].Codes);
        Assert.Equal("authored-gain-v1", Assert.Single(loaded.Catalog[uint.MaxValue].Effects!.References));
    }

    [Fact]
    public void PackagesAreDeterministicAndExistingVersionsAreNeverOverwritten()
    {
        string first = Package(); string second = Package("v1-repeat");
        Assert.Equal(File.ReadAllBytes(Path.Combine(first, "cards.json")), File.ReadAllBytes(Path.Combine(second, "cards.json")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(first, "manifest.json")), File.ReadAllBytes(Path.Combine(second, "manifest.json")));
        var before = File.ReadAllBytes(Path.Combine(first, "cards.json"));
        Assert.Throws<IOException>(() => CardCatalogPackage.Write(first, Catalog(), Identity with { Revision = "2" }));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(first, "cards.json")));
    }

    [Fact]
    public void ReferenceCollectionsAndLoadedCatalogsCannotBeMutatedThroughAliases()
    {
        var ids = new List<string> { "first" };
        var effects = new CardEffectBinding(CardEffectState.Referenced, ids);
        ids[0] = "changed";
        Assert.Equal("first", effects.References[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)effects.References)[0] = "changed");
        var catalog = CardCatalogPackage.Load(Package()).Catalog;
        Assert.Throws<NotSupportedException>(() => ((IList<CardDefinition>)catalog.Cards).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)catalog[uint.MaxValue].Effects!.References).Clear());
        var merged = catalog.Merge(new(new(null, "other"), [new(20, "Extra", "", new(0, 0, 1))]));
        Assert.Equal(3, catalog.Count);
        Assert.Equal(4, merged.Count);
        Assert.Equal(catalog[2].Source, merged[2].Source);
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("identity")]
    [InlineData("source")]
    [InlineData("cardCount")]
    [InlineData("cardsFile")]
    [InlineData("cardsSha256")]
    public void InvalidManifestRejectsBeforeReplacingPublishedCatalog(string field)
    {
        string path = Package();
        var original = Catalog(); var provider = new CardCatalogProvider(original);
        ChangeManifest(path, m => m[field] = field switch
        {
            "schemaVersion" => JsonValue.Create(99), "cardCount" => JsonValue.Create(100001),
            "cardsFile" => JsonValue.Create("../outside.json"), "cardsSha256" => JsonValue.Create("bad"), _ => null
        });
        Assert.Throws<InvalidDataException>(() => provider.Reload(path));
        Assert.Same(original, provider.Current);
    }

    [Fact]
    public void CorruptMissingAndTruncatedFilesPreserveTheLastValidSnapshot()
    {
        string path = Package(); var provider = new CardCatalogProvider(Catalog());
        var valid = provider.Reload(path).Catalog;
        File.AppendAllText(Path.Combine(path, "cards.json"), "corrupt");
        Assert.Throws<InvalidDataException>(() => provider.Reload(path));
        Assert.Same(valid, provider.Current);
        File.WriteAllText(Path.Combine(path, "manifest.json"), "{");
        Assert.Throws<InvalidDataException>(() => provider.Reload(path));
        Assert.Same(valid, provider.Current);
        File.Delete(Path.Combine(path, "manifest.json"));
        Assert.Throws<FileNotFoundException>(() => provider.Reload(path));
        Assert.Same(valid, provider.Current);
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("zero-id")]
    [InlineData("negative-stat")]
    [InlineData("missing-stat")]
    [InlineData("invalid-kind")]
    [InlineData("missing-reference")]
    [InlineData("duplicate-reference")]
    [InlineData("unknown-with-reference")]
    [InlineData("missing-source")]
    [InlineData("bad-origin-hash")]
    [InlineData("wrong-source")]
    [InlineData("wrong-count")]
    [InlineData("wrong-version")]
    [InlineData("unknown-property")]
    [InlineData("incomplete-raw-codes")]
    public void MalformedDefinitionsRejectEvenWithAnUpdatedChecksum(string failure)
    {
        string path = Package();
        ChangeCards(path, d =>
        {
            var cards = d["cards"]!.AsArray();
            var card = cards[0]!.AsObject();
            switch (failure)
            {
                case "duplicate-id": cards[1]!["gameCardId"] = card["gameCardId"]!.DeepClone(); break;
                case "zero-id": card["gameCardId"] = 0; break;
                case "negative-stat": card["statistics"]!["attack"] = -1; break;
                case "missing-stat": card["statistics"]!.AsObject().Remove("defense"); break;
                case "invalid-kind": card["kind"] = "EffectEngine"; break;
                case "missing-reference": cards[2]!["effectReferences"] = new JsonArray(); break;
                case "duplicate-reference": cards[2]!["effectReferences"] = new JsonArray("same", "same"); break;
                case "unknown-with-reference": card["effectState"] = "Unknown"; card["effectReferences"] = new JsonArray("some-effect"); break;
                case "missing-source": card["source"] = null; break;
                case "bad-origin-hash": card["source"]!["containerSha256"] = "bad"; break;
                case "wrong-source": d["source"]!["decoderProfile"] = "changed"; break;
                case "wrong-count": cards.RemoveAt(1); break;
                case "wrong-version": d["schemaVersion"] = 2; break;
                case "unknown-property": card["owner"] = 1; break;
                case "incomplete-raw-codes": cards[1]!["codes"]!.AsObject().Remove("type"); break;
            }
        });
        Assert.Throws<InvalidDataException>(() => CardCatalogPackage.Load(path));
    }

    [Fact]
    public void DuplicateJsonKeysAndOversizedFilesAreRejected()
    {
        string path = Package();
        string manifest = Path.Combine(path, "manifest.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"));
        Assert.Throws<InvalidDataException>(() => CardCatalogPackage.Load(path));
        string large = Package("large");
        using (var file = new FileStream(Path.Combine(large, "cards.json"), FileMode.Open, FileAccess.Write))
            file.SetLength(CardCatalogPackage.MaximumCardsBytes + 1L);
        Assert.Throws<InvalidDataException>(() => CardCatalogPackage.Load(large));
    }

    [Fact]
    public void FailedWriteDoesNotExposeAnIncompleteVersionOrLeaveTemporaryFiles()
    {
        var invalid = new CardCatalog(new(null, "authored"), [new(1, "Test", new string('x', 16385), new(0, 0, 1))]);
        string destination = Path.Combine(root, "failed");
        Assert.Throws<InvalidDataException>(() => CardCatalogPackage.Write(destination, invalid, Identity));
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public void EffectStateCannotSilentlyMeanNoEffectOrExecuteCode()
    {
        var unknown = new CardCatalog(new(null, "test"), [new(1, "Test", "", new(0, 0, 1))]);
        Assert.Equal(CardEffectState.Unknown, unknown[1].Effects!.State);
        Assert.Throws<ArgumentException>(() => new CardEffectBinding(CardEffectState.None, ["effect"]));
        Assert.Throws<ArgumentException>(() => new CardEffectBinding(CardEffectState.Referenced, []));
        Assert.Throws<ArgumentException>(() => new CardEffectBinding(CardEffectState.Referenced, [" "]));
    }
}
