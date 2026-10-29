using System.Globalization;
using System.Text;

namespace TagForceSpecial.Tools;

public static class ProgressPresentation
{
    private static void Validate(ProgressConfiguration config)
    {
        if (config.Validation.Total <= 0 || config.Validation.Passed < 0 ||
            config.Validation.Passed > config.Validation.Total ||
            config.Research.LoadedTableBytes < 0 || config.Research.DecodedCards < 0 ||
            config.Research.LoadedTables < 0 || config.Research.RuntimeGetterSamples < 0 ||
            config.Research.CardhSprites < 0 || config.Research.EvidenceSha256 is null ||
            config.Research.EvidenceSha256.Values.Any(hash =>
                hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit)) ||
            !DateOnly.TryParseExact(config.InventoryDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _) ||
            !DateOnly.TryParseExact(config.Validation.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            throw new InvalidDataException("Invalid research or validation snapshot.");
    }

    private static string N(long n) => n.ToString("N0", CultureInfo.InvariantCulture);
    private static string Safe(string s) => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");

    public static string RenderCompact(ProgressConfiguration config,
        IReadOnlyList<ModuleInventory> inventories, NativeByteReport matches, CoverageManifest? coverage = null)
    {
        Validate(config);
        long inventoried = inventories.Sum(i => i.Module.ExecutableBytes);
        long candidates = inventories.Sum(i => (long)i.Functions.Count);
        var output = new StringBuilder();
        output.AppendLine("## Progress");
        output.AppendLine();
        output.AppendLine($"**Current milestone:** {Safe(config.Implementation.Milestone)}");
        output.AppendLine();
        output.AppendLine("| Measure | Recorded status |");
        output.AppendLine("| --- | --- |");
        output.AppendLine($"| C# tests | **{N(config.Validation.Passed)}/{N(config.Validation.Total)}** (last recorded {config.Validation.Date}) |");
        foreach (var scope in CoverageScopes(coverage))
            output.AppendLine($"| {Safe(scope.Label)} | {ResearchValidation.FormatCoverage(scope)} |");
        output.AppendLine($"| Decoded cards | {N(config.Research.DecodedCards)}; full visual acceptance remains pending |");
        output.AppendLine();
        output.AppendLine($"**Implemented subset:** {Safe(string.Join(", ", config.Implementation.CompletedFeatures))}.");
        output.AppendLine();
        output.AppendLine($"**Still to build:** {Safe(string.Join(", ", config.Implementation.PendingFeatures))}.");
        output.AppendLine();
        output.AppendLine("Passing tests and decoded assets are not a whole-game completion percentage. PSP comparisons cover only their declared inputs and behavior; a supported duel in the PC application remains a separate acceptance gate.");
        output.AppendLine();
        output.AppendLine("<details>");
        output.AppendLine("<summary>Optional native byte-matching research</summary>");
        output.AppendLine();
        output.AppendLine($"`{NativeByteProgress.Bar(matches.MatchedBytes, inventoried)}` **{NativeByteProgress.Percent(matches.MatchedBytes, inventoried)}** — {N(matches.MatchedBytes)} / {N(inventoried)} inventoried executable bytes");
        output.AppendLine();
        output.AppendLine($"**{N(matches.MatchedFunctions)}** native functions matched · **{N(candidates)}** discovered candidates.");
        output.AppendLine();
        output.AppendLine("This measures only exact native reconstruction against a partial, unclassified inventory. It is not PC-build progress and does not gate the C# rewrite. CI checks evidence metadata; original-byte comparisons require local proprietary inputs.");
        output.AppendLine();
        output.AppendLine("</details>");
        output.AppendLine();
        output.AppendLine("[Detailed status](docs/STATUS.md) · [Roadmap](docs/ROADMAP.md) · [Updating progress](docs/DEVELOPMENT.md)");
        return output.ToString();
    }

    public static string RenderDetailed(ProgressConfiguration config,
        IReadOnlyList<ModuleInventory> inventories, NativeByteReport matches, CoverageManifest? coverage = null)
    {
        Validate(config);
        long executableBytes = inventories.Sum(x => x.Module.ExecutableBytes);
        var s = new StringBuilder();
        s.AppendLine("## Generated status");
        s.AppendLine();
        s.AppendLine("### C# reconstruction and asset research");
        s.AppendLine();
        s.AppendLine("| Metric | Recorded value |");
        s.AppendLine("| --- | --- |");
        s.AppendLine($"| Milestone | {Safe(config.Implementation.Milestone)} |");
        s.AppendLine($"| C# unit-test cases | {N(config.Validation.Passed)} / {N(config.Validation.Total)} ({config.Validation.Date}, Europe/Berlin) |");
        foreach (var scope in CoverageScopes(coverage))
            s.AppendLine($"| {Safe(scope.Label)} | {ResearchValidation.FormatCoverage(scope)} |");
        s.AppendLine($"| Decoded cards | {N(config.Research.DecodedCards)} |");
        s.AppendLine($"| Card getter samples compared with PPSSPP | {N(config.Research.RuntimeGetterSamples)} |");
        s.AppendLine($"| Card tables compared with PPSSPP memory | {N(config.Research.LoadedTables)} tables · {N(config.Research.LoadedTableBytes)} **asset bytes** |");
        s.AppendLine($"| Card atlas sprite samples | {N(config.Research.CardhSprites)} |");
        s.AppendLine($"| Card-screen visual acceptance | {Safe(config.Research.VisualAcceptance)} |");
        s.AppendLine($"| Implemented subset | {Safe(string.Join(", ", config.Implementation.CompletedFeatures))} |");
        s.AppendLine($"| Pending | {Safe(string.Join(", ", config.Implementation.PendingFeatures))} |");
        s.AppendLine();
        s.AppendLine("These tests and bounded reference comparisons do not establish whole-game completion or a playable PC application. Native byte equality is optional and does not gate functional reconstruction or PC integration.");
        s.AppendLine();
        s.AppendLine("### Optional native byte-matching research");
        s.AppendLine();
        s.AppendLine($"`{NativeByteProgress.Bar(matches.MatchedBytes, executableBytes)}` **{NativeByteProgress.Percent(matches.MatchedBytes, executableBytes)}** · **{N(matches.MatchedBytes)} / {N(executableBytes)}** inventoried executable bytes (verified native matches)");
        s.AppendLine();
        s.AppendLine("| Module | Function candidates | Native matches | Matched bytes / inventoried bytes | Coverage |");
        s.AppendLine("| --- | ---: | ---: | ---: | ---: |");
        foreach (var module in inventories)
        {
            string key = module.Module.Name;
            long bytes = matches.ModuleBytes[key];
            s.AppendLine($"| `{Safe(key)}` | {N(module.Functions.Count)} | {N(matches.ModuleFunctions[key])} | {N(bytes)} / {N(module.Module.ExecutableBytes)} | {NativeByteProgress.Percent(bytes, module.Module.ExecutableBytes)} |");
        }
        s.AppendLine();
        s.AppendLine("The inventories mix as-yet-unclassified game code, CRT/SDK code and candidate function boundaries. This denominator is **only currently inventoried executable text**, not a measured whole-game scope. Overlapping candidates are never summed as coverage. Native byte matches require matched original/rebuilt code digests and recorded evidence; CI cannot reproduce those comparisons without the original binaries.");
        s.AppendLine();
        s.AppendLine($"Inventory recorded: {config.InventoryDate}. Tests and research counts are recorded snapshots; CI does not include the original game ISO or rerun PPSSPP evidence capture.");
        s.AppendLine();
        s.AppendLine("Inputs: `config/progress.json`, `config/native-matches.json` and the inventoried function CSVs under `config/npjh00142/`.");
        return s.ToString();
    }

    private static IEnumerable<CoverageScope> CoverageScopes(CoverageManifest? coverage) => coverage?.Scopes ??
    [
        new("csharp-acceptance", "C# acceptance-criterion coverage", "unscoped-v1", null, "Denominator not defined.", []),
        new("psp-reference", "PSP reference-scenario coverage", "unscoped-v1", null, "Denominator not defined.", [])
    ];
}
