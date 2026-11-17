using System.Text.Json.Nodes;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Stages;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Import.Maintenance;

public sealed record CleanupItem(string RelativePath, bool Directory, long Bytes, string Reason);

/// <param name="Blocked">Why the plan must not run yet, or null.</param>
public sealed record CleanupPlan(string Kind, IReadOnlyList<CleanupItem> Items, string? Blocked)
{
    public long Bytes => Items.Sum(i => i.Bytes);
}

/// <summary>Removes files the importer owns, and nothing else: the previous importer's layout, or stage outputs no recent session needs.
/// Saved decks, fonts, the player profile and preferences are never removed.</summary>
public static class Cleanup
{
    private static readonly (string Path, bool Directory, string Reason)[] LegacyOwned =
    [
        ("caches", true, "Card caches of the previous importer"),
        ("presentation", true, "Presentation generations of the previous importer"),
        ("work", true, "Work area of the previous importer"),
        ("retained", true, "Interrupted work kept by the previous importer"),
        ("recipes", true, "Recipe generations of the previous importer"),
        ("import-journal.json", false, "Resume journal of the previous importer"),
        ("import.lock", false, "Lock file of the previous importer"),
        ("presentation-import.lock", false, "Presentation lock file of the previous importer")
    ];
    public const int KeptSessions = 3;

    public static CleanupPlan PlanLegacy(PrivateStorage storage)
    {
        var items = LegacyOwned.Where(o => Exists(storage, o.Path)).Select(o => Item(storage, o.Path, o.Directory, o.Reason)).ToList();
        string? blocked = null;
        var recipes = Active(storage)?.Outputs.Recipes;
        if (Directory.Exists(storage.PathFor("recipes")) &&
            (recipes is null || !recipes.Directory.StartsWith("import/stages/recipes/", StringComparison.Ordinal) ||
             !StageCache.Matches(storage.PathFor(recipes.Directory), recipes.Artifacts)))
            blocked = "The previous importer's recipe library has not been carried into a new import yet. Run an import first; it picks the library up automatically.";
        return new("legacy", items, blocked);
    }

    public static CleanupPlan PlanUnusedStages(PrivateStorage storage)
    {
        string root = storage.PathFor("import/stages");
        if (!Directory.Exists(root)) return new("stages", [], null);
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in new[] { Active(storage) }.Concat(Sessions(storage)
                     .Where(m => m.Status is ImportStatus.Succeeded or ImportStatus.SucceededWithWarnings).Take(KeptSessions)))
            if (manifest is not null)
                foreach (var output in new[] { manifest.Outputs.Catalog, manifest.Outputs.Presentation, manifest.Outputs.Recipes, manifest.Outputs.Inventory })
                    if (output is not null) keep.Add(output.Directory);
        var items = new List<CleanupItem>();
        foreach (string stage in Directory.EnumerateDirectories(root))
        {
            PrivateStorage.Check(stage);
            if (Path.GetFileName(stage) is not ("catalog" or "presentation" or "recipes" or "inventory")) continue;
            foreach (string directory in Directory.EnumerateDirectories(stage))
            {
                string name = Path.GetFileName(directory), relative = $"import/stages/{Path.GetFileName(stage)}/{name}";
                bool owned = ImportLayout.ValidKey(name) ||
                    name.StartsWith(".building-", StringComparison.Ordinal) && Guid.TryParseExact(name[10..], "N", out _) ||
                    name.StartsWith(".rejected-", StringComparison.Ordinal) && Guid.TryParseExact(name[10..], "N", out _);
                if (!owned || keep.Contains(relative)) continue;
                items.Add(Item(storage, relative, true, name.StartsWith('.') ? "Interrupted or rejected stage output" :
                    $"Not used by the active import or the {KeptSessions} most recent successful imports"));
            }
        }
        return new("stages", items, null);
    }

    /// <summary>Deletes exactly the planned items after re-checking each path; a blocked plan throws.</summary>
    public static void Execute(PrivateStorage storage, CleanupPlan plan)
    {
        if (plan.Blocked is not null) throw new InvalidOperationException(plan.Blocked);
        Directory.CreateDirectory(storage.PathFor(ImportLayout.Root));
        using var lease = new FileStream(storage.PathFor(ImportLayout.Lock), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var current = plan.Kind switch
        {
            "legacy" => PlanLegacy(storage), "stages" => PlanUnusedStages(storage),
            _ => throw new InvalidOperationException("Unknown cleanup kind.")
        };
        if (current.Blocked is not null) throw new InvalidOperationException(current.Blocked);
        if (!plan.Items.OrderBy(i => i.RelativePath, StringComparer.Ordinal).SequenceEqual(
                current.Items.OrderBy(i => i.RelativePath, StringComparer.Ordinal)))
            throw new InvalidOperationException("Cleanup plan changed; review a new plan before deleting files.");
        foreach (var item in plan.Items)
        {
            string path = storage.PathFor(item.RelativePath);
            if (item.Directory && Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (!item.Directory && File.Exists(path)) File.Delete(path);
        }
        if (plan.Kind == "legacy") ClearLegacyGeneration(storage);
    }

    /// <summary>The previous importer stored its active cache generation in preferences; only that one field is cleared.</summary>
    private static void ClearLegacyGeneration(PrivateStorage storage)
    {
        if (!File.Exists(storage.PathFor("preferences.json"))) return;
        try
        {
            var node = JsonNode.Parse(storage.ReadBytes("preferences.json", 65536)) as JsonObject;
            if (node is null || !node.ContainsKey("activeGeneration") || node["activeGeneration"] is null) return;
            node["activeGeneration"] = null;
            storage.WriteBytes("preferences.json", System.Text.Encoding.UTF8.GetBytes(node.ToJsonString(new() { WriteIndented = true })));
        }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { /* Unreadable preferences are reported by the game, not repaired here. */ }
    }

    private static ImportManifest? Active(PrivateStorage storage)
    {
        return InstalledGameReader.ReadActiveManifest(storage);
    }

    private static bool Exists(PrivateStorage storage, string relative) =>
        File.Exists(storage.PathFor(relative)) || Directory.Exists(storage.PathFor(relative));

    private static IEnumerable<ImportManifest> Sessions(PrivateStorage storage)
    {
        string root = storage.PathFor("import/sessions");
        if (!Directory.Exists(root)) return [];
        // Cleanup must fail closed on corrupt history instead of treating unreadable sessions as unused.
        return Directory.EnumerateDirectories(root).Where(d => ImportLayout.ValidSessionId(Path.GetFileName(d)))
            .Where(d => File.Exists(Path.Combine(d, "manifest.json")))
            .Select(d => ManifestJson.Parse<ImportManifest>(storage.ReadBytes(
                ImportLayout.SessionManifest(Path.GetFileName(d)), InstalledGameReader.MaximumManifestBytes)))
            .OrderByDescending(m => m.StartedUtc).ToArray();
    }

    private static CleanupItem Item(PrivateStorage storage, string relative, bool directory, string reason)
    {
        string path = storage.PathFor(relative);
        long bytes = directory
            ? Directory.Exists(path) ? DirectoryBytes(path) : 0
            : File.Exists(path) ? new FileInfo(path).Length : 0;
        return new(relative, directory, bytes, reason);
    }

    private static long DirectoryBytes(string path)
    {
        PrivateStorage.Check(path);
        long bytes = 0;
        foreach (string child in Directory.EnumerateFileSystemEntries(path))
        {
            PrivateStorage.Check(child);
            bytes += Directory.Exists(child) ? DirectoryBytes(child) : new FileInfo(child).Length;
        }
        return bytes;
    }
}
