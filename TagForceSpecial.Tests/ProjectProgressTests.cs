using System.Globalization;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

public sealed class ProjectProgressTests
{
    private static ProgressConfiguration Config => new(1, "2026-10-16", [],
        new(10, 2, 6, 932344, 16, "Partial", []), new(82, 82, "2026-10-16"), new("Stage 5", ["Draw"], ["Battle"]));
    private static ModuleInventory[] Modules => [new(new("test.prx", "overlay", "config/test.csv", 100,
        new string('0', 64), new string('1', 64)), [new(0x8804000, 80), new(0x8804020, 80)])];

    [Fact]
    public void OverlappingCandidateSizesAndAssetBytesAreNotCalledMatchedCode()
    {
        string report = ProjectProgress.Render(Config, Modules);
        Assert.Contains("0 / 100", report);
        Assert.Contains("| `test.prx` | 2 |", report);
        Assert.Contains("not a measured whole-game scope", report);
        Assert.DoesNotContain("160 (0xA0)", report);
        Assert.Contains("932,344", report);
        Assert.Contains("asset bytes", report);
        Assert.Contains("0.00%", report);
    }

    [Fact]
    public void RegistryRejectsDuplicatesBadSizesAndClaimedMatchColumns()
    {
        Assert.Equal(2, ProjectProgress.ReadFunctions("address,body_bytes\n0x1000,8\n0x2000,12\n").Count);
        foreach (string csv in new[] { "address,body_bytes\n0x1000,8\n0x1000,12", "address,body_bytes\n0x1000,0",
            "address,body_bytes,matched\n0x1000,8,true", "address,body_bytes\nnot-an-address,8" })
            Assert.Throws<InvalidDataException>(() => ProjectProgress.ReadFunctions(csv));
    }

    [Fact]
    public void UpdatingMarkersPreservesHandwrittenTextAndIsIdempotent()
    {
        string input = "# Intro\r\n\r\n" + ProjectProgress.StartMarker + "\r\nold\r\n" + ProjectProgress.EndMarker + "\r\nKeep this.\r\n";
        string updated = ProjectProgress.ReplaceProgress(input, "new report\n");
        Assert.StartsWith("# Intro\n\n", updated);
        Assert.EndsWith("\nKeep this.\n", updated);
        Assert.Equal(updated, ProjectProgress.ReplaceProgress(updated, "new report\n"));
        Assert.Throws<InvalidDataException>(() => ProjectProgress.ReplaceProgress("No markers", "new"));
        Assert.Throws<InvalidDataException>(() => ProjectProgress.ReplaceProgress(updated + ProjectProgress.StartMarker, "new"));
    }

    [Fact]
    public void TrxCountersRemainPartialAndDatesUseBerlinTime()
    {
        string trx = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Times finish="2026-10-15T23:30:00Z" />
              <ResultSummary><Counters total="3" passed="2" /></ResultSummary>
            </TestRun>
            """;
        Assert.Equal(new ProgressValidation(2, 3, "2026-10-16"), ProjectProgress.ReadValidation(trx));
        Assert.Throws<InvalidDataException>(() => ProjectProgress.ReadValidation(trx.Replace("passed=\"2\"", "passed=\"4\"")));
        Assert.Throws<InvalidDataException>(() => ProjectProgress.ReadValidation("<TestRun />"));
    }

    [Fact]
    public void RenderingIsIndependentOfMachineNumberCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Contains("932,344", ProjectProgress.Render(Config, Modules));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
