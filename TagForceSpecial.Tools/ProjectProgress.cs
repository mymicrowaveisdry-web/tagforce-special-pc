using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace TagForceSpecial.Tools;

public sealed record ProgressModule(string Name, string Kind, string Registry, long ExecutableBytes,
    string SourceSha256, string InventoryEvidenceSha256);
public sealed record ResearchProgress(int DecodedCards, int RuntimeGetterSamples, int LoadedTables,
    long LoadedTableBytes, int CardhSprites, string VisualAcceptance, Dictionary<string, string> EvidenceSha256);
public sealed record ImplementationProgress(string Milestone, string[] CompletedFeatures, string[] PendingFeatures);
public sealed record ProgressValidation(int Passed, int Total, string Date);
public sealed record ProgressConfiguration(int SchemaVersion, string InventoryDate, ProgressModule[] Modules,
    ResearchProgress Research, ProgressValidation Validation, ImplementationProgress Implementation);
public sealed record DiscoveredFunction(uint Address, int BodyBytes);
public sealed record ModuleInventory(ProgressModule Module, IReadOnlyList<DiscoveredFunction> Functions);

/// <summary>Reports analyzed function candidates, independently of native reconstruction and asset evidence.
/// No PSP matching build exists yet, so this tool cannot certify any matched executable bytes.</summary>
public static class ProjectProgress
{
    public const string StartMarker = "<!-- progress:start -->";
    public const string EndMarker = "<!-- progress:end -->";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        bool check = args.SequenceEqual(["--check"]);
        string? trx = args.Length == 2 && args[0] == "--test-results" ? args[1] : null;
        string? verifyTrx = args.Length == 2 && args[0] == "--verify-test-counts" ? args[1] : null;
        if (args.Length != 0 && !check && trx is null && verifyTrx is null)
        {
            error.WriteLine("Usage: TagForceSpecial.Tools progress [--check | --test-results <run.trx> | --verify-test-counts <run.trx>]");
            return 2;
        }
        try
        {
            string root = FindRoot();
            string configPath = Path.Combine(root, "config", "progress.json");
            var config = JsonSerializer.Deserialize<ProgressConfiguration>(File.ReadAllText(configPath), JsonOptions)
                ?? throw new InvalidDataException("Missing progress configuration.");
            if (verifyTrx is not null)
            {
                VerifyTestCounts(config.Validation, File.ReadAllText(verifyTrx));
                output.WriteLine("Recorded test counts match the executed suite; snapshot date preserved.");
                return 0;
            }
            if (trx is not null) config = config with { Validation = ReadValidation(File.ReadAllText(trx)) };
            if (config.SchemaVersion != 1 || config.Modules is null || config.Modules.Length == 0 ||
                config.Research is null || config.Validation is null || config.Implementation is null ||
                config.Modules.Select(m => m.Registry).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Modules.Length)
                throw new InvalidDataException("Unsupported progress configuration.");
            var modules = config.Modules.Select(module =>
            {
                if (module.Kind is not ("resident" or "overlay") || module.ExecutableBytes <= 0 ||
                    !ValidHash(module.SourceSha256) || !ValidHash(module.InventoryEvidenceSha256))
                    throw new InvalidDataException("Invalid module inventory metadata.");
                string registry = Path.GetFullPath(Path.Combine(root, module.Registry));
                string registryRoot = Path.Combine(root, "config") + Path.DirectorySeparatorChar;
                if (!registry.StartsWith(registryRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Function registries must be under config/.");
                return new ModuleInventory(module, ReadFunctions(File.ReadAllText(registry)));
            }).ToArray();
            var native = NativeByteProgress.Load(root, modules);
            var coverage = ResearchValidation.Read<CoverageManifest>(File.ReadAllText(Path.Combine(root, "config/coverage.json")));
            ResearchValidation.ValidateCoverage(coverage, modules);
            string readmePath = Path.Combine(root, "README.md");
            string statusPath = Path.Combine(root, "docs", "STATUS.md");
            string originalReadme = File.ReadAllText(readmePath).Replace("\r\n", "\n");
            string originalStatus = File.ReadAllText(statusPath).Replace("\r\n", "\n");
            string newReadme = ReplaceProgress(originalReadme,
                ProgressPresentation.RenderCompact(config, modules, native, coverage));
            string newStatus = ReplaceProgress(originalStatus,
                ProgressPresentation.RenderDetailed(config, modules, native, coverage));
            if (check)
            {
                if (originalReadme != newReadme || originalStatus != newStatus)
                {
                    error.WriteLine("Generated README or docs/STATUS.md progress is stale. Run progress to refresh.");
                    return 1;
                }
                output.WriteLine("README and docs/STATUS.md are current.");
            }
            else
            {
                // Calculate/validate before writing either page. Preserve all prose outside progress markers.
                if (trx is not null) File.WriteAllText(configPath, JsonSerializer.Serialize(config, JsonOptions) + "\n");
                if (originalReadme != newReadme) File.WriteAllText(readmePath, newReadme);
                if (originalStatus != newStatus) File.WriteAllText(statusPath, newStatus);
                output.WriteLine($"Progress updated. Evidence-backed native matches: {native.MatchedFunctions} functions, {native.MatchedBytes} bytes.");
            }
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            JsonException or System.Xml.XmlException or InvalidOperationException or FormatException or OverflowException)
        {
            error.WriteLine($"Progress error: {e.Message}");
            return 1;
        }
    }

    public static IReadOnlyList<DiscoveredFunction> ReadFunctions(string csv)
    {
        var lines = csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        if (lines.Length < 2 || lines[0] != "address,body_bytes")
            throw new InvalidDataException("Expected candidate registry header: address,body_bytes.");
        var found = new HashSet<uint>();
        var functions = new List<DiscoveredFunction>();
        foreach (string line in lines.Skip(1))
        {
            var fields = line.Split(',');
            if (fields.Length != 2 || !fields[0].StartsWith("0x", StringComparison.Ordinal) ||
                !uint.TryParse(fields[0].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint address) ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int size) || size <= 0 ||
                !found.Add(address))
                throw new InvalidDataException("Invalid or duplicate discovered function.");
            functions.Add(new(address, size));
        }
        return functions.AsReadOnly();
    }

