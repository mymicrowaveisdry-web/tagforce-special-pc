using System.Security.Cryptography;
using System.Text.Json;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

public sealed class ResearchValidationTests
{
    private static readonly string SourceHash = new('a', 64);
    private static readonly string EvidenceHash = new('b', 64);
    private static ModuleInventory[] Inventories =>
    [
        new(new("synthetic.prx", "overlay", "config/synthetic.csv", 100, SourceHash, EvidenceHash),
            [new(0x1000, 16), new(0x1008, 16), new(0x2000, 8)])
    ];
    private static ResearchEvidence Evidence => new("config/source.json", EvidenceHash, "committed", "static-analysis");
    private static ResearchFunction Function => new("getter", "synthetic.prx", SourceHash, "0x00001000", 16,
        "synthetic", "game", "corroborated", "classified", [Evidence], []);
    private static ResearchQueue Queue(params ResearchFunction[] functions) => new(1, functions, []);

    [Fact]
    public void QueueRejectsOverlapsWithoutChangingCandidateInventories()
    {
        ResearchValidation.ValidateQueue(Queue(Function), Inventories);
        Assert.Equal(3, Inventories[0].Functions.Count);
        var overlapping = Function with { Id = "alternate", Address = "0x00001008" };
        Assert.Throws<InvalidDataException>(() => ResearchValidation.ValidateQueue(Queue(Function, overlapping), Inventories));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("length")]
    [InlineData("classification")]
    [InlineData("boundary")]
    [InlineData("provenance")]
    [InlineData("status")]
    public void QueueFailsClosedForUnsupportedPromotionsAndSourceIdentity(string mutation)
    {
        var function = mutation switch
        {
            "hash" => Function with { SourceSha256 = EvidenceHash },
            "length" => Function with { Length = 12 },
            "classification" => Function with { Classification = "unknown" },
            "boundary" => Function with { BoundaryConfidence = "candidate" },
            "provenance" => Function with { Provenance = [] },
            _ => Function with { Status = "guessed-match" }
        };
        Assert.Throws<InvalidDataException>(() => ResearchValidation.ValidateQueue(Queue(function), Inventories));
    }

    [Fact]
    public void DependenciesMustExistAndRemainAcyclic()
    {
        var other = Function with { Id = "lookup", Address = "0x00002000", Length = 8, Dependencies = ["getter"] };
        var getter = Function with { Dependencies = ["lookup"] };
        Assert.Throws<InvalidDataException>(() => ResearchValidation.ValidateQueue(Queue(getter), Inventories));
        Assert.Throws<InvalidDataException>(() => ResearchValidation.ValidateQueue(Queue(getter, other), Inventories));
        var units = new[]
        {
            new AgentWorkUnit("A", "analysis", "getter", ["B"], ["native/target.json"], "inspect"),
            new AgentWorkUnit("B", "native", "getter", ["A"], ["native/trial.c"], "validate")
        };
        Assert.Throws<InvalidDataException>(() => ResearchValidation.ValidateQueue(new(1, [Function], units), Inventories));
    }

    [Fact]
    public void UnknownCoverageCannotBecomeAUnitTestPercentage()
    {
        var scope = new CoverageScope("acceptance", "Acceptance", "unscoped-v1", null, "No denominator.", []);
        ResearchValidation.ValidateCoverage(new(1, [scope]), Inventories);
        Assert.StartsWith("N/A", ResearchValidation.FormatCoverage(scope));
        Assert.Throws<InvalidDataException>(() => ResearchValidation.ValidateCoverage(new(1, [scope with { Denominator = 112 }]), Inventories));
        Assert.Throws<InvalidDataException>(() => ResearchValidation.ValidateCoverage(new(1, [scope with { Denominator = 0 }]), Inventories));
    }

    [Fact]
    public void DefinedCoverageRequiresEnumeratedCriteriaAndTraceableAcceptance()
    {
        var criterion = new CoverageCriterion("attack", "accepted", "synthetic.prx", SourceHash,
            [Evidence with { Kind = "fixture" }, Evidence with { Kind = "test" }]);
        var scope = new CoverageScope("reference", "Reference", "bounded-v1", 2, "Explicit bounded scope.",
            [criterion, criterion with { Id = "defense", Status = "pending", Evidence = [] }]);
        ResearchValidation.ValidateCoverage(new(1, [scope]), Inventories);
        Assert.Equal("1 / 2 (50.00%; bounded-v1)", ResearchValidation.FormatCoverage(scope));
        Assert.Throws<InvalidDataException>(() => ResearchValidation.ValidateCoverage(new(1,
            [scope with { Criteria = [criterion with { Evidence = [Evidence] }, scope.Criteria[1]] }]), Inventories));
        Assert.Throws<InvalidDataException>(() => ResearchValidation.ValidateCoverage(new(1,
            [scope with { Criteria = [criterion with { SourceSha256 = EvidenceHash }, scope.Criteria[1]] }]), Inventories));
    }

    [Theory]
    [InlineData("C:/private/file.json")]
    [InlineData("/private/file.json")]
    [InlineData("../escape.json")]
    [InlineData("config/../escape.json")]
    [InlineData("config\\private.json")]
    public void ProvenanceCannotEscapeOrDisclosePrivateAbsolutePaths(string path) =>
        Assert.Throws<InvalidDataException>(() => ResearchValidation.ValidateEvidence(Evidence with { Path = path }));

    [Fact]
    public void JsonContractRejectsMissingUnknownAndMistypedFields()
    {
        Assert.Throws<JsonException>(() => ResearchValidation.Read<ResearchQueue>("{}"));
        Assert.Throws<JsonException>(() => ResearchValidation.Read<ResearchQueue>("""{"schemaVersion":1,"functions":[],"workUnits":[],"matchedBytes":100}"""));
        Assert.Throws<JsonException>(() => ResearchValidation.Read<ResearchQueue>("""{"schemaVersion":"1","functions":[],"workUnits":[]}"""));
        Assert.Throws<JsonException>(() => ResearchValidation.Read<ResearchQueue>("""{"schemaVersion":1,"schemaVersion":1,"functions":[],"workUnits":[]}"""));
    }

    [Fact]
    public void EvidenceValidationRehashesFilesAndDistinguishesPrivateAvailability()
    {
        string root = Path.Combine(Path.GetTempPath(), "tagforce-evidence-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var local = Evidence with { Path = "capture.json", Availability = "local" };
            ResearchValidation.ValidateEvidenceFile(root, local, false);
            Assert.Throws<InvalidDataException>(() => ResearchValidation.ValidateEvidenceFile(root, local, true));
            File.WriteAllText(Path.Combine(root, local.Path), "captured value");
            var valid = local with { Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, local.Path)))) };
            ResearchValidation.ValidateEvidenceFile(root, valid, true);
            File.WriteAllText(Path.Combine(root, local.Path), "changed value");
            Assert.Throws<InvalidDataException>(() => ResearchValidation.ValidateEvidenceFile(root, valid, true));
        }
        finally
        {
            File.Delete(Path.Combine(root, "capture.json"));
            Directory.Delete(root);
        }
    }
}
