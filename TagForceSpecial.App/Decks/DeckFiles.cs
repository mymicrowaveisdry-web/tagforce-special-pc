using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using TagForceSpecial.Core.Decks;

namespace TagForceSpecial.PC.Decks;

/// <summary>Versioned local deck/collection persistence; no ownership quantities or duel instances enter Cards.</summary>
public static class DeckFiles
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16
    };
    private sealed record DeckDocument
    {
        public required int SchemaVersion { get; init; }
        public required string DeckId { get; init; }
        public required string Revision { get; init; }
        public required string OwnerId { get; init; }
        public required string CatalogId { get; init; }
        public required string CatalogRevision { get; init; }
        public required string SourceBuild { get; init; }
        public required string PolicyId { get; init; }
        public required string PolicyRevision { get; init; }
        public required uint[] Main { get; init; }
        public required uint[] Extra { get; init; }
        public required uint[] Side { get; init; }
    }
    private sealed record CollectionDocument(int SchemaVersion, string Revision, string OwnerId,
        string CatalogId, string CatalogRevision, string SourceBuild, Dictionary<uint, int> Counts);

    public static DeckDefinition ReadDeck(string path)
    {
        var d = Read<DeckDocument>(path);
        if (d.SchemaVersion != DeckDefinition.CurrentSchema || d.Main is null || d.Extra is null || d.Side is null)
            throw new InvalidDataException("Unsupported or incomplete deck document.");
        try { return new(d.DeckId, d.Revision, d.OwnerId, d.CatalogId, d.CatalogRevision, d.SourceBuild, d.PolicyId, d.PolicyRevision, d.Main, d.Extra, d.Side, d.SchemaVersion); }
        catch (ArgumentException ex) { throw new InvalidDataException("Invalid deck identity or sections.", ex); }
    }
    public static PlayerCollection ReadCollection(string path)
    {
        var d = Read<CollectionDocument>(path);
        if (d.SchemaVersion != 1 || d.Counts is null) throw new InvalidDataException("Unsupported or incomplete collection document.");
        try { return new(d.OwnerId, d.CatalogId, d.CatalogRevision, d.SourceBuild, d.Revision, d.Counts, d.SchemaVersion); }
        catch (ArgumentException ex) { throw new InvalidDataException("Invalid collection quantities or identity.", ex); }
    }
    public static void WriteDeck(string path, DeckDefinition deck) => Write(path, new DeckDocument
    {
        SchemaVersion = deck.SchemaVersion, DeckId = deck.DeckId, Revision = deck.Revision, OwnerId = deck.OwnerId,
        CatalogId = deck.CatalogId, CatalogRevision = deck.CatalogRevision, SourceBuild = deck.SourceBuild,
        PolicyId = deck.PolicyId, PolicyRevision = deck.PolicyRevision, Main = deck.Main.ToArray(), Extra = deck.Extra.ToArray(), Side = deck.Side.ToArray()
    });
    public static void WriteCollection(string path, PlayerCollection collection) => Write(path,
        new CollectionDocument(collection.SchemaVersion, collection.Revision, collection.OwnerId,
            collection.CatalogId, collection.CatalogRevision, collection.SourceBuild, collection.Counts.OrderBy(c => c.Key).ToDictionary()));

    private static T Read<T>(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > 1024 * 1024) throw new InvalidDataException("Deck/collection document exceeds its bounds.");
        try
        {
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
            RejectDuplicates(document.RootElement);
            if (typeof(T) == typeof(CollectionDocument) && document.RootElement.TryGetProperty("counts", out var counts) && counts.ValueKind == JsonValueKind.Object)
                foreach (var entry in counts.EnumerateObject())
                    if (!uint.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out uint id) ||
                        id.ToString(CultureInfo.InvariantCulture) != entry.Name)
                        throw new InvalidDataException("Collection IDs must be canonical unsigned decimal keys.");
            return document.RootElement.Deserialize<T>(Json) ?? throw new InvalidDataException("Empty deck document.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Malformed deck/collection JSON.", ex); }
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject())
            { if (!seen.Add(p.Name)) throw new InvalidDataException("Duplicate JSON field."); RejectDuplicates(p.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var e in value.EnumerateArray()) RejectDuplicates(e);
    }
    private static void Write<T>(string path, T document)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Deck document exceeds its bounds.");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes); // New revision files only; existing decks are never overwritten implicitly.
    }
}
