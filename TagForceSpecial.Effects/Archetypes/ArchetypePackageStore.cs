using System.Security.Cryptography;
using System.Text;

namespace TagForceSpecial.Effects.Archetypes;

/// <summary>Versioned local packages with fixed document names and hash-pinned inventories; never discovers or downloads content.</summary>
public static class ArchetypePackageStore
{
    private static readonly string[] RequiredFiles = ["manifest.json", "definitions.json", "memberships.json", "identity-rules.json",
        "native-mappings.json", "catalog.json", "card-mappings.json", "mapping-sources.json"];
    public const int MaximumPackageBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static ArchetypePackageReference Write(string directory, ArchetypePackageDocument package, ArchetypeValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(context);
        ArchetypePackageValidation.Validate(package, context);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["manifest.json"] = ArchetypePackJson.WriteDocument(package.Pack.Manifest),
            ["definitions.json"] = ArchetypePackJson.WriteDocument(package.Pack.Definitions),
            ["memberships.json"] = ArchetypePackJson.WriteDocument(package.Pack.Memberships),
            ["identity-rules.json"] = ArchetypePackJson.WriteDocument(package.Pack.IdentityRules),
            ["native-mappings.json"] = ArchetypePackJson.WriteDocument(package.Pack.NativeMappings),
            ["catalog.json"] = ArchetypePackJson.WriteDocument(package.Catalog),
            ["card-mappings.json"] = ArchetypePackJson.WriteDocument(package.CardMappings),
            ["mapping-sources.json"] = ArchetypePackJson.WriteDocument(package.MappingSources)
        };
        if (package.NativeTable is { } table) files.Add("native-table.json", ArchetypePackJson.WriteDocument(table));
        var inventory = new ArchetypePackageInventory(1, package.Pack.Manifest.PackId, package.Pack.Manifest.Version,
            files.ToDictionary(p => p.Key, p => Hash(Utf8.GetBytes(p.Value)), StringComparer.Ordinal));
        files.Add("package.json", ArchetypePackJson.WriteDocument(inventory));
        if (files.Sum(p => (long)Utf8.GetByteCount(p.Value)) > MaximumPackageBytes)
            throw new InvalidDataException("Archetype package exceeds its byte limit.");
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Package revision exists; choose a new directory.");
        string parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("Package requires a parent directory.", nameof(directory));
        Directory.CreateDirectory(parent);
        string staging = Path.GetFullPath(Path.Combine(parent, ".archetype-package-" + Guid.NewGuid().ToString("N")));
        if (!string.Equals(Path.GetDirectoryName(staging), parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package staging directory escaped its intended parent.");
        Directory.CreateDirectory(staging);
        var reference = new ArchetypePackageReference(inventory.PackId, inventory.Version, destination, Hash(Utf8.GetBytes(files["package.json"])));
        try
        {
            foreach (var (name, json) in files) File.WriteAllText(Path.Combine(staging, name), json, Utf8);
            _ = Read(reference with { Directory = staging });
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                foreach (string name in files.Keys) File.Delete(Path.Combine(staging, name));
                Directory.Delete(staging);
            }
        }
        return reference;
    }

    internal static (ArchetypePackageDocument Package, long Bytes) Read(ArchetypePackageReference reference)
    {
        if (!ContentIdentifier.IsValid(reference.PackId.Value) || !ArchetypePackageValidation.IsVersion(reference.Version) ||
            !ArchetypePackageValidation.IsHash(reference.Sha256) || string.IsNullOrWhiteSpace(reference.Directory))
            throw new InvalidDataException("Package references require an exact ID, version, directory and inventory SHA-256.");
        string directory = Path.GetFullPath(reference.Directory);
        byte[] inventoryBytes = ReadBytes(Path.Combine(directory, "package.json"), 64 * 1024);
        if (!Hash(inventoryBytes).Equals(reference.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package inventory differs from its pinned SHA-256.");
        var inventory = ArchetypePackJson.ReadDocument<ArchetypePackageInventory>(Text(inventoryBytes));
        if (inventory.SchemaVersion != 1 || inventory.PackId != reference.PackId || inventory.Version != reference.Version || inventory.DocumentHashes is null ||
            RequiredFiles.Any(name => !inventory.DocumentHashes.ContainsKey(name)) ||
            inventory.DocumentHashes.Keys.Any(name => !RequiredFiles.Contains(name) && name != "native-table.json") ||
            inventory.DocumentHashes.Values.Any(hash => !ArchetypePackageValidation.IsHash(hash)))
            throw new InvalidDataException("Invalid package inventory, revision or document set.");
        if (Directory.EnumerateFileSystemEntries(directory).Count() != inventory.DocumentHashes.Count + 1)
            throw new InvalidDataException("Package contains missing or unexpected entries.");
        long total = inventoryBytes.Length;
        var documents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, expected) in inventory.DocumentHashes.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            byte[] bytes = ReadBytes(Path.Combine(directory, name), ArchetypePackJson.MaximumDocumentBytes);
            total += bytes.Length;
            if (total > MaximumPackageBytes || !Hash(bytes).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package content hash or total byte limit is invalid.");
            documents.Add(name, Text(bytes));
        }
        if (!inventoryBytes.AsSpan().SequenceEqual(ReadBytes(Path.Combine(directory, "package.json"), 64 * 1024)))
            throw new InvalidDataException("Package inventory changed during loading.");
        var manifest = ArchetypePackJson.ReadDocument<ArchetypePackManifest>(documents["manifest.json"]);
        if (manifest.PackId != reference.PackId || manifest.Version != reference.Version)
            throw new InvalidDataException("Manifest and pinned package revision disagree.");
        return (new(new(manifest,
            ArchetypePackJson.ReadDocument<ArchetypeDefinitionsDocument>(documents["definitions.json"]),
            ArchetypePackJson.ReadDocument<ArchetypeMembershipsDocument>(documents["memberships.json"]),
            ArchetypePackJson.ReadDocument<ArchetypeIdentityRulesDocument>(documents["identity-rules.json"]),
            ArchetypePackJson.ReadDocument<NativeArchetypeMappingsDocument>(documents["native-mappings.json"])),
            ArchetypePackJson.ReadDocument<ArchetypeCardCatalogDocument>(documents["catalog.json"]),
            ArchetypePackJson.ReadDocument<ArchetypeCardMappingsDocument>(documents["card-mappings.json"]),
            ArchetypePackJson.ReadDocument<ArchetypeMappingSourcesDocument>(documents["mapping-sources.json"]),
            documents.TryGetValue("native-table.json", out var native) ? ArchetypePackJson.ReadDocument<NativeArchetypeTableDocument>(native) : null), total);
    }

    internal static byte[] ReadBytes(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum) throw new InvalidDataException("Content document exceeds its byte limit.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }
    internal static string Text(byte[] bytes) => Utf8.GetString(bytes).TrimStart('\ufeff');
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
