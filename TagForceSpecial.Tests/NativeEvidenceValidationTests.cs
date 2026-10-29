using System.Security.Cryptography;
using System.Text.Json;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

/// <summary>Synthetic metadata fixtures only. These tests never reproduce PSP bytes or award actual progress.</summary>
public sealed class NativeEvidenceValidationTests
{
    [Theory]
    [InlineData("self-review")]
    [InlineData("blocked")]
    [InlineData("changed-source")]
    [InlineData("hashes-only")]
    [InlineData("wrong-length")]
    [InlineData("unrelated-source")]
    [InlineData("valid-metadata")]
    public void MatchingDigestsAloneCannotPassNativeEvidenceGate(string mutation)
    {
        string root = Path.Combine(Path.GetTempPath(), "tagforce-review-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "config/match-evidence"));
        Directory.CreateDirectory(Path.Combine(root, "native"));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        try
        {
            string moduleHash = new('a', 64), codeHash = new('b', 64);
            string Write(string path, object value)
            {
                File.WriteAllText(Path.Combine(root, path), JsonSerializer.Serialize(value, options));
                return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, path))));
            }
            var source = new ResearchEvidence("native/synthetic.c", Write("native/synthetic.c", "synthetic source"), "committed", "source");
            var summary = new NativeComparisonSummary(1, mutation == "blocked" ? "blocked" : "compared", "synthetic.prx", "0x00001000",
                moduleHash, source.Sha256, codeHash, codeHash, 16, mutation == "wrong-length" ? 12 : 16, 0, "synthetic test compiler", ["-O2"], codeHash);
            var comparison = new ResearchEvidence("config/comparison.json", Write("config/comparison.json", summary), "committed", "comparison");
            var review = new NativeEvidenceReview(1, "synthetic.prx", "0x00001000", 16, moduleHash, codeHash, codeHash,
                "B", mutation == "self-review" ? "B" : "A", "accepted", "independent rebuild and byte validation", source, comparison);
            string evidence = mutation == "hashes-only" ? Write("config/match-evidence/synthetic.json", new { originalSha256 = codeHash, rebuiltSha256 = codeHash })
                : Write("config/match-evidence/synthetic.json", review);
            var match = new NativeFunctionMatch("synthetic.prx", "0x00001000", 16, "game-c", moduleHash, codeHash, codeHash,
                "config/match-evidence/synthetic.json", evidence);
            Write("config/native-matches.json", new NativeMatchManifest(1, [match]));
            var function = new ResearchFunction("getter", "synthetic.prx", moduleHash, "0x00001000", 16, "synthetic", "game",
                "corroborated", "psp-byte-matched", [source], []);
            Write("config/research-queue.json", new ResearchQueue(1, [function],
            [
                new("A", "analysis", "getter", [], ["native/synthetic.c"], "inspect"),
                new("B", "native", "getter", ["A"], [mutation == "unrelated-source" ? "native/unrelated.c" : "native/synthetic.c"], "validate")
            ]));
            ModuleInventory[] inventories = [new(new("synthetic.prx", "overlay", "config/test.csv", 100, moduleHash, codeHash), [new(0x1000, 16)])];
            if (mutation == "changed-source") File.WriteAllText(Path.Combine(root, source.Path), "tampered");
            if (mutation == "valid-metadata") Assert.Equal(16, NativeByteProgress.Load(root, inventories).MatchedBytes);
            else if (mutation == "hashes-only") Assert.Throws<JsonException>(() => NativeByteProgress.Load(root, inventories));
            else Assert.Throws<InvalidDataException>(() => NativeByteProgress.Load(root, inventories));
        }
        finally
        {
            foreach (string path in new[] { "native/synthetic.c", "config/comparison.json", "config/match-evidence/synthetic.json", "config/native-matches.json", "config/research-queue.json" })
                File.Delete(Path.Combine(root, path));
            Directory.Delete(Path.Combine(root, "config/match-evidence"));
            Directory.Delete(Path.Combine(root, "config"));
            Directory.Delete(Path.Combine(root, "native"));
            Directory.Delete(root);
        }
    }
}
