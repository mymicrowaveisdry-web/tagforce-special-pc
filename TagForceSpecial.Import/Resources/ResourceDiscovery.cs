using TagForceSpecial.Import.Installed;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.Import.Stages;
using System.Text.Json;

namespace TagForceSpecial.Import.Resources;

/// <summary>Finds existing local import resources in bounded application/source/private-data locations without scanning whole disks.</summary>
public static class ResourceDiscovery
{
    public static IReadOnlyList<string> Roots(PrivateStorage storage, string? source = null)
    {
        var roots = new List<string> { storage.Root };
        if (!string.IsNullOrWhiteSpace(source)) roots.Add(Path.GetDirectoryName(Path.GetFullPath(source))!);
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TagForceSpecial"));
        var containers = new[]
        {
            Path.GetDirectoryName(storage.Root),
            Environment.GetEnvironmentVariable("TFSP_PRIVATE_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TFSP-Private")
        };
        foreach (string? container in containers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (container is null || !Directory.Exists(container) || Path.GetDirectoryName(container) is null) continue;
            try
            {
                PrivateStorage.Check(container); roots.Add(container);
                foreach (var child in new DirectoryInfo(container).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Take(128))
                {
                    roots.Add(child.FullName);
                    foreach (string name in new[] { "data", "first-run-data", "reference", "initial" }) roots.Add(Path.Combine(child.FullName, name));
                }
            }
            catch (Exception e) when (PrivateStorage.Recoverable(e)) { }
        }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToArray();
    }

    public static string? FindRecipes(IEnumerable<string> roots, string buildId)
    {
        foreach (string root in roots)
        {
            try
            {
                var storage = new PrivateStorage(root);
                var active = InstalledGameReader.ReadActiveManifest(storage);
                if (active?.Source.BuildId == buildId && active.Outputs.Recipes is { } output)
                {
                    string candidate = Path.Combine(storage.PathFor(output.Directory), "recipes", "viewer.json");
                    if (Compatible(candidate, buildId)) return candidate;
                }
                if (RecipeStage.FindLegacy(storage) is { } legacy && Compatible(legacy, buildId)) return legacy;
                foreach (var session in InstalledGameReader.ListSessions(storage).Where(m => m.Source.BuildId == buildId &&
                    m.Status is Manifest.ImportStatus.Succeeded or Manifest.ImportStatus.SucceededWithWarnings))
                    if (session.Outputs.Recipes is { } previous)
                    {
                        string candidate = Path.Combine(storage.PathFor(previous.Directory), "recipes", "viewer.json");
                        if (Compatible(candidate, buildId)) return candidate;
                    }
            }
            catch (Exception e) when (PrivateStorage.Recoverable(e)) { }
        }
        return null;
    }
    private static bool Compatible(string path, string buildId)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > RecipeStage.MaximumDocumentBytes) return false;
        PrivateStorage.Check(path);
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        return json.RootElement.TryGetProperty("buildId", out var build) && build.GetString() == buildId;
    }
}
