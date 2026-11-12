using System.Buffers.Binary;
using System.Security.Cryptography;
using TagForceSpecial.Cards;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.Effects.Archetypes;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

public sealed class NativeArchetypeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tagforce-native-archetypes-" + Guid.NewGuid().ToString("N"));
    private static ArchetypeEvidence Evidence => new("synthetic review", new string('a', 64), "Invented fixture, not original game data.");
    private static readonly ArchetypeId Semantic = new("synthetic:astral");
    private static CardCatalog Catalog => new(new(null, "synthetic-native-fixture"), Enumerable.Range(10, 4).Select(id =>
        new CardDefinition((uint)id, $"Card {id}", "", new(0, 0, 1), new(0, 0, 1, id == 11 ? 10u : 1u, 0, 0, 0))));
    private static NativeArchetypeLabelsDocument Labels => new(1,
        [new(1, "Astral", Semantic, ["Star Group"], ArchetypeReviewStatus.Reviewed, Evidence),
         new(3, "Unreviewed Group", null, [], ArchetypeReviewStatus.Candidate, Evidence)]);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Table()
    {
        var bytes = new byte[30];
        Write(bytes, 0, 4); Write(bytes, 2, 5);
        Write(bytes, 4, 0); Write(bytes, 6, 0);
        Write(bytes, 8, 0); Write(bytes, 10, 3);
        Write(bytes, 12, 3); Write(bytes, 14, 0);
        Write(bytes, 16, 3); Write(bytes, 18, 2);
        Write(bytes, 20, 10); Write(bytes, 22, 11); Write(bytes, 24, 12);
        Write(bytes, 26, 12); Write(bytes, 28, 13);
        return bytes;
    }
    private static void Write(byte[] bytes, int offset, short value) => BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), value);
    private static NativeArchetypeImport Import(byte[]? bytes = null, NativeArchetypeLabelsDocument? labels = null)
    {
        bytes ??= Table();
        return NativeArchetypeImporter.Import(bytes, Hash(bytes), Catalog, new string('b', 64), labels ?? Labels);
    }

    public void Dispose()
    {
        // The directory is unique to this fixture and contains only its generated inputs and outputs.
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void AllSlotsAndTokenMembershipSurviveWhileOnlyReviewedNonTokenMembershipIsProjected()
    {
        var imported = Import();
        Assert.Equal(4, imported.NativeIndex.GroupCount);
        Assert.Equal(5, imported.NativeIndex.MembershipCount);
        Assert.Empty(imported.NativeIndex.GetMembers(0)); Assert.Empty(imported.NativeIndex.GetMembers(2));
        Assert.Equal(new uint[] { 10, 11, 12 }, imported.NativeIndex.GetMembers(1));
        Assert.True(imported.NativeIndex.Contains(1, 11)); Assert.True(imported.NativeIndex.IsToken(11));
        Assert.Equal(Semantic, Assert.Single(imported.Pack.Definitions.Definitions).Id);
        Assert.Equal(new[] { "tfs:10", "tfs:12" }, imported.Pack.Memberships.Memberships.Select(m => m.CardId.Value));
        Assert.Equal(4, imported.Pack.NativeMappings.Mappings.Count);
        Assert.Null(imported.Pack.NativeMappings.Mappings[3].Archetype);
        Assert.Equal(ArchetypeReviewStatus.Candidate, Assert.Single(imported.CandidateLabels.Labels).Review);
        Assert.Empty(imported.Pack.IdentityRules.Rules);
    }

    [Fact]
    public void NativeLookupAgreesWithIndependentFixtureSetsForMembersAndNonmembers()
    {
        var index = Import().NativeIndex;
        uint[][] expected = [[], [10, 11, 12], [], [12, 13]];
        for (int group = 0; group < expected.Length; group++)
            for (uint id = 0; id <= 20; id++) Assert.Equal(expected[group].Contains(id), index.Contains(group, id));
        Assert.False(index.Contains(-1, 10)); Assert.False(index.Contains(4, 10));
        Assert.False(index.Contains(1, uint.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.GetMembers(-1));
    }

    [Fact]
    public void AdapterDeepCopiesAuthoringArraysAndExposesReadOnlyLists()
    {
        uint[] members = [10, 11]; uint[] tokens = [11];
        NativeArchetypeGroup[] groups = [new(0, 0, members)];
        var index = new NativeArchetypeIndex(new(1, new string('a', 64), new string('b', 64), 2, tokens, groups));
        members[0] = 12; tokens[0] = 13; groups[0] = new(0, 0, []);
        Assert.True(index.Contains(0, 10)); Assert.False(index.Contains(0, 12)); Assert.True(index.IsToken(11));
        Assert.Throws<NotSupportedException>(() => ((IList<uint>)index.GetMembers(0))[0] = 12);
        Assert.Throws<NotSupportedException>(() => ((IList<uint>)index.Document.TokenCardIds)[0] = 12);
        var restored = new NativeArchetypeIndex(ArchetypePackJson.ReadDocument<NativeArchetypeTableDocument>(ArchetypePackJson.WriteDocument(index.Document)));
        Assert.Equal(index.GetMembers(0), restored.GetMembers(0));
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(2, -1)]
    [InlineData(8, 1)]
    [InlineData(10, -1)]
    [InlineData(10, 6)]
    [InlineData(20, 0)]
    [InlineData(20, -1)]
    [InlineData(20, 9)]
    [InlineData(22, 10)]
    [InlineData(22, 13)]
    public void MalformedNativeBoundsOrderAndIdsAreRejected(int offset, int value)
    {
        var bytes = Table(); Write(bytes, offset, (short)value);
        Assert.Throws<InvalidDataException>(() => Import(bytes));
    }

    [Fact]
    public void TruncationTrailingBytesWrongSourceAndUnclassifiedCardsAreRejected()
    {
        var bytes = Table();
        Assert.Throws<InvalidDataException>(() => Import(bytes[..^1]));
        Assert.Throws<InvalidDataException>(() => Import([.. bytes, 0]));
        Assert.Throws<InvalidDataException>(() => NativeArchetypeImporter.Import(bytes, new string('0', 64), Catalog, new string('b', 64), Labels));
        var catalog = new CardCatalog(Catalog.Source, Catalog.Cards.Select(c => c with { Codes = null }));
        Assert.Throws<InvalidDataException>(() => NativeArchetypeImporter.Import(bytes, Hash(bytes), catalog, new string('b', 64), Labels));
    }

    [Theory]
    [InlineData("candidate-id")]
    [InlineData("missing-reviewed-id")]
    [InlineData("duplicate-reviewed-slot")]
    [InlineData("unknown-slot")]
    [InlineData("invalid-evidence")]
    public void ReviewClaimsAndLabelReferencesAreValidatedBeforeProjection(string invalid)
    {
        var reviewed = Labels.Labels[0]; var candidate = Labels.Labels[1];
        var labels = invalid switch
        {
            "candidate-id" => new NativeArchetypeLabelsDocument(1, [reviewed, candidate with { Archetype = new ArchetypeId("synthetic:candidate") }]),
            "missing-reviewed-id" => new(1, [reviewed with { Archetype = null }]),
            "duplicate-reviewed-slot" => new(1, [reviewed, reviewed with { Name = "Other", Archetype = new ArchetypeId("synthetic:other") }]),
            "unknown-slot" => new(1, [reviewed with { NativeGroupId = 4 }]),
            _ => new(1, [reviewed with { Evidence = Evidence with { Sha256 = "invalid" } }])
        };
        Assert.Throws<InvalidDataException>(() => Import(labels: labels));
    }

    [Fact]
    public void LocalPackagesAreReproducibleCheckableAndNeverOverwriteAnExistingVersion()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.bin"), labels = Path.Combine(root, "labels.json"), catalog = Path.Combine(root, "catalog");
        var bytes = Table(); File.WriteAllBytes(source, bytes);
        File.WriteAllText(labels, ArchetypePackJson.WriteDocument(Labels));
        CardCatalogPackage.Write(catalog, Catalog, new("synthetic", "1", "en", "authored-fixture"));
        string first = Path.Combine(root, "one"), second = Path.Combine(root, "two");
        string[] Args(string output) => [source, catalog, labels, Hash(bytes), output];
        int Run(string command, string directory) => NativeArchetypeCommands.Run(command, Args(directory), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, Run("native-archetype-import", first));
        Assert.Equal(0, Run("native-archetype-import", second));
        foreach (string path in Directory.GetFiles(first))
            Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(Path.Combine(second, Path.GetFileName(path))));
        Assert.Equal(0, Run("native-archetype-check", first));
        Assert.Equal(1, Run("native-archetype-import", first));
        Assert.Equal(bytes, File.ReadAllBytes(source));
        File.AppendAllText(Path.Combine(first, "definitions.json"), " ");
        Assert.Equal(1, Run("native-archetype-check", first));
        File.WriteAllText(Path.Combine(second, "unexpected.json"), "{}");
        Assert.Equal(1, Run("native-archetype-check", second));
    }
}
