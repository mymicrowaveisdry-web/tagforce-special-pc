using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TagForceSpecial.Cards.CatalogStorage;

public sealed record LoadedCardCatalog(CardCatalogPackageManifest Manifest, CardCatalog Catalog);

/// <summary>Versioned JSON catalog packages.</summary>
public static class CardCatalogPackage
{
    public const int SchemaVersion = 1;
    public const int MaximumCards = 100_000;
    public const int MaximumCardsBytes = 64 * 1024 * 1024;
    private const string CardsFile = "cards.json";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    /// <summary>Load fully before returning. Checksums detect corruption, not source authenticity.</summary>
    public static LoadedCardCatalog Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var manifest = Parse<CardCatalogPackageManifest>(ReadBounded(Path.Combine(directory, "manifest.json"), 64 * 1024));
        ValidateManifest(manifest);
        var bytes = ReadBounded(Path.Combine(directory, CardsFile), MaximumCardsBytes);
        if (!string.Equals(Hash(bytes), manifest.CardsSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Catalog content checksum does not match its manifest.");
        var document = Parse<CardCatalogDocument>(bytes);
        if (document.SchemaVersion != SchemaVersion || document.Source != manifest.Source ||
            document.Cards is null || document.Cards.Length != manifest.CardCount)
            throw new InvalidDataException("Catalog document version, source or count differs from its manifest.");
        try
        {
            var cards = document.Cards.Select(c =>
            {
                if (c is null || c.Name is null || c.Name.Length > 2048 || c.Description is null || c.Description.Length > 16384 ||
                    c.Source is null || c.Statistics is null || c.EffectReferences is null)
                    throw new InvalidDataException("Catalog definition fields are missing or exceed their bounds.");
                return new CardDefinition(c.GameCardId, c.Name, c.Description, new(c.Statistics.Attack, c.Statistics.Defense, c.Statistics.Level), c.Codes?.ToModel(), c.SourceRow?.ToModel(),
                    c.Source, c.Kind, new(c.EffectState, c.EffectReferences));
            });
            var catalog = new CardCatalog(document.Source, cards);
            return new(manifest, catalog);
        }
        catch (ArgumentException ex) { throw new InvalidDataException("Invalid catalog definitions.", ex); }
    }

    /// <summary>Create a new package through a sibling temporary directory; existing versions are never overwritten.</summary>
    public static CardCatalogPackageManifest Write(string directory, CardCatalog catalog, CardCatalogPackageInfo identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Count is < 1 or > MaximumCards) throw new InvalidDataException("Catalog count exceeds the package bounds.");
        var document = new CardCatalogDocument
        {
            SchemaVersion = SchemaVersion, Source = catalog.Source,
            Cards = catalog.Cards.Select(c => new CardDefinitionDocument
            {
                GameCardId = c.GameCardId, Name = c.Name, Description = c.Description,
                Statistics = new() { Attack = c.Statistics.Attack, Defense = c.Statistics.Defense, Level = c.Statistics.Level },
                Codes = c.Codes is { } codes ? new()
                {
                    Attribute = codes.Attribute, Type = codes.Type, RawKind = codes.RawKind, GetterKind = codes.GetterKind,
                    Icon = codes.Icon, Field23To26 = codes.Field23To26, Field27To30 = codes.Field27To30
                } : null,
                SourceRow = c.SourceRow is { } row ? new()
                {
                    InternalIndex = row.InternalIndex, PropertyWord0 = row.PropertyWord0, PropertyWord1 = row.PropertyWord1,
                    DescriptionBitOffset = row.DescriptionBitOffset
                } : null,
                Source = c.Source!, Kind = c.Kind,
                EffectState = c.Effects!.State, EffectReferences = c.Effects.References.ToArray()
            }).ToArray()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        if (bytes.Length > MaximumCardsBytes) throw new InvalidDataException("Catalog document exceeds the package limit.");
        var manifest = new CardCatalogPackageManifest(SchemaVersion, identity, catalog.Source, catalog.Count, CardsFile, Hash(bytes));
        ValidateManifest(manifest);
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (Path.GetDirectoryName(destination) is not { } parent || Path.GetFileName(destination).Length == 0)
            throw new ArgumentException("A package directory below a parent is required.", nameof(directory));
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Catalog version already exists; choose a new revision directory.");
        Directory.CreateDirectory(parent);
        string temporary = Path.Combine(parent, ".catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllBytes(Path.Combine(temporary, CardsFile), bytes);
            File.WriteAllBytes(Path.Combine(temporary, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
            _ = Load(temporary); // Validate the complete on-disk package before publication.
            Directory.Move(temporary, destination);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                File.Delete(Path.Combine(temporary, CardsFile));
                File.Delete(Path.Combine(temporary, "manifest.json"));
                Directory.Delete(temporary); // Only the two known files; never recursively delete an arbitrary path.
            }
        }
        return manifest;
    }

    private static void ValidateManifest(CardCatalogPackageManifest manifest)
    {
        if (manifest is null || manifest.SchemaVersion != SchemaVersion || manifest.Identity is not { } identity ||
            manifest.Source is null || manifest.CardCount is < 1 or > MaximumCards || manifest.CardsFile != CardsFile ||
            manifest.CardsSha256 is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit) ||
            new[] { identity.CatalogId, identity.Revision, identity.Locale, identity.SourceBuild }
                .Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 256))
            throw new InvalidDataException("Invalid or unsupported catalog package manifest.");
        try { _ = new CardCatalog(manifest.Source, []); }
        catch (ArgumentException ex) { throw new InvalidDataException("Invalid catalog source provenance.", ex); }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] ReadBounded(string path, int maximum)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length < 1 || file.Length > maximum) throw new InvalidDataException("Catalog file exceeds its size bounds.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new InvalidDataException("Catalog file changed during loading.");
        return bytes;
    }

    private static T Parse<T>(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicateProperties(doc.RootElement);
            return doc.RootElement.Deserialize<T>(Json) ?? throw new InvalidDataException("Missing catalog document.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Malformed catalog JSON.", ex); }
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate catalog JSON property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var entry in value.EnumerateArray()) RejectDuplicateProperties(entry);
    }
}
