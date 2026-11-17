using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Import.Inspection;

public sealed record ImportDifference(string Kind, string Resource, string? Before, string? After);

/// <summary>Compares persisted identities, immutable artifacts, original file inventories and validation outcomes.</summary>
public static class ImportComparison
{
    public static IReadOnlyList<ImportDifference> Compare(PrivateStorage storage, ImportManifest before, ImportManifest after)
    {
        var result = new List<ImportDifference>();
        Add("metadata", "build", before.Source.BuildId, after.Source.BuildId);
        Add("metadata", "source SHA-256", before.Source.Sha256, after.Source.Sha256);
        Add("metadata", "importer", before.ImporterVersion, after.ImporterVersion);
        Add("metadata", "decoder", before.DecoderSha256, after.DecoderSha256);
        Add("metadata", "status", before.Status.ToString(), after.Status.ToString());
        Add("metadata", "options", System.Text.Json.JsonSerializer.Serialize(before.Options), System.Text.Json.JsonSerializer.Serialize(after.Options));
        var left = Outputs(before).ToDictionary(o => o.Stage); var right = Outputs(after).ToDictionary(o => o.Stage);
        foreach (string id in left.Keys.Union(right.Keys).Order())
        {
            left.TryGetValue(id, out var a); right.TryGetValue(id, out var b);
            Add("stage-key", id, a?.CacheKey, b?.CacheKey);
            Add("stage-version", id, a?.ToolVersion, b?.ToolVersion);
            CompareMaps("artifact", a?.Artifacts.ToDictionary(f => id + "/" + f.Path, f => $"{f.Length}:{f.Sha256}") ?? [],
                b?.Artifacts.ToDictionary(f => id + "/" + f.Path, f => $"{f.Length}:{f.Sha256}") ?? []);
        }
        CompareMaps("original-file", Files(before), Files(after));
        CompareMaps("validation", Checks(before), Checks(after));
        if (before.Outputs.Catalog is not null && after.Outputs.Catalog is not null)
            Add("normalized-count", "cards", ImportInspection.Cards(storage, before).Count.ToString(), ImportInspection.Cards(storage, after).Count.ToString());
        return result;

        void Add(string kind, string resource, string? a, string? b) { if (a != b) result.Add(new(kind, resource, a, b)); }
        void CompareMaps(string kind, Dictionary<string, string> a, Dictionary<string, string> b)
        { foreach (string key in a.Keys.Union(b.Keys).Order()) Add(kind, key, a.GetValueOrDefault(key), b.GetValueOrDefault(key)); }
        Dictionary<string, string> Files(ImportManifest m) => m.Outputs.Inventory is null ? [] : ImportInspection.Inventory(storage, m).Files.ToDictionary(f => f.Path, f => $"{f.Length}:{f.Sha256}");
        Dictionary<string, string> Checks(ImportManifest m) => ManifestJson.Parse<ValidationReport>(storage.ReadBytes(ImportLayout.SessionValidation(m.SessionId), Installed.InstalledGameReader.MaximumManifestBytes)).Checks
            .ToDictionary(c => c.Id + "/" + c.Resource, c => c.Status + ":" + c.Details);
    }
    public static IEnumerable<StageOutput> Outputs(ImportManifest m) =>
        new[] { m.Outputs.Catalog, m.Outputs.Presentation, m.Outputs.Recipes, m.Outputs.Inventory }.OfType<StageOutput>();
}
