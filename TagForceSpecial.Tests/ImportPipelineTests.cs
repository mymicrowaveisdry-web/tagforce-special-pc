using TagForceSpecial.Import;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Sources;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Tests;

public sealed class ImportPipelineTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tfsp-import-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public void UnreadableSourceCannotReplaceAnExistingActivePointer()
    {
        var storage = new PrivateStorage(root); byte[] pointer = "existing-pointer"u8.ToArray();
        storage.WriteBytes(ImportLayout.Active, pointer);
        var manifest = ImportPipeline.Run(new(Path.Combine(root, "missing.iso"), root, new()));
        Assert.Equal(ImportStatus.Failed, manifest.Status);
        Assert.Contains(manifest.Stages, s => s.Id == "source" && s.Status == StageStatus.Failed);
        Assert.Equal(pointer, storage.ReadBytes(ImportLayout.Active, 1024));
    }
    [Fact]
    public void CancellationDoesNotPublishPartialArtifacts()
    {
        using var fixture = new FoundationFixture();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var manifest = ImportPipeline.Run(new(fixture.Iso, root, new(), [fixture.Profile()]), cancellation: cancellation.Token);
        Assert.Equal(ImportStatus.Cancelled, manifest.Status);
        Assert.False(File.Exists(new PrivateStorage(root).PathFor(ImportLayout.Active)));
        Assert.DoesNotContain(manifest.Stages, s => s.Id == "publish" && s.Status == StageStatus.Succeeded);
    }
    [Fact]
    public void DestinationInsideExtractedSourceIsRejectedBeforeWriting()
    {
        Directory.CreateDirectory(root);
        string destination = Path.Combine(root, "derived");
        Assert.Throws<InvalidOperationException>(() => ImportPipeline.Run(new(root, destination, new())));
        Assert.False(Directory.Exists(destination));
    }
    [Fact]
    public void StageReuseRejectsTamperedOutputAndRetainsItWhenRebuilt()
    {
        var cache = new StageCache(new PrivateStorage(root)); string key = StageCache.Key("synthetic");
        string directory = cache.Publish("catalog", key, path => File.WriteAllText(Path.Combine(path, "data.bin"), "original"), default);
        var expected = StageCache.Inventory(directory);
        Assert.True(cache.TryReuse("catalog", key, path => StageCache.Matches(path, expected)));
        File.WriteAllText(Path.Combine(directory, "data.bin"), "corrupted");
        Assert.False(cache.TryReuse("catalog", key, path => StageCache.Matches(path, expected)));
        cache.Publish("catalog", key, path => File.WriteAllText(Path.Combine(path, "data.bin"), "original"), default);
        Assert.True(StageCache.Matches(directory, expected));
        string rejected = Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(directory)!, ".rejected-*"));
        Assert.Equal("corrupted", File.ReadAllText(Path.Combine(rejected, "data.bin")));
    }
    [Theory]
    [InlineData("../outside")]
    [InlineData("import/stages/../../outside")]
    [InlineData("C:/outside")]
    public void PrivatePathsCannotEscapeTheDataRoot(string path) => Assert.Throws<InvalidDataException>(() => new PrivateStorage(root).PathFor(path));

    [Fact]
    public void MetadataProbeDoesNotTreatAFileNameAsSourceRecognition()
    {
        Directory.CreateDirectory(root); string path = Path.Combine(root, "Tag Force Special.iso"); File.WriteAllBytes(path, new byte[128]);
        var probe = SourceProbe.Probe(path); Assert.False(probe.Recognized); Assert.False(probe.Container);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