    public static ProgressValidation ReadValidation(string trx)
    {
        var document = XDocument.Parse(trx);
        var summary = document.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "ResultSummary");
        var counters = summary?.Elements().SingleOrDefault(e => e.Name.LocalName == "Counters")
            ?? throw new InvalidDataException("TRX result counters are missing.");
        int passed = int.Parse(counters.Attribute("passed")?.Value ?? "", CultureInfo.InvariantCulture);
        int total = int.Parse(counters.Attribute("total")?.Value ?? "", CultureInfo.InvariantCulture);
        if (total <= 0 || passed < 0 || passed > total) throw new InvalidDataException("Invalid TRX counters.");
        string finish = document.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "Times")?.Attribute("finish")?.Value
            ?? throw new InvalidDataException("TRX finish time is missing.");
        var time = DateTimeOffset.Parse(finish, CultureInfo.InvariantCulture);
        string date = TimeZoneInfo.ConvertTime(time, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new(passed, total, date);
    }

    public static void VerifyTestCounts(ProgressValidation recorded, string trx)
    {
        var actual = ReadValidation(trx);
        if (recorded.Passed != actual.Passed || recorded.Total != actual.Total)
            throw new InvalidDataException("Recorded test counts are stale. Refresh progress using the latest test results.");
    }

    public static string Render(ProgressConfiguration config, IReadOnlyList<ModuleInventory> modules,
        NativeByteReport? verified = null) => ProgressPresentation.RenderDetailed(config, modules,
            verified ?? NativeByteProgress.Measure(modules, Array.Empty<NativeFunctionMatch>()));

    public static string ReplaceProgress(string readme, string generated)
    {
        readme = readme.Replace("\r\n", "\n");
        generated = generated.Replace("\r\n", "\n");
        int start = readme.IndexOf(StartMarker, StringComparison.Ordinal);
        int end = readme.IndexOf(EndMarker, StringComparison.Ordinal);
        if (start < 0 || end <= start || start != readme.LastIndexOf(StartMarker, StringComparison.Ordinal) ||
            end != readme.LastIndexOf(EndMarker, StringComparison.Ordinal))
            throw new InvalidDataException("README must contain one ordered progress marker pair.");
        return readme[..(start + StartMarker.Length)] + "\n" + generated.TrimEnd() + "\n" + readme[end..];
    }

    private static string FindRoot()
    {
        foreach (string beginning in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(beginning); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "ygo-tag-force-special-decomp.slnx")) &&
                    File.Exists(Path.Combine(directory.FullName, "config", "progress.json"))) return directory.FullName;
        throw new InvalidDataException("Repository root not found; run from the project directory.");
    }

    private static bool ValidHash(string hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string Bytes(long value) => $"{Number(value)} (0x{value:X})";
    private static string Cell(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
