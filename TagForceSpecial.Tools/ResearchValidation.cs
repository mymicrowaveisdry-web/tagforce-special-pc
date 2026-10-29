using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TagForceSpecial.Tools;

public sealed record ResearchEvidence(string Path, string Sha256, string Availability, string Kind);
public sealed record ResearchFunction(string Id, string Module, string SourceSha256, string Address, int Length,
    string Subsystem, string Classification, string BoundaryConfidence, string Status,
    ResearchEvidence[] Provenance, string[] Dependencies);
public sealed record AgentWorkUnit(string Id, string Role, string FunctionId, string[] DependsOn,
    string[] Outputs, string ValidationCommand);
public sealed record ResearchQueue(int SchemaVersion, ResearchFunction[] Functions, AgentWorkUnit[] WorkUnits);
public sealed record CoverageCriterion(string Id, string Status, string Module, string SourceSha256, ResearchEvidence[] Evidence);
public sealed record CoverageScope(string Id, string Label, string ScopeVersion, int? Denominator, string Reason,
    CoverageCriterion[] Criteria);
public sealed record CoverageManifest(int SchemaVersion, CoverageScope[] Scopes);

/// <summary>Deterministic metadata checks; local mode also rehashes private evidence. Never awards native bytes.</summary>
public static class ResearchValidation
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict
    };

    public static T Read<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        RejectDuplicateProperties(document.RootElement);
        return JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("Missing research document.");
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>();
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException("Duplicate research property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length > 1 || args.Length == 1 && args[0] != "--local")
        {
            error.WriteLine("Usage: research-check [--local]");
            return 2;
        }
        try
        {
            string root = Directory.GetCurrentDirectory();
            var config = Read<ProgressConfiguration>(File.ReadAllText(Path.Combine(root, "config/progress.json")));
            var inventories = config.Modules.Select(m => new ModuleInventory(m,
                ProjectProgress.ReadFunctions(File.ReadAllText(Path.Combine(root, m.Registry))))).ToArray();
            var queue = Read<ResearchQueue>(File.ReadAllText(Path.Combine(root, "config/research-queue.json")));
            var coverage = Read<CoverageManifest>(File.ReadAllText(Path.Combine(root, "config/coverage.json")));
            ValidateQueue(queue, inventories);
            ValidateCoverage(coverage, inventories);
            NativeByteProgress.Load(root, inventories);
            var matches = Read<NativeMatchManifest>(File.ReadAllText(Path.Combine(root, NativeByteProgress.ManifestPath)));
            if (queue.Functions.Any(f => f.Status == "psp-byte-matched" && !matches.Matches.Any(m =>
                m.Module == f.Module && m.Address == f.Address && m.Length == f.Length)))
                throw new InvalidDataException("Queue status cannot self-certify a native match.");
            foreach (var evidence in queue.Functions.SelectMany(f => f.Provenance)
                .Concat(coverage.Scopes.SelectMany(s => s.Criteria).SelectMany(c => c.Evidence)))
                ValidateEvidenceFile(root, evidence, args.Length == 1);
            foreach (var unit in queue.WorkUnits)
                foreach (string path in unit.Outputs)
                    if (!File.Exists(Path.Combine(root, path))) throw new InvalidDataException($"Missing work-unit output: {path}");
            output.WriteLine($"Research metadata valid: {queue.Functions.Length} nonoverlapping queued functions; {queue.WorkUnits.Length} work units.");
            foreach (var scope in coverage.Scopes) output.WriteLine($"{scope.Label}: {FormatCoverage(scope)}");
            output.WriteLine(args.Length == 1 ? "Private evidence hashes rechecked locally." : "Private evidence is not replayed by hosted metadata validation.");
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            error.WriteLine($"Research validation error: {e.Message}");
            return 1;
        }
    }

    public static void ValidateQueue(ResearchQueue queue, IReadOnlyList<ModuleInventory> inventories)
    {
        if (queue.SchemaVersion != 1 || queue.Functions is null || queue.WorkUnits is null)
            throw new InvalidDataException("Unsupported research queue schema.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var ranges = new Dictionary<string, List<(ulong Start, ulong End)>>();
        foreach (var function in queue.Functions)
        {
            if (function is null || !Identifier(function.Id) || !ids.Add(function.Id) ||
                string.IsNullOrWhiteSpace(function.Subsystem) ||
                function.Classification is not ("unknown" or "game" or "crt" or "sdk") ||
                function.BoundaryConfidence is not ("candidate" or "corroborated") ||
                function.Status is not ("discovered" or "classified" or "under-investigation" or "implemented" or
                    "behavior-verified" or "comparison-blocked" or "comparison-differs" or "psp-byte-matched") ||
                function.Provenance is not { Length: > 0 } || function.Dependencies is null)
                throw new InvalidDataException("Invalid function research record.");
            var inventory = Module(function.Module, function.SourceSha256, inventories);
            if (!Address(function.Address, out uint address) || address % 4 != 0 || function.Length <= 0 || function.Length % 4 != 0 ||
                (ulong)address + (ulong)function.Length > (ulong)uint.MaxValue + 1 ||
                !inventory.Functions.Any(f => f.Address == address && f.BodyBytes == function.Length))
                throw new InvalidDataException("Queue entry must cover a complete inventoried candidate.");
            var range = ranges.GetValueOrDefault(function.Module);
            if (range is null) ranges[function.Module] = range = [];
            ulong end = (ulong)address + (ulong)function.Length;
            if (range.Any(r => address < r.End && r.Start < end))
                throw new InvalidDataException("Overlapping queued functions; preserve alternate candidates in the CSV only.");
            range.Add((address, end));
            if (function.Status != "discovered" && (function.Classification == "unknown" || function.BoundaryConfidence != "corroborated"))
                throw new InvalidDataException("Promotion needs classification and corroborated boundaries.");
            foreach (var evidence in function.Provenance) ValidateEvidence(evidence);
        }
        foreach (var function in queue.Functions)
            if (function.Dependencies.Distinct().Count() != function.Dependencies.Length ||
                function.Dependencies.Any(d => d == function.Id || !ids.Contains(d)))
                throw new InvalidDataException("Missing, duplicate or self-referencing function dependency.");
        CheckCycles(queue.Functions.ToDictionary(f => f.Id, f => f.Dependencies));
        var units = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in queue.WorkUnits)
        {
            if (unit is null || !Identifier(unit.Id) || !units.Add(unit.Id) || !ids.Contains(unit.FunctionId) ||
                unit.Role is not ("analysis" or "native" or "behavior") || unit.DependsOn is null ||
                unit.Outputs is not { Length: > 0 } || string.IsNullOrWhiteSpace(unit.ValidationCommand) ||
                unit.Outputs.Any(p => !RelativePath(p))) throw new InvalidDataException("Invalid agent work unit.");
        }
        if (queue.WorkUnits.Any(u => u.DependsOn.Distinct().Count() != u.DependsOn.Length ||
            u.DependsOn.Any(d => d == u.Id || !units.Contains(d)))) throw new InvalidDataException("Invalid work-unit dependency.");
        CheckCycles(queue.WorkUnits.ToDictionary(u => u.Id, u => u.DependsOn));
    }

    public static void ValidateCoverage(CoverageManifest coverage, IReadOnlyList<ModuleInventory> inventories)
    {
        if (coverage.SchemaVersion != 1 || coverage.Scopes is not { Length: > 0 })
            throw new InvalidDataException("Unsupported coverage schema.");
        var scopeIds = new HashSet<string>();
        foreach (var scope in coverage.Scopes)
        {
            if (scope is null || !Identifier(scope.Id) || !scopeIds.Add(scope.Id) ||
                string.IsNullOrWhiteSpace(scope.Label) || !Identifier(scope.ScopeVersion) ||
                string.IsNullOrWhiteSpace(scope.Reason) || scope.Criteria is null ||
                scope.Denominator is not null && (scope.Denominator <= 0 || scope.Denominator != scope.Criteria.Length))
                throw new InvalidDataException("Coverage requires an explicit versioned scope; unknown denominators must be null.");
            var ids = new HashSet<string>();
            foreach (var criterion in scope.Criteria)
            {
                if (criterion is null || !Identifier(criterion.Id) || !ids.Add(criterion.Id) ||
                    criterion.Status is not ("pending" or "accepted" or "historical") || criterion.Evidence is null ||
                    criterion.Status != "pending" && criterion.Evidence.Length == 0)
                    throw new InvalidDataException("Invalid coverage criterion/evidence.");
                Module(criterion.Module, criterion.SourceSha256, inventories);
                foreach (var evidence in criterion.Evidence) ValidateEvidence(evidence);
                if (criterion.Status == "accepted" &&
                    (!criterion.Evidence.Any(e => e.Availability == "committed" && e.Kind == "fixture") ||
                     !criterion.Evidence.Any(e => e.Availability == "committed" && e.Kind == "test")))
                    throw new InvalidDataException("Acceptance requires a committed reference fixture and test provenance.");
            }
        }
    }

    public static string FormatCoverage(CoverageScope scope) => scope.Denominator is null ? "N/A (denominator not defined)" :
        $"{scope.Criteria.Count(c => c.Status == "accepted")} / {scope.Denominator} ({NativeByteProgress.Percent(scope.Criteria.Count(c => c.Status == "accepted"), scope.Denominator.Value)}; {scope.ScopeVersion})";

    public static void ValidateEvidence(ResearchEvidence evidence)
    {
        if (evidence is null || !RelativePath(evidence.Path) || !Sha(evidence.Sha256) ||
            evidence.Availability is not ("committed" or "local") ||
            evidence.Kind is not ("static-analysis" or "runtime-capture" or "fixture" or "test" or "comparison" or "source"))
            throw new InvalidDataException("Invalid evidence provenance; use relative paths and SHA-256.");
    }

    public static void ValidateEvidenceFile(string root, ResearchEvidence evidence, bool local)
    {
        ValidateEvidence(evidence);
        if (evidence.Availability == "local" && !local) return;
        string file = Path.Combine(root, evidence.Path);
        if (!File.Exists(file) || !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).Equals(evidence.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Missing or changed evidence: {evidence.Path}");
    }

    public static bool RelativePath(string? path) => !string.IsNullOrWhiteSpace(path) && !path.Contains('\\') &&
        !path.Contains(':') && !Path.IsPathRooted(path) && path.Split('/').All(p => p is not ("" or "." or ".."));
    public static bool Sha(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool Identifier(string? value) => !string.IsNullOrWhiteSpace(value) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static bool Address(string? value, out uint address)
    {
        address = 0;
        return value is { Length: 10 } && value.StartsWith("0x", StringComparison.Ordinal) &&
            uint.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }
    private static ModuleInventory Module(string name, string hash, IReadOnlyList<ModuleInventory> inventories)
    {
        var inventory = inventories.SingleOrDefault(i => i.Module.Name == name);
        if (inventory is null || !Sha(hash) || !hash.Equals(inventory.Module.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unknown module or source hash mismatch.");
        return inventory;
    }
    private static void CheckCycles(Dictionary<string, string[]> graph)
    {
        var done = new HashSet<string>();
        var active = new HashSet<string>();
        void Visit(string id)
        {
            if (done.Contains(id)) return;
            if (!active.Add(id)) throw new InvalidDataException("Cyclic research dependencies.");
            foreach (string dependency in graph[id]) Visit(dependency);
            active.Remove(id);
            done.Add(id);
        }
        foreach (string id in graph.Keys) Visit(id);
    }
}
