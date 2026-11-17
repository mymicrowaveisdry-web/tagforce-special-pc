using System.Text.Json;
using TagForceSpecial.Import;
using TagForceSpecial.Import.Inspection;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Tests;

public sealed class ImportInspectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tfsp-inspection-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public void ComparisonReportsIdentityAndValidationChangesWithoutInventingResources()
    {
        var storage = new PrivateStorage(root);
        var before = Manifest(Guid.NewGuid().ToString("N"));
        var after = Manifest(Guid.NewGuid().ToString("N")) with { DecoderSha256 = new string('b', 64), Status = ImportStatus.Partial };
        Report(before, CheckStatus.Passed); Report(after, CheckStatus.Failed);
        var changes = ImportComparison.Compare(storage, before, after);
        Assert.Contains(changes, c => c.Kind == "metadata" && c.Resource == "decoder");
        Assert.Contains(changes, c => c.Kind == "validation" && c.Before!.StartsWith("Passed") && c.After!.StartsWith("Failed"));
        Assert.DoesNotContain(changes, c => c.Kind == "original-file");
        Assert.Empty(ImportComparison.Compare(storage, before, before));
        void Report(ImportManifest m, CheckStatus status) => storage.WriteBytes(ImportLayout.SessionValidation(m.SessionId),
            ManifestJson.Serialize(new ValidationReport(1, m.SessionId, DateTimeOffset.UtcNow, "synthetic", m.Source,
                [new("check", CheckCategory.Source, CheckSeverity.Info, status, "source", "synthetic check")])));
    }
    [Fact]
    public void TamperedActiveManifestIsRejectedBeforeCleanup()
    {
        var storage = new PrivateStorage(root); var manifest = Manifest(Guid.NewGuid().ToString("N"));
        storage.WriteBytes(ImportLayout.SessionManifest(manifest.SessionId), ManifestJson.Serialize(manifest));
        storage.Write(ImportLayout.Active, new ActiveImport(1, manifest.SessionId, new string('0', 64)));
        Directory.CreateDirectory(storage.PathFor("import/stages"));
        Assert.Throws<InvalidDataException>(() => TagForceSpecial.Import.Installed.InstalledGameReader.ReadActiveManifest(storage));
        Assert.Throws<InvalidDataException>(() => TagForceSpecial.Import.Maintenance.Cleanup.PlanUnusedStages(storage));
    }
    [Fact]
    public void CandidateInventoryDoesNotInventReconstructionOrNativeCoverage()
    {
        Directory.CreateDirectory(Path.Combine(root, "config/npjh00142"));
        File.WriteAllText(Path.Combine(root, "config/npjh00142/functions.csv"), "address,body_bytes\n0x08804000,16\n");
        var evidence = DecompilationEvidence.Load(root);
        Assert.Equal("Discovered", Assert.Single(evidence.Functions).Status);
        Assert.Contains(evidence.Metrics, m => m.StartsWith("Native byte matching: Not measured"));
        Assert.Contains(evidence.Metrics, m => m.StartsWith("C# behavioral parity: Not measured"));
    }
    private static ImportManifest Manifest(string id) => new(1, id, "synthetic", new string('a', 64), DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch.AddSeconds(1), ImportStatus.Succeeded,
        new("iso", "synthetic.iso", "synthetic", 128, null, null, null, null, false, false, false, null, null, []), new(), [], new(null, null, null, null, null), 0, 0);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
