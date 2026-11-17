using System.Text.Json;
using System.Text.Json.Serialization;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Maintenance;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Tools;

/// <summary>Command-line front end of the shared importer: the same pipeline the workbench and the game use.</summary>
public static class ImportCommands
{
    private const string Usage = "Usage: import <iso-or-extracted-directory> <data-root> [--recipes <recipes/viewer.json>] [--no-presentation] [--no-recipes] [--no-inventory] [--no-cache]\n" +
        "       import-check <data-root>\n" +
        "       import-clean-legacy <data-root> [--yes]\n" +
        "       import-clean-stages <data-root> [--yes]";
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() }
    };

    public static int Run(string command, string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (command == "import-check" && args.Length == 1) return Check(args[0], output);
            if (command is "import-clean-legacy" or "import-clean-stages")
            {
                if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--yes")
                { error.WriteLine(Usage); return 2; }
                return Clean(command, args[0], args.Length == 2, output);
            }
            if (command != "import" || args.Length < 2) { error.WriteLine(Usage); return 2; }
            var options = new ImportOptions();
            for (int i = 2; i < args.Length; i++)
            {
                options = args[i] switch
                {
                    "--recipes" when i + 1 < args.Length => options with { RecipeLibrary = Path.GetFullPath(args[++i]) },
                    "--no-presentation" => options with { Presentation = false },
                    "--no-recipes" => options with { Recipes = false },
                    "--no-inventory" => options with { Inventory = false },
                    "--no-cache" => options with { ReuseCache = false },
                    _ => throw new ArgumentException(Usage)
                };
            }
            using var cancel = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
            string? lastStage = null;
            var manifest = ImportPipeline.Run(new(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), options),
                new InlineProgress<ImportProgress>(p =>
                {
                    if (p.StageId == lastStage && p.Fraction is not null) return;
                    lastStage = p.StageId; error.WriteLine($"[{p.StageName}] {p.Message}");
                }), cancel.Token);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                manifest.SessionId, manifest.Status, manifest.Source.BuildId, manifest.Source.Supported,
                stages = manifest.Stages.Select(s => new { s.Id, s.Status, s.Message, durationSeconds = Seconds(s) }),
                manifest.Warnings, manifest.Errors
            }, Json));
            return manifest.Status is ImportStatus.Succeeded or ImportStatus.SucceededWithWarnings ? 0 : 1;
        }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { error.WriteLine("ImportFailed: " + e.Message); return 1; }
    }

    private static int Check(string root, TextWriter output)
    {
        var result = InstalledGameReader.Open(new PrivateStorage(Path.GetFullPath(root)));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            usable = result.Usable, session = result.Game?.Manifest?.SessionId, cards = result.Game?.Catalog.Catalog.Count,
            presentationEntries = result.Game?.Presentation?.Entries.Count, recipes = result.Game?.RecipeRoot is not null,
            issues = result.Issues
        }, Json));
        return result.Usable ? 0 : 1;
    }

    private static int Clean(string command, string root, bool execute, TextWriter output)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Data root does not exist.");
        var storage = new PrivateStorage(Path.GetFullPath(root));
        var plan = command == "import-clean-legacy" ? Cleanup.PlanLegacy(storage) : Cleanup.PlanUnusedStages(storage);
        output.WriteLine(JsonSerializer.Serialize(new { plan.Kind, plan.Items, plan.Bytes, plan.Blocked, dryRun = !execute }, Json));
        if (plan.Blocked is not null) return 1;
        if (execute) { Cleanup.Execute(storage, plan); output.WriteLine("Cleanup completed."); }
        else output.WriteLine("Preview only. Run again with --yes to remove these importer-owned files.");
        return 0;
    }

    private static double? Seconds(StageRecord s) => s.StartedUtc is { } a && s.FinishedUtc is { } b ? Math.Round((b - a).TotalSeconds, 2) : null;
}
