using System.Security.Cryptography;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Stages;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Import.Installed;

/// <summary>Opens the active import for the game. Every output is re-verified; nothing is trusted because a manifest says so.</summary>
public static class InstalledGameReader
{
    public const int MaximumManifestBytes = 16 * 1024 * 1024;

    public static InstalledGameResult Open(PrivateStorage storage, IReadOnlyList<GameBuildProfile>? profiles = null)
    {
        profiles ??= [GameBuildProfile.English21];
        ImportManifest? manifest;
        try { manifest = ReadActiveManifest(storage); }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { return Fail("ActiveImportInvalid"); }
        if (manifest is null) return Fail("ImportMissing");
        var profile = profiles.FirstOrDefault(p => p.Fingerprint == manifest.Source.ProfileFingerprint && p.BuildId == manifest.Source.BuildId);
        if (profile is null || !manifest.Source.Supported || manifest.Status is ImportStatus.Failed or ImportStatus.Cancelled)
            return Fail("ImportUnsupported");
        if (manifest.Outputs.Catalog is not { } catalogOutput) return Fail("CatalogInvalid");
        Cards.CatalogStorage.LoadedCardCatalog catalog;
        try { catalog = CatalogStage.Open(storage.PathFor(catalogOutput.Directory), profile); }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { return Fail("CatalogInvalid"); }
        var issues = new List<ImportIssue>();
        SemanticAssetRegistry? presentation = null;
        if (manifest.Outputs.Presentation is { } presentationOutput)
        {
            try { presentation = PresentationStage.Open(storage.PathFor(presentationOutput.Directory), profile).Registry; }
            catch (Exception e) when (PrivateStorage.Recoverable(e) || e is KeyNotFoundException) { issues.Add(ImportIssue.For("PresentationInvalid")); }
        }
        else issues.Add(ImportIssue.For("PresentationMissing"));
        string? recipes = null;
        if (manifest.Outputs.Recipes is { } recipeOutput)
        {
            try
            {
                string directory = storage.PathFor(recipeOutput.Directory);
                if (!Pipeline.StageCache.Matches(directory, recipeOutput.Artifacts)) throw new InvalidDataException("Recipe files changed.");
                recipes = directory;
            }
            catch (Exception e) when (PrivateStorage.Recoverable(e)) { issues.Add(ImportIssue.For("RecipesInvalid")); }
        }
        return new(new InstalledGame(profile, catalog, presentation, recipes, manifest), issues.AsReadOnly());

        static InstalledGameResult Fail(string code) => new(null, [ImportIssue.For(code)]);
    }

    /// <summary>The active session's manifest after its hash matches the pointer, or null when no import is active.</summary>
    public static ImportManifest? ReadActiveManifest(PrivateStorage storage)
    {
        if (!File.Exists(storage.PathFor(ImportLayout.Active))) return null;
        var active = storage.Read<ActiveImport>(ImportLayout.Active, 65536);
        if (active.SchemaVersion != 1 || !ImportLayout.ValidSessionId(active.SessionId) || !ImportLayout.ValidKey(active.ManifestSha256))
            throw new InvalidDataException("Active import pointer is invalid.");
        byte[] bytes = storage.ReadBytes(ImportLayout.SessionManifest(active.SessionId), MaximumManifestBytes);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != active.ManifestSha256) throw new InvalidDataException("Active manifest changed.");
        var manifest = ManifestJson.Parse<ImportManifest>(bytes);
        if (manifest.SchemaVersion != ImportManifest.CurrentSchema || manifest.SessionId != active.SessionId)
            throw new InvalidDataException("Active manifest version or session differs.");
        return manifest;
    }

    /// <summary>Every recorded session manifest, newest first; unreadable ones are skipped.</summary>
    public static IReadOnlyList<ImportManifest> ListSessions(PrivateStorage storage, int maximum = 200)
    {
        string root = storage.PathFor("import/sessions");
        if (!Directory.Exists(root)) return [];
        var list = new List<ImportManifest>();
        foreach (string directory in Directory.EnumerateDirectories(root).Take(maximum * 2))
        {
            string id = Path.GetFileName(directory);
            if (!ImportLayout.ValidSessionId(id) || !File.Exists(Path.Combine(directory, "manifest.json"))) continue;
            try { list.Add(ManifestJson.Parse<ImportManifest>(storage.ReadBytes(ImportLayout.SessionManifest(id), MaximumManifestBytes))); }
            catch (Exception e) when (PrivateStorage.Recoverable(e)) { }
        }
        return list.OrderByDescending(m => m.StartedUtc).Take(maximum).ToArray();
    }
}
