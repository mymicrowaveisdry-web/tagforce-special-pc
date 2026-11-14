using System.Text.Json;
using System.Text.Json.Serialization;

namespace TagForceSpecial.Effects.Archetypes;

/// <summary>Strict JSON for separate authoring documents; performs no file loading or gameplay registration.</summary>
public static class ArchetypePackJson
{
    public const int SchemaVersion = 1;
    public const int MaximumDocumentBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static T ReadDocument<T>(string json) where T : class
    {
        ArgumentNullException.ThrowIfNull(json);
        RequireDocumentType<T>();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
            throw new JsonException("Archetype document exceeds the byte limit.");
        using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicates(parsed.RootElement);
        return JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("A document cannot be null.");
    }

    public static string WriteDocument<T>(T document) where T : class
    {
        ArgumentNullException.ThrowIfNull(document);
        RequireDocumentType<T>();
        string json = JsonSerializer.Serialize(document, Options);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
            throw new JsonException("Archetype document exceeds the byte limit.");
        return json;
    }

    public static ArchetypePackDocument Read(string manifest, string definitions, string memberships,
        string identityRules, string nativeMappings, ArchetypeValidationContext context,
        ArchetypeValidationMode mode = ArchetypeValidationMode.ReviewedContent)
    {
        var pack = new ArchetypePackDocument(ReadDocument<ArchetypePackManifest>(manifest),
            ReadDocument<ArchetypeDefinitionsDocument>(definitions), ReadDocument<ArchetypeMembershipsDocument>(memberships),
            ReadDocument<ArchetypeIdentityRulesDocument>(identityRules), ReadDocument<NativeArchetypeMappingsDocument>(nativeMappings));
        var issues = ArchetypePackValidator.Validate(pack, context, mode);
        if (issues.Count != 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, issues.Select(i => $"{i.Path}: {i.Code}: {i.Message}")));
        return pack;
    }

    private static void RequireDocumentType<T>()
    {
        Type type = typeof(T);
        if (type != typeof(ArchetypePackManifest) && type != typeof(ArchetypeDefinitionsDocument) &&
            type != typeof(ArchetypeMembershipsDocument) && type != typeof(ArchetypeIdentityRulesDocument) &&
            type != typeof(NativeArchetypeMappingsDocument) && type != typeof(NativeArchetypeTableDocument) &&
            type != typeof(NativeArchetypeLabelsDocument) && type != typeof(ArchetypeCardCatalogDocument) &&
            type != typeof(ArchetypeMappingSourcesDocument) && type != typeof(ArchetypeCardMappingsDocument) &&
            type != typeof(ArchetypePackageInventory) && type != typeof(ArchetypeStartupConfiguration))
            throw new ArgumentException("Only the separate archetype authoring document types are supported.");
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate JSON property: {property.Name}");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
}
