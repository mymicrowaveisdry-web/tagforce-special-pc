using System.Security.Cryptography;
using System.Text.Json;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Cards;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Import.Stages;

/// <summary>Copies a prepared recipe library (<c>recipes/viewer.json</c> plus RGBA blobs) after checking build identity, card IDs and blob
/// hashes. Layout and draw lists are checked again by the game's recipe catalog when it opens the copy.</summary>
public static class RecipeStage
{
    public const string Id = "recipes";
    public const string Version = "recipe-stage-v1";
    public const int MaximumDocumentBytes = 8 * 1024 * 1024;
    private const string Document = "recipes/viewer.json";

    /// <param name="library">Path to a <c>recipes/viewer.json</c> inside a prepared library, or null to skip.</param>
    public static StageResult Run(StageCache cache, string? library, GameBuildProfile profile, CardCatalog catalog, bool reuse,
        IProgress<(long Done, long Total)>? progress, CancellationToken cancellation)
    {
        if (library is null)
            return new(StageStatus.Skipped, "No recipe library was selected or found; the Recipe Viewer will show no imported recipes.", Checks:
            [
                new("recipes.library-present", CheckCategory.Normalized, CheckSeverity.Warning, CheckStatus.Warning, Document,
                    "No prepared recipe library was selected, kept from the active import or found from the previous importer.",
                    Remediation: "Import a prepared recipes/viewer.json library from the importer.")
            ]);
        string full = Path.GetFullPath(library);
        string folder = Path.GetDirectoryName(full) ?? throw new InvalidDataException("Recipe library folder is missing.");
        if (!string.Equals(Path.GetFileName(full), "viewer.json", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(folder), "recipes", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new InvalidDataException("Select viewer.json inside the prepared recipe library's recipes folder.");
        PrivateStorage.Check(full);
        if (new FileInfo(full).Length > MaximumDocumentBytes) throw new InvalidDataException("Recipe metadata exceeds its limit.");
        byte[] document = File.ReadAllBytes(full);
        string documentSha = Hash(document);
        var textures = Check(document, profile, catalog, out int recipes);
        string key = StageCache.Key(Id, Version, documentSha, profile.Fingerprint, catalog.Source.ContainerSha256);
        if (reuse && cache.TryReuse(Id, key, dir => Verify(dir, documentSha, textures)))
            return Done(StageStatus.Reused, $"Recipe library reused: {recipes} recipes.", recipes, textures.Count);
        string sourceBlobs = Path.Combine(folder, "blobs");
        cache.Publish(Id, key, staging =>
        {
            Directory.CreateDirectory(Path.Combine(staging, "recipes", "blobs"));
            int copied = 0;
            foreach (var texture in textures)
            {
                cancellation.ThrowIfCancellationRequested();
                byte[] pixels = ReadBlob(Path.Combine(sourceBlobs, texture.Sha256 + ".rgba"), texture);
                File.WriteAllBytes(Path.Combine(staging, "recipes", "blobs", texture.Sha256 + ".rgba"), pixels);
                progress?.Report((++copied, textures.Count));
            }
            File.WriteAllBytes(Path.Combine(staging, "recipes", "viewer.json"), document);
            if (Hash(File.ReadAllBytes(full)) != documentSha) throw new InvalidDataException("Recipe source changed during import.");
        }, cancellation);
        return Done(StageStatus.Succeeded, $"Recipe library imported: {recipes} recipes, {textures.Count} textures.", recipes, textures.Count);

        StageResult Done(StageStatus status, string message, int count, int blobCount)
        {
            var output = new StageOutput(Id, cache.RelativeDirectory(Id, key), key, Version, StageCache.Inventory(cache.Directory(Id, key)));
            return new(status, message, output,
            [
                new("recipes.identity", CheckCategory.Normalized, CheckSeverity.Info, CheckStatus.Passed, Document,
                    "The library names this exact build and ISO, and every recipe card ID exists in the imported catalog.", $"{count} recipes"),
                new("recipes.blobs", CheckCategory.Asset, CheckSeverity.Info, CheckStatus.Passed, "recipes/blobs",
                    "Every recipe texture blob has its recorded size and SHA-256.", $"{blobCount} textures"),
                new("recipes.selection-parity", CheckCategory.Evidence, CheckSeverity.Info, CheckStatus.NotSupported, Document,
                    "Recipe browsing integrity does not establish original selection or active-deck behaviour.")
            ], count, count);
        }
    }

    /// <summary>The previous importer's active recipe library in this data root, if one exists.</summary>
    public static string? FindLegacy(PrivateStorage storage)
    {
        try
        {
            if (File.Exists(storage.PathFor("recipes/active.json")))
            {
                using var binding = JsonDocument.Parse(storage.ReadBytes("recipes/active.json", 65536));
                string? generation = binding.RootElement.TryGetProperty("generation", out var g) ? g.GetString() : null;
                if (!ImportLayout.ValidSessionId(generation)) return null;
                string candidate = storage.PathFor("recipes/imports/" + generation + "/" + Document);
                return File.Exists(candidate) ? candidate : null;
            }
            string direct = storage.PathFor(Document);
            return File.Exists(direct) ? direct : null;
        }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { return null; }
    }

    internal sealed record Texture(string Key, string Sha256, int Width, int Height);

    private static IReadOnlyList<Texture> Check(byte[] bytes, GameBuildProfile profile, CardCatalog catalog, out int recipes)
    {
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var root = json.RootElement;
        if (Int(root, "schemaVersion") != 1 || Text(root, "buildId") != profile.BuildId || Text(root, "isoSha256") != profile.ImageSha256)
            throw new InvalidDataException("The recipe library belongs to a different build or ISO.");
        var known = catalog.Cards.Select(c => c.GameCardId).ToHashSet();
        var list = root.GetProperty("recipes");
        if (list.GetArrayLength() > 2000) throw new InvalidDataException("Recipe library exceeds its record bound.");
        foreach (var recipe in list.EnumerateArray())
            foreach (string section in new[] { "main", "extra", "side" })
                foreach (var id in recipe.GetProperty(section).EnumerateArray())
                    if (!known.Contains(id.GetUInt32())) throw new InvalidDataException("A recipe references a card that is not in the imported catalog.");
        recipes = list.GetArrayLength();
        var textures = new List<Texture>();
        foreach (var t in root.GetProperty("textures").EnumerateArray())
        {
            var texture = new Texture(Text(t, "key") ?? "", Text(t, "sha256") ?? "", Int(t, "width"), Int(t, "height"));
            if (!ImportLayout.ValidKey(texture.Sha256) || texture.Width is < 1 or > 512 || texture.Height is < 1 or > 512)
                throw new InvalidDataException("Invalid recipe texture manifest.");
            textures.Add(texture);
        }
        if (textures.Count > 8000) throw new InvalidDataException("Recipe library exceeds its texture bound.");
        return textures.DistinctBy(t => t.Sha256).ToArray();
    }

    private static bool Verify(string directory, string documentSha, IReadOnlyList<Texture> textures)
    {
        try
        {
            if (Hash(File.ReadAllBytes(Path.Combine(directory, "recipes", "viewer.json"))) != documentSha) return false;
            foreach (var t in textures) _ = ReadBlob(Path.Combine(directory, "recipes", "blobs", t.Sha256 + ".rgba"), t);
            return true;
        }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { return false; }
    }

    private static byte[] ReadBlob(string path, Texture texture)
    {
        PrivateStorage.Check(path);
        if (!File.Exists(path) || new FileInfo(path).Length != checked(texture.Width * texture.Height * 4))
            throw new InvalidDataException("Recipe texture data is missing or has the wrong size.");
        byte[] bytes = File.ReadAllBytes(path);
        if (Hash(bytes) != texture.Sha256) throw new InvalidDataException("Recipe texture integrity mismatch.");
        return bytes;
    }

    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out int i) ? i : -1;
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
