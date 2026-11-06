using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TagForceSpecial.Assets;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.Tests;

public class PresentationPackageTests
{
    [Fact]
    public void KnownReferenceNameAndEvidenceIdCannotVerifySyntheticSourcePixels()
    {
        using var fixture = new Fixture(); var package = fixture.Create();
        var reference = PresentationReferenceCases.Cases.First();
        var entry = SemanticEntry(package, fixture) with
        {
            AssetKey = reference.AssetKey, Status = "mapped-unverified", EvidenceIds = new[] { reference.EvidenceId }
        };
        var registry = OpenSemantic(fixture, package, entry);
        Assert.Null(registry.VerifiedReferenceScope(reference.AssetKey));
        Assert.Equal("decoded-unverified", Assert.Single(TagForceSpecial.PC.Import.AssetDiagnostics.Inspect(registry,
            [new TagForceSpecial.PC.Import.AssetRequirement(reference.Screen, reference.AssetKey)])).Status);
    }

    [Fact]
    public void PresentationInventoryIsIndependentOfFourteenCardGameplayPool()
    {
        const int count = 21;
        byte[] archive = new byte[(count + 1) * 2048]; "CIP\x1a"u8.CopyTo(archive);
        Put(archive, 4, archive.Length); Put(archive, 16, 48 + count * 8 + 8);
        Put(archive, 20, 1000); Put(archive, 24, 1000 + count - 1);
        Put(archive, 28, 48); Put(archive, 32, 1);
        for (int i = 0; i < count; i++) Put(archive, 48 + i * 8, i + 1);
        BinaryPrimitives.WriteUInt64LittleEndian(archive.AsSpan(48 + count * 8), ulong.MaxValue);
        using var stream = new MemoryStream(archive);
        var inventory = PresentationInventory.ReadCardArchive(stream);
        Assert.Equal(count, inventory.Count);
        Assert.Equal((uint)1000, inventory[0].CardId);
        Assert.All(inventory, row => Assert.Equal("unresolved", row.DecodeStatus));
    }

    [Fact]
    public void PresentationDiagnosticsDoNotCallStoredBytesDecodedOrVerified()
    {
        using var fixture = new Fixture();
        var package = fixture.Create(new PresentationResourceRequest("title/pixel", SourcePath,
            Conversion: PresentationConversion.Stored));
        var registry = OpenSemantic(fixture, package, SemanticEntry(package, fixture) with { Status = "stored" });
        var rows = TagForceSpecial.PC.Import.AssetDiagnostics.Inspect(registry,
            [new TagForceSpecial.PC.Import.AssetRequirement("title", "title.logo")]);
        Assert.Equal("unresolved", Assert.Single(rows).Status);
    }

