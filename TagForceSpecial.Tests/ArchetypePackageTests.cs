using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using TagForceSpecial.Cards;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.Effects.Archetypes;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

public sealed class ArchetypePackageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tagforce-archetype-packages-" + Guid.NewGuid().ToString("N"));
    private static readonly ArchetypeId Existing = new("official:example"), Added = new("synthetic:future");
    private static readonly SourceCardId Original = new("tfs:4007"), NewCard = new("synthetic:new-card");
    private static readonly ArchetypePackId BaseId = new("synthetic:base"), ExpansionId = new("synthetic:expansion");
    private static ArchetypeEvidence Evidence => new("synthetic pinned dataset", new string('b', 64), "Invented data, not PSP gameplay evidence.");
    private static ArchetypeMappingSourcesDocument Sources => new(1,
        [new("carddb", "1", ArchetypeReviewStatus.Reviewed, Evidence), new("setdb", "1", ArchetypeReviewStatus.Reviewed, Evidence)]);
    private static ArchetypePackageDocument Base => Package(BaseId, Existing, Original, 41, "0x1008");
    private static ArchetypePackageDocument Expansion => Package(ExpansionId, Added, NewCard, uint.MaxValue, "0x3008") with
    {
        Pack = Package(ExpansionId, Added, NewCard, uint.MaxValue, "0x3008").Pack with
        {
            Manifest = new(1, ExpansionId, "1", ArchetypePackKind.Expansion, [ArchetypeContentProfile.Expanded], [new(BaseId, "1")], Evidence),
            Memberships = new(1, [new(NewCard, [Existing, Added], ArchetypeReviewStatus.Reviewed, Evidence),
                new(Original, [Added], ArchetypeReviewStatus.Reviewed, Evidence)])
        }
    };

    private static ArchetypePackageDocument Package(ArchetypePackId id, ArchetypeId group, SourceCardId card, uint catalogId, string external) => new(
        new(new(1, id, "1", ArchetypePackKind.Synthetic, [ArchetypeContentProfile.TfsClassic, ArchetypeContentProfile.Expanded], [], Evidence),
            new(1, [new(group, group.Value, [], [new("setdb", external, Evidence)], ArchetypeReviewStatus.Reviewed, Evidence)], []),
            new(1, [new(card, [group], ArchetypeReviewStatus.Reviewed, Evidence)]), new(1, []), new(1, [])),
        new(1, [new(catalogId, new(card, CardKind.Monster, Level: 4), ArchetypeReviewStatus.Reviewed, Evidence)]),
        new(1, [new(new("carddb:" + catalogId), card, ArchetypeReviewStatus.Reviewed, Evidence)]), Sources);

    private static ArchetypeValidationContext Context => new([Original, NewCard], [new(BaseId, "1", [Existing])]);
    private ArchetypePackageReference Write(ArchetypePackageDocument package, string name) =>
        ArchetypePackageStore.Write(Path.Combine(root, name), package, Context);
    private static ArchetypeStartupConfiguration Configuration(ArchetypeContentProfile profile, ArchetypePackId rootId,
        params ArchetypePackageReference[] references) => new(1, profile, [new(rootId, "1")], references);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public void Dispose()
    {
        // This unique fixture-owned directory contains only generated test inputs and packages.
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void StartupLoadsExactDependenciesAndNewCardsGroupsAndMappingsFromData()
    {
        var original = Write(Base, "base"); var expansion = Write(Expansion, "expansion");
        var loaded = ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.Expanded, ExpansionId, expansion, original));
        Assert.Equal(2, loaded.Packages.Count); Assert.Equal(2, loaded.Snapshot.Cards.Count);
        Assert.True(loaded.IsMember(new("carddb:4294967295"), Existing));
        Assert.True(loaded.IsMember(Original, Added));
        Assert.Equal(41u, loaded.GetCatalogCardId(Original));
        Assert.Equal(uint.MaxValue, loaded.GetCatalogCardId(new("carddb:4294967295")));
        Assert.Equal(NewCard, loaded.GetSourceCard(uint.MaxValue));
        Assert.Null(loaded.ResolveCard(new("unmapped:4294967295")));
        Assert.Equal(Existing, loaded.ResolveExternalArchetype("setdb", "0x1008"));
        Assert.Equal(Added, loaded.ResolveExternalArchetype("setdb", "0x3008"));
        Assert.Null(loaded.ResolveExternalArchetype("setdb", "0x0008"));
        Assert.Null(loaded.ResolveExternalArchetype("setdb", "0X1008"));
        Assert.Null(loaded.ResolveExternalArchetype("other", "0x1008"));
    }

    [Fact]
    public void ClassicNeverOpensAnUnselectedExpansionAndSelectionOrderDoesNotChangeIdentity()
    {
        var original = Write(Base, "base"); var expansion = Write(Expansion, "expansion");
        var invalidUnused = expansion with { Directory = Path.Combine(root, "not-installed") };
        var classic = ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.TfsClassic, BaseId, original, invalidUnused));
        Assert.Single(classic.Packages); Assert.Null(classic.GetCatalogCardId(NewCard));
        Assert.False(classic.IsMember(Original, Added));
        var first = ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.Expanded, ExpansionId, original, expansion));
        var second = ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.Expanded, ExpansionId, expansion, original));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.TfsClassic, ExpansionId, original, expansion)));
    }

    [Fact]
    public void ExistingLoadedContentRemainsPinnedAcrossDiskEditsAndNewVersionSelections()
    {
        var reference = Write(Base, "v1");
        var configuration = Configuration(ArchetypeContentProfile.TfsClassic, BaseId, reference);
        var loaded = ArchetypeContentLoader.Load(configuration); string fingerprint = loaded.Fingerprint;
        File.AppendAllText(Path.Combine(reference.Directory, "catalog.json"), " ");
        Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(configuration));
        Assert.True(loaded.IsMember(Original, Existing)); Assert.Equal(fingerprint, loaded.Fingerprint);
        Assert.Equal(41u, loaded.GetCatalogCardId(Original));
        var second = Write(Base with { Pack = Base.Pack with { Manifest = Base.Pack.Manifest with { Version = "2" } } }, "v2");
        var newer = ArchetypeContentLoader.Load(new(1, ArchetypeContentProfile.TfsClassic, [new(BaseId, "2")], [second]));
        Assert.NotEqual(fingerprint, newer.Fingerprint); Assert.Equal(fingerprint, loaded.Fingerprint);
        Assert.Throws<NotSupportedException>(() => ((IList<ArchetypeLoadedPackage>)loaded.Packages)[0] = newer.Packages[0]);
    }

    [Fact]
    public void AvailableNewerRevisionsAreNotChosenUnlessRequiredAndIncompatibleRootsFail()
    {
        var original = Write(Base, "v1"); var expansion = Write(Expansion, "expansion");
        var second = Write(Base with { Pack = Base.Pack with { Manifest = Base.Pack.Manifest with { Version = "2" } } }, "v2");
        var loaded = ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.Expanded, ExpansionId, second, original, expansion));
        Assert.Equal("1", Assert.Single(loaded.Packages, p => p.PackId == BaseId).Version);
        Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(new(1, ArchetypeContentProfile.Expanded,
            [new(BaseId, "2"), new(ExpansionId, "1")], [second, original, expansion])));
        Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.Expanded, ExpansionId, second, expansion)));
        Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.Expanded, BaseId, original, original)));
    }

    [Fact]
    public void PackagesAreReproduciblePortableAndExistingRevisionsCannotBeOverwritten()
    {
        var first = Write(Base, "one"); var second = Write(Base, "two");
        Assert.Equal(first.Sha256, second.Sha256);
        foreach (string path in Directory.GetFiles(first.Directory))
            Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(Path.Combine(second.Directory, Path.GetFileName(path))));
        Assert.Throws<IOException>(() => Write(Base, "one"));
        Assert.Equal(ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.TfsClassic, BaseId, first)).Fingerprint,
            ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.TfsClassic, BaseId, second)).Fingerprint);
        string configuration = Path.Combine(root, "startup.json");
        File.WriteAllText(configuration, ArchetypePackJson.WriteDocument(Configuration(ArchetypeContentProfile.TfsClassic, BaseId,
            first with { Directory = "one" })));
        Assert.True(ArchetypeContentLoader.LoadFile(configuration).IsMember(Original, Existing));
        Assert.Equal(0, ArchetypePackageCommands.Run("archetype-load", [configuration], TextWriter.Null, TextWriter.Null));
    }

    [Fact]
    public void FileLoadedDependencyCyclesAndDuplicateRootsAreRejected()
    {
        var firstData = Base with { Pack = Base.Pack with { Manifest = Base.Pack.Manifest with { Dependencies = [new(ExpansionId, "1")] } } };
        var first = ArchetypePackageStore.Write(Path.Combine(root, "cycle-a"), firstData,
            Context with { Dependencies = [new(ExpansionId, "1", [Added])] });
        var second = Write(Expansion, "cycle-b");
        Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.Expanded, BaseId, first, second)));
        var configuration = Configuration(ArchetypeContentProfile.Expanded, BaseId, first, second);
        Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(configuration with { Roots = [new(BaseId, "1"), new(BaseId, "1")] }));
    }

    [Fact]
    public void NativeSourceCannotBeDeclaredWithoutItsTableAndOtherPacksCannotCarryOne()
    {
        var native = Base with { Pack = Base.Pack with { Manifest = Base.Pack.Manifest with { Kind = ArchetypePackKind.Native } } };
        Assert.Throws<InvalidDataException>(() => Write(native, "missing-native"));
        var inventedTable = new NativeArchetypeTableDocument(1, new string('b', 64), new string('b', 64), 0, [], [new(0, 0, [])]);
        Assert.Throws<InvalidDataException>(() => Write(Base with { NativeTable = inventedTable }, "synthetic-native"));
    }

    [Theory]
    [InlineData("catalog-id")]
    [InlineData("alias-shadow")]
    [InlineData("dataset-version")]
    public void ConflictingCatalogAliasesAndDatasetVersionsCannotDependOnLoadOrder(string conflict)
    {
        var original = Write(Base, "base"); var package = Expansion;
        if (conflict == "catalog-id") package = package with { Catalog = new(1, [package.Catalog.Cards[0] with { CatalogCardId = 41 }]) };
        if (conflict == "alias-shadow") package = package with
        {
            MappingSources = new(1, [.. Sources.Sources, new("tfs", "1", ArchetypeReviewStatus.Reviewed, Evidence)]),
            CardMappings = new(1, [new(Original, NewCard, ArchetypeReviewStatus.Reviewed, Evidence)])
        };
        if (conflict == "dataset-version") package = package with { MappingSources = new(1,
            Sources.Sources.Select(s => s.System == "setdb" ? s with { Version = "2" } : s).ToArray()) };
        var expansion = Write(package, "bad-selection");
        Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.Expanded, ExpansionId, original, expansion)));
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("unknown-card")]
    [InlineData("unregistered-system")]
    [InlineData("wrong-dataset-hash")]
    [InlineData("zero-catalog-id")]
    public void ReviewedMappingsNeedExplicitKnownTargetsAndMatchingVersionedSources(string invalid)
    {
        var package = Base;
        package = invalid switch
        {
            "candidate" => package with { CardMappings = new(1, [package.CardMappings.Mappings[0] with { Review = ArchetypeReviewStatus.Candidate }]) },
            "unknown-card" => package with { CardMappings = new(1, [package.CardMappings.Mappings[0] with { CardId = new("synthetic:unknown") }]) },
            "unregistered-system" => package with { MappingSources = new(1, []) },
            "wrong-dataset-hash" => package with { CardMappings = new(1, [package.CardMappings.Mappings[0] with { Evidence = Evidence with { Sha256 = new string('c', 64) } }]) },
            _ => package with { Catalog = new(1, [package.Catalog.Cards[0] with { CatalogCardId = 0 }]) }
        };
        Assert.Throws<InvalidDataException>(() => Write(package, "invalid"));
        Assert.False(Directory.Exists(Path.Combine(root, "invalid")));
    }

    [Theory]
    [InlineData("null-cards")]
    [InlineData("schema")]
    [InlineData("unknown-property")]
    [InlineData("unsafe-inventory")]
    [InlineData("candidate")]
    public void CorrectlyRehashedButInvalidDocumentsStillFailSchemaAndSemanticValidation(string invalid)
    {
        var reference = Write(Base, "package");
        if (invalid == "unsafe-inventory")
        {
            string path = Path.Combine(reference.Directory, "package.json"); var node = JsonNode.Parse(File.ReadAllText(path))!;
            node["documentHashes"]!["../escape.json"] = new string('a', 64); File.WriteAllText(path, node.ToJsonString());
            reference = reference with { Sha256 = Hash(File.ReadAllBytes(path)) };
        }
        else
        {
            string name = invalid == "candidate" ? "definitions.json" : "catalog.json";
            var node = JsonNode.Parse(File.ReadAllText(Path.Combine(reference.Directory, name)))!;
            if (invalid == "null-cards") node["cards"] = null;
            if (invalid == "schema") node["schemaVersion"] = 99;
            if (invalid == "unknown-property") node["unexpected"] = true;
            if (invalid == "candidate") node["definitions"]![0]!["review"] = "candidate";
            reference = Rewrite(reference, name, node.ToJsonString());
        }
        var configuration = Configuration(ArchetypeContentProfile.TfsClassic, BaseId, reference);
        if (invalid == "unknown-property") Assert.Throws<JsonException>(() => ArchetypeContentLoader.Load(configuration));
        else Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(configuration));
    }

    [Fact]
    public void MissingChangedExtraAndOversizedFilesAreRejectedBeforePublishingContent()
    {
        var reference = Write(Base, "package"); var configuration = Configuration(ArchetypeContentProfile.TfsClassic, BaseId, reference);
        Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(configuration with { Packages = [reference with { Sha256 = new string('0', 64) }] }));
        File.WriteAllText(Path.Combine(reference.Directory, "unexpected"), "extra");
        Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(configuration)); File.Delete(Path.Combine(reference.Directory, "unexpected"));
        string file = Path.Combine(reference.Directory, "catalog.json");
        using (var stream = File.OpenWrite(file)) stream.SetLength(ArchetypePackJson.MaximumDocumentBytes + 1L);
        Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(configuration));
        File.Delete(file); Assert.Throws<InvalidDataException>(() => ArchetypeContentLoader.Load(configuration));
    }

    [Fact]
    public void GenericAuthoringCommandCanPackageAnExpansionUsingOnlyJsonAndAnExistingDependencySelection()
    {
        var original = Write(Base, "base"); string configuration = Path.Combine(root, "dependencies.json");
        File.WriteAllText(configuration, ArchetypePackJson.WriteDocument(Configuration(ArchetypeContentProfile.Expanded, BaseId, original)));
        var source = Write(Expansion, "authoring"); File.Delete(Path.Combine(source.Directory, "package.json"));
        string destination = Path.Combine(root, "rebuilt-data"); var output = new StringWriter();
        Assert.Equal(0, ArchetypePackageCommands.Run("archetype-package", [source.Directory, configuration, destination], output, TextWriter.Null));
        var reference = JsonSerializer.Deserialize<ArchetypePackageReference>(output.ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.True(ArchetypeContentLoader.Load(Configuration(ArchetypeContentProfile.Expanded, ExpansionId, original, reference)).IsMember(NewCard, Added));
    }

    [Fact]
    public void NativeBridgeRetainsEmptySlotsTokensAndNumericAdapterAlongsideSemanticContent()
    {
        Directory.CreateDirectory(root); var bytes = new byte[16];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, 2); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(10), 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(12), 10); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(14), 11);
        string table = Path.Combine(root, "table.bin"), labels = Path.Combine(root, "labels.json"), catalog = Path.Combine(root, "catalog");
        File.WriteAllBytes(table, bytes); File.WriteAllText(labels, ArchetypePackJson.WriteDocument(new NativeArchetypeLabelsDocument(1,
            [new(1, "Known", Existing, [], ArchetypeReviewStatus.Reviewed, Evidence)])));
        var cards = new CardCatalog(new(null, "synthetic-native"), Enumerable.Range(10, 2).Select(i => new CardDefinition((uint)i, $"Card {i}", "",
            new(0, 0, 1), new(0, 0, 1, i == 11 ? 10u : 1u, 0, 0, 0))));
        CardCatalogPackage.Write(catalog, cards, new("synthetic", "1", "en", "invented"));
        var output = new StringWriter(); string destination = Path.Combine(root, "native");
        Assert.Equal(0, ArchetypePackageCommands.Run("archetype-package-native", [table, catalog, labels, Hash(bytes), destination], output, TextWriter.Null));
        var reference = JsonSerializer.Deserialize<ArchetypePackageReference>(output.ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var loaded = ArchetypeContentLoader.Load(new(1, ArchetypeContentProfile.TfsClassic, [new(reference.PackId, reference.Version)], [reference]));
        var native = Assert.Single(loaded.NativeIndexes).Value;
        Assert.Empty(native.GetMembers(0)); Assert.True(native.Contains(1, 11)); Assert.True(native.IsToken(11));
        Assert.True(loaded.IsMember(new("tfs:10"), Existing)); Assert.False(loaded.IsMember(new("tfs:11"), Existing));
        Assert.Equal(Hash(bytes), native.Document.SourceSha256);
    }

    private static ArchetypePackageReference Rewrite(ArchetypePackageReference reference, string name, string json)
    {
        string path = Path.Combine(reference.Directory, name); File.WriteAllText(path, json);
        string inventoryPath = Path.Combine(reference.Directory, "package.json"); var inventory = JsonNode.Parse(File.ReadAllText(inventoryPath))!;
        inventory["documentHashes"]![name] = Hash(File.ReadAllBytes(path)); File.WriteAllText(inventoryPath, inventory.ToJsonString());
        return reference with { Sha256 = Hash(File.ReadAllBytes(inventoryPath)) };
    }
}
