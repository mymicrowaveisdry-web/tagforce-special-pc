using System.Globalization;
using System.Text;
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
        if (args.Length != 0 && !check && trx is null)
        {
            error.WriteLine("Usage: TagForceSpecial.Tools progress [--check | --test-results <run.trx>]");
            return 2;
        }
        try
        {
            string root = FindRoot();
            string configPath = Path.Combine(root, "config", "progress.json");
            var config = JsonSerializer.Deserialize<ProgressConfiguration>(File.ReadAllText(configPath), JsonOptions)
                ?? throw new InvalidDataException("Missing progress configuration.");
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
            string readmePath = Path.Combine(root, "README.md");
            string before = File.ReadAllText(readmePath).Replace("\r\n", "\n");
            string after = ReplaceProgress(before, Render(config, modules));
            if (check)
            {
                if (before != after) { error.WriteLine("README progress is stale. Run the progress command."); return 1; }
                output.WriteLine("README progress matches the recorded inventory and validation snapshot.");
            }
            else
            {
                // Render/validate first; malformed input must not partially update the snapshot.
                if (trx is not null) File.WriteAllText(configPath, JsonSerializer.Serialize(config, JsonOptions) + "\n");
                if (before != after) File.WriteAllText(readmePath, after);
                output.WriteLine("README progress updated. Verified executable matches: 0 functions, 0 bytes.");
            }
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or
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

    public static string Render(ProgressConfiguration config, IReadOnlyList<ModuleInventory> modules)
    {
        if (config.Validation.Total <= 0 || config.Validation.Passed < 0 || config.Validation.Passed > config.Validation.Total ||
            config.Research.LoadedTableBytes < 0 || config.Research.DecodedCards < 0 || config.Research.LoadedTables < 0 ||
            config.Research.RuntimeGetterSamples < 0 || config.Research.CardhSprites < 0 ||
            config.Research.EvidenceSha256 is null || config.Research.EvidenceSha256.Values.Any(hash => !ValidHash(hash)) ||
            !DateOnly.TryParseExact(config.InventoryDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            !DateOnly.TryParseExact(config.Validation.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new InvalidDataException("Invalid progress counters or dates.");
        long discovered = modules.Sum(m => (long)m.Functions.Count);
        long executableBytes = modules.Sum(m => m.Module.ExecutableBytes);
        var text = new StringBuilder();
        text.AppendLine("## Progress\n");
        text.AppendLine("| Metric | Current |");
        text.AppendLine("| --- | --- |");
        text.AppendLine("| Game C-decompilation targets matched | 0 / not yet classified |");
        text.AppendLine("| Game C-decompilation target bytes matched | 0 (0x0) / not yet classified |");
        text.AppendLine("| Remaining game C-decompilation targets | Not yet classified |");
        text.AppendLine("| Evidence-backed handwritten game assembly | 0 functions, 0 (0x0) bytes |");
        text.AppendLine("| Total game-owned functions | Not yet classified |");
        text.AppendLine("| Preserved PSP CRT/SDK assembly | Not yet classified |");
        text.AppendLine($"| Total discovered functions | {Number(discovered)} candidates across {Number(modules.Count)} inventoried modules |");
        text.AppendLine($"| Inventoried executable-section bytes | {Bytes(executableBytes)}; includes unclassified game/SDK code |");
        text.AppendLine("| Embedded/unassigned resident text | Not yet measured |\n");
        text.AppendLine("Discovered functions are Ghidra candidates, not confirmed C targets. Function-body sizes can overlap and are not summed as code coverage. Uninventoried modules are outside these totals.\n");
        text.AppendLine("### Runtime overlay modules\n");
        text.AppendLine("| Module | Discovered candidates | Matching C functions | Matching C bytes |");
        text.AppendLine("| --- | --- | --- | --- |");
        foreach (var module in modules.Where(m => m.Module.Kind == "overlay"))
            text.AppendLine($"| {Cell(module.Module.Name)} | {Number(module.Functions.Count)} | 0 / unclassified | 0 (0x0) / unclassified |");
        text.AppendLine();
        text.AppendLine("Only inventoried overlays are listed; this is not whole-game coverage.\n");
        text.AppendLine("### C# reconstruction and asset research\n");
        text.AppendLine("| Metric | Current |");
        text.AppendLine("| --- | --- |");
        text.AppendLine($"| Current milestone | {Cell(config.Implementation.Milestone)} |");
        text.AppendLine($"| Core behavior | {Cell(string.Join(", ", config.Implementation.CompletedFeatures))} |");
        text.AppendLine($"| C# tests passing at last validation | {Number(config.Validation.Passed)} / {Number(config.Validation.Total)} ({config.Validation.Date}, Europe/Berlin) |");
        text.AppendLine($"| Decoded and normalized cards | {Number(config.Research.DecodedCards)} |");
        text.AppendLine($"| Card getter samples compared with PPSSPP | {Number(config.Research.RuntimeGetterSamples)} |");
        text.AppendLine($"| Card tables matching PPSSPP memory | {Number(config.Research.LoadedTables)} tables, {Bytes(config.Research.LoadedTableBytes)} |");
        text.AppendLine($"| Bounded cardh atlas sprite samples decoded | {Number(config.Research.CardhSprites)} |");
        text.AppendLine($"| Stage 4D visual acceptance | {Cell(config.Research.VisualAcceptance)} |");
        text.AppendLine($"| Pending implementation | {Cell(string.Join(", ", config.Implementation.PendingFeatures))} |\n");
        text.AppendLine("The table-byte figure measures original asset data verified against memory, not replicated executable code. Research numbers are recorded local-evidence snapshots; CI does not contain the ISO or rerun those comparisons. No reliable whole-project completion percentage is available.\n");
        text.AppendLine("Generated by `TagForceSpecial.Tools progress` from:");
        text.AppendLine();
        foreach (var module in modules) text.AppendLine($"- `{module.Module.Registry}`");
        text.AppendLine("- `config/progress.json` (module provenance, research evidence and last test validation)");
        text.AppendLine();
        text.AppendLine($"Function inventory snapshot: {config.InventoryDate}. Matching is deliberately zero until a PSP rebuild/comparison pipeline supplies evidence.");
        return text.ToString().Replace("\r\n", "\n").TrimEnd() + "\n";
    }

    public static string ReplaceProgress(string readme, string generated)
    {
        readme = readme.Replace("\r\n", "\n");
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