    [Theory]
    [InlineData("Schema")]
    [InlineData("ConverterId")]
    public void PresentationSchemaAndConverterChangesRequireExplicitRebuild(string field)
    {
        using var fixture = new Fixture(); var package = fixture.Create();
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(Path.Combine(package.DirectoryPath, "manifest.json")))!;
        if (field == "Schema") json[field] = 1;
        else json[field] = "other-converter";
        byte[] changed = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(json);
        string id = FoundationFixture.Hash(changed);
        string destination = Path.Combine(fixture.Cache, "packages", id);
        Directory.CreateDirectory(Path.Combine(destination, "blobs"));
        File.WriteAllBytes(Path.Combine(destination, "manifest.json"), changed);
        foreach (string blob in Directory.GetFiles(Path.Combine(package.DirectoryPath, "blobs")))
            File.Copy(blob, Path.Combine(destination, "blobs", Path.GetFileName(blob)));
        var error = Assert.Throws<InvalidDataException>(() => PresentationPackage.Open(fixture.Cache, id,
            fixture.Profile, Scope, ["title/pixel"]));
        Assert.Contains(field == "Schema" ? "rebuild" : "version mismatch", error.Message);
    }

    [Fact]
    public void PresentationGenerationsRetainPreviousPublicationAndDoNotActivateGameplay()
    {
        using var fixture = new Fixture();
        var storage = new TagForceSpecial.PC.Setup.PrivateStorage(Path.Combine(fixture.Root, "app-cache"));
        var store = new TagForceSpecial.PC.Import.PresentationCacheStore(storage, [fixture.Profile]);
        using var source = fixture.Open();
        var requests = new[] { new PresentationResourceRequest("title/pixel", SourcePath) };
        store.Publish(source, Scope, requests, package => [SemanticEntry(package, fixture)]);
        byte[] active = File.ReadAllBytes(storage.PathFor("presentation/active.json"));
        Assert.Equal(12, store.OpenActive()!.Resolve("title.logo").Content[0]);
        Assert.Contains("integrity-only", File.ReadAllText(store.SignOffActiveIntegrity()));
        Assert.Throws<InvalidDataException>(() => store.Publish(source, Scope, requests, package =>
            [SemanticEntry(package, fixture) with { Source = SemanticEntry(package, fixture).Source with { Offset = 1 } }]));
        Assert.Equal(active, File.ReadAllBytes(storage.PathFor("presentation/active.json")));
        store.Publish(source, Scope, requests, package => [SemanticEntry(package, fixture)]);
        Assert.Equal(3, Directory.GetDirectories(storage.PathFor("presentation/generations")).Length);
        Assert.False(File.Exists(storage.PathFor("preferences.json")));
        Assert.False(Directory.Exists(storage.PathFor("caches")));
    }

    [Fact]
    public void PresentationGenerationRejectsTamperingAndUnregisteredSourceProfiles()
    {
        using var fixture = new Fixture();
        var storage = new TagForceSpecial.PC.Setup.PrivateStorage(Path.Combine(fixture.Root, "app-cache"));
        using var source = fixture.Open();
        var requests = new[] { new PresentationResourceRequest("title/pixel", SourcePath) };
        var defaultStore = new TagForceSpecial.PC.Import.PresentationCacheStore(storage);
        Assert.Throws<InvalidDataException>(() => defaultStore.Publish(source, Scope, requests, package => [SemanticEntry(package, fixture)]));
        var store = new TagForceSpecial.PC.Import.PresentationCacheStore(storage, [fixture.Profile]);
        store.Publish(source, Scope, requests, package => [SemanticEntry(package, fixture)]);
        string mapping = Directory.GetFiles(storage.PathFor("presentation/generations"), "mapping.json", SearchOption.AllDirectories).Single();
        File.AppendAllText(mapping, " ");
        Assert.Throws<InvalidDataException>(() => store.OpenActive());
        File.Delete(mapping);
        Assert.Throws<FileNotFoundException>(() => store.OpenActive());
    }

    [Fact]
    public void PresentationDiagnosticsSeparateMissingDecodedUnresolvedAndInvalid()
    {
        using var fixture = new Fixture(); var package = fixture.Create();
        var registry = OpenSemantic(fixture, package, SemanticEntry(package, fixture));
        var needs = new[] { new TagForceSpecial.PC.Import.AssetRequirement("title", "title.logo"), new("title", "title.absent") };
        var rows = TagForceSpecial.PC.Import.AssetDiagnostics.Inspect(registry, needs);
        Assert.Equal(new[] { "decoded-unverified", "missing" }, rows.Select(r => r.Status));
        Assert.Equal(SourcePath, rows[0].Source);
        Assert.DoesNotContain(rows, row => row.Status == "verified");
        File.Delete(Directory.GetFiles(Path.Combine(package.DirectoryPath, "blobs")).Single());
        Assert.Equal("invalid", TagForceSpecial.PC.Import.AssetDiagnostics.Inspect(registry, needs)[0].Status);
    }

    [Fact]
    public void PresentationGpuThreadGuardRejectsWorkerAccess()
    {
        var guard = new TagForceSpecial.PC.Rendering.GameThreadGuard();
        guard.Check();
        Exception? failure = null;
        var worker = new Thread(() => { failure = Record.Exception(guard.Check); });
        worker.Start(); worker.Join();
        Assert.IsType<InvalidOperationException>(failure);
    }

    private static AssetManifestEntry SemanticEntry(PresentationPackage package, Fixture fixture)
    {
        var resource = package.Resources["title/pixel"];
        return new(2, fixture.Profile.BuildId, package.PackageId, "title.logo", resource.AssetKey,
            new(resource.SourcePath, resource.MemberName, resource.Offset, resource.SourceSha256,
                resource.StoredSha256, resource.InputSha256),
            new("blobs/" + resource.ContentSha256 + ".bin", resource.ContentSha256,
                resource.Conversion == "gim-rgba-v1" ? resource.ContentSha256 : null,
                resource.Width, resource.Height, null), resource.EvidenceStatus, []);
    }

    private static SemanticAssetRegistry OpenSemantic(Fixture fixture, PresentationPackage package,
        params AssetManifestEntry[] entries)
    {
        byte[] json = SemanticAssetRegistry.Encode(entries);
        return SemanticAssetRegistry.Open(fixture.Cache, package.PackageId, fixture.Profile, Scope,
            ["title/pixel"], json, FoundationFixture.Hash(json));
    }

    [Fact]
    public void SemanticManifestResolvesExactKeysAndPreservesUnknownGeometry()
    {
        using var fixture = new Fixture(); var package = fixture.Create();
        var registry = OpenSemantic(fixture, package, SemanticEntry(package, fixture));
        var resolved = registry.Resolve("title.logo");
        Assert.Equal(new byte[] { 12, 34, 56, 78 }, resolved.Content);
        Assert.Null(resolved.Manifest.Output.SourceRectangle);
        Assert.Equal("decoded", resolved.Manifest.Status);
        Assert.Throws<KeyNotFoundException>(() => registry.Resolve("menu.main.background"));
        resolved.Content[0] = 255;
        Assert.Equal(12, registry.Resolve("title.logo").Content[0]);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("pixels")]
    [InlineData("path")]
    [InlineData("rectangle")]
    [InlineData("build")]
    [InlineData("approval")]
    [InlineData("full-art-alias")]
    [InlineData("missing-evidence")]
    public void SemanticManifestRejectsUnboundMetadataAndApproval(string fault)
    {
        using var fixture = new Fixture(); var package = fixture.Create();
        var entry = SemanticEntry(package, fixture);
        entry = fault switch
        {
            "source" => entry with { Source = entry.Source with { Offset = 1 } },
            "pixels" => entry with { Output = entry.Output with { PixelSha256 = new string('0', 64) } },
            "path" => entry with { Output = entry.Output with { RelativePath = "../image.png" } },
            "rectangle" => entry with { Output = entry.Output with { SourceRectangle = new[] { 0, 0, 256, 16 } } },
            "build" => entry with { BuildId = "other-build" },
            "approval" => entry with { Status = "reference-verified" },
            "full-art-alias" => entry with { AssetKey = "deck.card-art.full.42" },
            _ => entry with { Status = "mapped-unverified" }
        };
        Assert.Throws<InvalidDataException>(() => OpenSemantic(fixture, package, entry));
    }

    [Fact]
    public void SemanticManifestRequiresPinnedSidecarAndRejectsDuplicates()
    {
        using var fixture = new Fixture(); var package = fixture.Create();
        var entry = SemanticEntry(package, fixture);
        byte[] json = SemanticAssetRegistry.Encode([entry]);
        Assert.Throws<InvalidDataException>(() => SemanticAssetRegistry.Open(fixture.Cache,
            package.PackageId, fixture.Profile, Scope, ["title/pixel"], json, new string('0', 64)));
        Assert.Throws<InvalidDataException>(() => OpenSemantic(fixture, package, entry, entry));
    }

    [Fact]
    public void SemanticManifestChecksPackageBeforeOpenAndBlobAgainOnResolve()
    {
        using var fixture = new Fixture(); var package = fixture.Create();
        var entry = SemanticEntry(package, fixture);
        var registry = OpenSemantic(fixture, package, entry);
        File.WriteAllBytes(Path.Combine(package.DirectoryPath, entry.Output.RelativePath), new byte[4]);
        Assert.Throws<InvalidDataException>(() => registry.Resolve("title.logo"));
        Assert.Throws<InvalidDataException>(() => OpenSemantic(fixture, package, entry));
    }

    [Fact]
    public void SemanticManifestMappingRetainsEvidenceWithoutAutomaticVerification()
    {
        using var fixture = new Fixture(); var package = fixture.Create();
        var entry = SemanticEntry(package, fixture) with
        {
            Status = "mapped-unverified", EvidenceIds = new[] { "title.logo.reference-01" }
        };
        var resolved = OpenSemantic(fixture, package, entry).Resolve("title.logo");
        Assert.Equal("mapped-unverified", resolved.Manifest.Status);
        Assert.Null(OpenSemantic(fixture, package, entry).VerifiedReferenceScope("title.logo"));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)resolved.Manifest.EvidenceIds).Add("invented.approval"));
    }

    [Theory]
    [InlineData("card.art.42")]
    [InlineData("card.art.full.42")]
    public void ThumbnailCannotBePublishedAsFullArt(string key)
    {
        using var fixture = new Fixture();
        Assert.Throws<InvalidDataException>(() => fixture.Create(new PresentationResourceRequest(key, SourcePath)));
    }

    [Fact]
    public void FullArtLookupReportsMissingEvenWhenThumbnailExists()
    {
        using var fixture = new Fixture();
        var package = fixture.Create(new PresentationResourceRequest(CardArtworkProvider.ThumbnailKey(42), SourcePath));
        var provider = new CardArtworkProvider(package);
        Assert.Equal(new byte[] { 12, 34, 56, 78 }, provider.ReadThumbnail(42).Rgba);
        var error = Assert.Throws<KeyNotFoundException>(() => provider.ReadFullArt(42));
        Assert.Contains("card.art.full.42", error.Message);
        Assert.Throws<KeyNotFoundException>(() => provider.ReadThumbnail(43));
    }

    private const string SourcePath = "PSP_GAME/USRDIR/SRC.BIN";
    private const string Scope = "synthetic-title-assets";
    private static void Put(byte[] bytes, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), value);
    private static byte[] Gim()
    {
        byte[] data = new byte[132]; "MIG.00.1PSP\0\0\0\0\0"u8.CopyTo(data);
        foreach (var (at, type) in new[] { (16, 2), (32, 3), (48, 4) })
        { Put(data, at, type); Put(data, at + 4, data.Length - at); Put(data, at + 8, type == 4 ? 84 : 16); Put(data, at + 12, 16); }
        Put(data, 64, 48); Put(data, 68, 3); Put(data, 72, 0x00010001); Put(data, 76, 0x00010020); Put(data, 80, 1);
        Put(data, 88, 48); Put(data, 92, 64); Put(data, 96, 68); Put(data, 104, 0x00010001);
        Put(data, 108, 0x00010003); Put(data, 112, 64); data[128] = 12; data[129] = 34; data[130] = 56; data[131] = 78;
        return data;
    }
    private static byte[] Ehp(byte[] payload, string name = "image.gim", bool warning = false)
    {
        byte[] filename = Encoding.ASCII.GetBytes(name + "\0");
        int start = (24 + filename.Length + 4 + 15) & ~15;
        byte[] data = new byte[start + payload.Length]; "EHP\x03"u8.CopyTo(data);
        Put(data, 4, warning ? 16 : data.Length); Put(data, 8, 0x20544f4e); Put(data, 12, 1);
        Put(data, 16, 24); Put(data, 20, start); filename.CopyTo(data, 24); Put(data, 24 + filename.Length, payload.Length);
        payload.CopyTo(data, start); return data;
    }
    private static byte[] Gzip(byte[] input)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true)) gzip.Write(input);
        return output.ToArray();
    }
    private sealed class Fixture : IDisposable
    {
        private readonly FoundationFixture foundation = new();
        public string Root => foundation.Root;
        public string Iso => foundation.Iso;
        public string Cache => Path.Combine(Root, "private-presentation");
        public byte[] Input { get; }
        public GameBuildProfile Profile { get; }
        public Fixture(byte[]? input = null)
        {
            Input = input ?? Gim();
            byte[] image = File.ReadAllBytes(Iso);
            Array.Resize(ref image, Math.Max(image.Length, 23 * 2048 + Input.Length));
            FoundationFixture.Record("SRC.BIN;1"u8.ToArray(), 23, Input.Length, false).CopyTo(image, 22 * 2048);
            Input.CopyTo(image, 23 * 2048); File.WriteAllBytes(Iso, image);
            string hash = FoundationFixture.Hash(Input);
            Profile = new("synthetic-presentation-build", image.Length, FoundationFixture.Hash(image), 1,
                "synthetic-presentation-v1", "synthetic-conversion-v1", hash,
                [new(SourcePath, Input.Length, hash)],
                [new("catalog/cards.json", 1, hash), new("catalog/manifest.json", 1, hash)]);
        }
        public ValidatedGameSource Open() => GameSourceValidator.Open(Iso, Profile).Source ?? throw new Exception("Synthetic source failed validation.");
        public PresentationPackage Create(params PresentationResourceRequest[] requests)
        { using var source = Open(); return PresentationPackage.Create(source, Cache, Scope, requests.Length == 0 ? [new("title/pixel", SourcePath)] : requests); }
        public PresentationPackage Reopen(PresentationPackage package, string[]? keys = null) =>
            PresentationPackage.Open(Cache, package.PackageId, Profile, Scope, keys ?? ["title/pixel"]);
        public void Dispose() => foundation.Dispose();
    }

    [Fact]
    public void NormalizationIsDeterministicDeduplicatedReadOnlyAndSourceBound()
    {
        using var f = new Fixture(); byte[] original = File.ReadAllBytes(f.Iso);
        var a = new PresentationResourceRequest("title/a", SourcePath);
        var b = new PresentationResourceRequest("title/b", SourcePath);
        var first = f.Create(b, a); var second = f.Create(a, b);
        Assert.Equal(first.PackageId, second.PackageId);
        Assert.Equal(FoundationFixture.Hash(File.ReadAllBytes(Path.Combine(first.DirectoryPath, "manifest.json"))), first.PackageId);
        Assert.Single(Directory.GetFiles(Path.Combine(first.DirectoryPath, "blobs")));
        Assert.Equal(new byte[] { 12, 34, 56, 78 }, first.ReadResource("title/a"));
        byte[] copy = first.ReadResource("title/a"); copy[0] = 0;
        Assert.Equal(12, first.ReadResource("title/a")[0]); Assert.Equal(original, File.ReadAllBytes(f.Iso));
        Assert.Equal("decoded", first.Resources["title/a"].EvidenceStatus);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, PresentationResource>)first.Resources).Clear());
        Assert.Equal(2, f.Reopen(first, ["title/a", "title/b"]).Resources.Count);
        Assert.Empty(Directory.GetDirectories(f.Cache, ".building-*"));
    }

    [Fact]
    public void IntegrityReceiptIsExplicitRepeatableAndNeverClaimsReferenceVerification()
    {
        using var f = new Fixture(); var package = f.Create();
        string receipt = package.SignOffIntegrity(f.Profile); byte[] before = File.ReadAllBytes(receipt);
        Assert.Equal(receipt, package.SignOffIntegrity(f.Profile)); Assert.Equal(before, File.ReadAllBytes(receipt));
        using var json = JsonDocument.Parse(before);
        Assert.Equal("integrity-only;psp-fidelity-unverified;pixel-parity-unverified", json.RootElement.GetProperty("Claim").GetString());
        Assert.Equal(package.PackageId, json.RootElement.GetProperty("PackageId").GetString());
        File.WriteAllText(receipt, "{}");
        Assert.Throws<InvalidDataException>(() => package.SignOffIntegrity(f.Profile));
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("truncated")]
    [InlineData("missing")]
    [InlineData("manifest")]
    [InlineData("extra")]
    public void MutatedPackagesCannotOpenOrReceiveIntegritySignoff(string mutation)
    {
        using var f = new Fixture(); var package = f.Create();
        string blob = Directory.GetFiles(Path.Combine(package.DirectoryPath, "blobs")).Single();
        if (mutation == "corrupt") File.WriteAllBytes(blob, [13, 34, 56, 78]);
        if (mutation == "truncated") File.WriteAllBytes(blob, [12]);
        if (mutation == "missing") File.Delete(blob);
        if (mutation == "manifest") File.AppendAllText(Path.Combine(package.DirectoryPath, "manifest.json"), " ");
        if (mutation == "extra") File.WriteAllText(Path.Combine(package.DirectoryPath, "extra.bin"), "synthetic");
        void Rejected(Action action)
        {
            if (mutation == "missing") Assert.Throws<FileNotFoundException>(action);
            else Assert.Throws<InvalidDataException>(action);
        }
        Rejected(() => f.Reopen(package));
        Rejected(() => package.SignOffIntegrity(f.Profile));
        Assert.False(Directory.Exists(Path.Combine(f.Cache, "signoffs")));
        if (mutation is "corrupt" or "truncated" or "missing") Rejected(() => package.ReadResource("title/pixel"));
    }

    [Fact]
    public void TrustedRequirementsAndProfileCannotBeReplacedByPackageClaims()
    {
        using var f = new Fixture(); var package = f.Create();
        Assert.Throws<InvalidDataException>(() => f.Reopen(package, ["title/pixel", "title/missing"]));
        Assert.Throws<InvalidDataException>(() => f.Reopen(package, []));
        Assert.Throws<InvalidDataException>(() => PresentationPackage.Open(f.Cache, package.PackageId, f.Profile, "different-scope", ["title/pixel"]));
        Assert.Throws<InvalidDataException>(() => PresentationPackage.Open(f.Cache, package.PackageId, GameBuildProfile.English21, Scope, ["title/pixel"]));
        Assert.Throws<InvalidDataException>(() => PresentationPackage.Open(f.Cache, "../manifest", f.Profile, Scope, ["title/pixel"]));
    }

    [Fact]
    public void MissingOrUnsupportedResourcesLeaveNoPublishedPackage()
    {
        using var f = new Fixture();
        Assert.Throws<InvalidDataException>(() => f.Create(new PresentationResourceRequest("title/pixel", "PSP_GAME/USRDIR/MISSING.GIM")));
        using var malformed = new Fixture([0, 1, 2]);
        Assert.Throws<InvalidDataException>(() => malformed.Create());
        foreach (string root in new[] { f.Cache, malformed.Cache })
        { Assert.Empty(Directory.GetDirectories(root, ".building-*")); Assert.False(Directory.Exists(Path.Combine(root, "packages"))); }
    }

    [Theory]
    [InlineData("../pixel")]
    [InlineData("TITLE/pixel")]
    [InlineData("/pixel")]
    [InlineData("title\\pixel")]
    public void InvalidKeysAndDuplicatesAreRejected(string key)
    {
        using var f = new Fixture();
        Assert.Throws<InvalidDataException>(() => f.Create(new PresentationResourceRequest(key, SourcePath)));
        Assert.Throws<InvalidDataException>(() => f.Create(new PresentationResourceRequest("title/pixel", SourcePath), new("title/pixel", SourcePath)));
    }

    [Fact]
    public void GitWorktreeOutputsAreRejectedBeforeWritingMedia()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(Path.Combine(f.Cache, ".git"));
        using var source = f.Open();
        Assert.Throws<InvalidDataException>(() => PresentationPackage.Create(source, f.Cache, Scope, [new("title/pixel", SourcePath)]));
        Assert.False(Directory.Exists(Path.Combine(f.Cache, "packages")));
    }

    [Fact]
    public void EhpWarningsAreRetainedAndInspectionMustBeRequested()
    {
        using var f = new Fixture(Ehp(Gim(), warning: true));
        Assert.Throws<InvalidDataException>(() => f.Create(new PresentationResourceRequest("title/pixel", SourcePath, "image.gim")));
        var package = f.Create(new PresentationResourceRequest("title/pixel", SourcePath, "image.gim", ArchiveMode: EhpReadMode.InspectStoredRanges));
        var resource = package.Resources["title/pixel"];
        Assert.Contains(resource.Diagnostics, d => d.Contains("Declared length"));
        Assert.Equal(FoundationFixture.Hash(f.Input), resource.SourceSha256);
        Assert.Equal(FoundationFixture.Hash(Gim()), resource.StoredSha256);
        Assert.Equal(new byte[] { 12, 34, 56, 78 }, package.ReadResource("title/pixel"));
        Assert.True(File.Exists(package.SignOffIntegrity(f.Profile))); // Byte integrity only, warning is not erased.
    }

    [Fact]
    public void CompressedGimHasIndependentStoredAndInflatedIdentitiesWhileStoredModeIsUnchanged()
    {
        byte[] gzip = Gzip(Gim());
        using var f = new Fixture(Ehp(gzip, "image.gim.gz"));
        var package = f.Create(new PresentationResourceRequest("title/pixel", SourcePath, "image.gim.gz"),
            new("research/stored", SourcePath, "image.gim.gz", PresentationConversion.Stored));
        var resource = package.Resources["title/pixel"];
        Assert.Equal(FoundationFixture.Hash(gzip), resource.StoredSha256);
        Assert.Equal(FoundationFixture.Hash(Gim()), resource.InputSha256);
        Assert.Equal(gzip, package.ReadResource("research/stored"));
        Assert.Equal("stored", package.Resources["research/stored"].EvidenceStatus);
    }

    [Fact]
    public void InflationBeyondTheResourceLimitCannotPublishAPackage()
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            byte[] zeros = new byte[65536];
            for (int i = 0; i <= PresentationPackage.MaximumResourceBytes / zeros.Length; i++) gzip.Write(zeros);
        }
        using var f = new Fixture(Ehp(compressed.ToArray(), "image.gim.gz"));
        var error = Assert.Throws<InvalidDataException>(() => f.Create(new PresentationResourceRequest("title/pixel", SourcePath, "image.gim.gz")));
        Assert.Contains("Inflated resource exceeds", error.Message);
        Assert.Empty(Directory.GetDirectories(f.Cache, ".building-*"));
        Assert.False(Directory.Exists(Path.Combine(f.Cache, "packages")));
    }

    [Fact]
    public void DisposedValidationHandleCannotBeUsedInsteadOfALiveValidatedSource()
    {
        using var f = new Fixture(); using var source = f.Open();
        source.Dispose(); // No caller-provided manifest can stand in for a live validated source.
        Assert.Throws<ObjectDisposedException>(() => PresentationPackage.Create(source, f.Cache, Scope, [new("title/pixel", SourcePath)]));
        Assert.False(Directory.Exists(f.Cache));
    }
}
