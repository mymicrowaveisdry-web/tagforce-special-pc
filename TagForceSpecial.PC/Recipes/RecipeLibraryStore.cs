using System.Security.Cryptography;
using TagForceSpecial.Cards;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Recipes;

/// <summary>Copies validated private recipe libraries into retained generations and atomically activates the completed copy.</summary>
public sealed class RecipeLibraryStore(PrivateStorage storage)
{
    private sealed record Binding(int SchemaVersion, string Generation, string DocumentSha256);

    public RecipeCatalog? OpenActive(CardCatalog catalog)
    {
        if (!File.Exists(storage.PathFor("recipes/active.json"))) return RecipeCatalog.Open(storage, catalog);
        var binding = storage.Read<Binding>("recipes/active.json", 65536);
        if (binding.SchemaVersion != 1 || !Guid.TryParseExact(binding.Generation, "N", out _) ||
            binding.DocumentSha256 is not { Length: 64 } || !binding.DocumentSha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidDataException("Recipe library binding is invalid.");
        var generation = new PrivateStorage(storage.PathFor("recipes/imports/" + binding.Generation));
        if (HashDocument(generation) != binding.DocumentSha256) throw new InvalidDataException("Recipe library metadata changed.");
        return RecipeCatalog.Open(generation, catalog) ?? throw new InvalidDataException("Imported recipe library is missing.");
    }

    public int Import(string file, CardCatalog catalog, CancellationToken cancellation = default, IProgress<ImportProgress>? progress = null)
    {
        string full = Path.GetFullPath(file);
        PrivateStorage.Check(full);
        string folder = Path.GetDirectoryName(full) ?? throw new InvalidDataException("Recipe library folder is missing.");
        if (!string.Equals(Path.GetFileName(full), "viewer.json", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(folder), "recipes", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new InvalidDataException("Select viewer.json inside the prepared recipe library's recipes folder.");
        var source = new PrivateStorage(Path.GetDirectoryName(folder)!);
        string expected = HashDocument(source);
        var document = source.Read<RecipeDocument>("recipes/viewer.json", 8 * 1024 * 1024);
        var library = RecipeCatalog.Open(source, catalog) ?? throw new InvalidDataException("Recipe library is empty.");
        Directory.CreateDirectory(storage.PathFor("recipes"));
        using var lease = new FileStream(storage.PathFor("recipes/import.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string id = Guid.NewGuid().ToString("N");
        var destination = new PrivateStorage(storage.PathFor("recipes/imports/" + id));
        Directory.CreateDirectory(destination.PathFor("recipes/blobs"));
        var images = document.Textures.DistinctBy(t => t.Sha256).ToArray();
        int copied = 0;
        foreach (var item in images)
        {
            cancellation.ThrowIfCancellationRequested();
            var texture = library.ReadTexture(item.Key) ?? throw new InvalidDataException("Recipe library texture is missing.");
            using var output = new FileStream(destination.PathFor("recipes/blobs/" + item.Sha256 + ".rgba"), FileMode.CreateNew, FileAccess.Write);
            output.Write(texture.Pixels); output.Flush(true);
            copied++;
            if (copied % 32 == 0 || copied == images.Length)
                progress?.Report(new(5 + copied * 90 / Math.Max(1, images.Length), "Copying recipe artwork"));
        }
        destination.Write("recipes/viewer.json", document);
        _ = RecipeCatalog.Open(destination, catalog) ?? throw new InvalidDataException("Copied recipe library failed validation.");
        cancellation.ThrowIfCancellationRequested();
        if (HashDocument(source) != expected) throw new InvalidDataException("Recipe source changed during import.");
        storage.Write("recipes/active.json", new Binding(1, id, HashDocument(destination)));
        progress?.Report(new(100, "Recipe library ready"));
        return library.Records.Count;
    }

    private static string HashDocument(PrivateStorage source)
    {
        string path = source.PathFor("recipes/viewer.json");
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Recipe metadata exceeds its limit.");
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
