using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TagForceSpecial.Effects.Archetypes;

[JsonConverter(typeof(ArchetypeIdConverter))]
public readonly record struct ArchetypeId
{
    public string Value { get; }
    public ArchetypeId(string value) { ContentIdentifier.Require(value); Value = value; }
    public override string ToString() => Value ?? "";
}

[JsonConverter(typeof(ArchetypePackIdConverter))]
public readonly record struct ArchetypePackId
{
    public string Value { get; }
    public ArchetypePackId(string value) { ContentIdentifier.Require(value); Value = value; }
    public override string ToString() => Value ?? "";
}

[JsonConverter(typeof(SourceCardIdConverter))]
public readonly record struct SourceCardId
{
    public string Value { get; }
    public SourceCardId(string value) { ContentIdentifier.Require(value); Value = value; }
    public override string ToString() => Value ?? "";
}

internal static class ContentIdentifier
{
    private static readonly Regex Pattern = new(@"\A[a-z][a-z0-9.-]*:[a-z0-9][a-z0-9._/-]*\z", RegexOptions.CultureInvariant);
    public static bool IsValid(string? value) => value is { Length: <= 160 } && Pattern.IsMatch(value);
    public static void Require(string value)
    {
        if (!IsValid(value)) throw new ArgumentException("A lowercase namespaced content identifier is required.", nameof(value));
    }
}

public abstract class ContentIdentifierConverter<T> : JsonConverter<T>
{
    protected abstract T Create(string value);
    protected abstract string Value(T value);
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !ContentIdentifier.IsValid(reader.GetString()))
            throw new JsonException("A lowercase namespaced content identifier is required.");
        return Create(reader.GetString()!);
    }
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        string text = Value(value);
        if (!ContentIdentifier.IsValid(text)) throw new JsonException("An uninitialized content identifier cannot be serialized.");
        writer.WriteStringValue(text);
    }
}

public sealed class ArchetypeIdConverter : ContentIdentifierConverter<ArchetypeId>
{
    protected override ArchetypeId Create(string value) => new(value);
    protected override string Value(ArchetypeId value) => value.Value;
}

public sealed class ArchetypePackIdConverter : ContentIdentifierConverter<ArchetypePackId>
{
    protected override ArchetypePackId Create(string value) => new(value);
    protected override string Value(ArchetypePackId value) => value.Value;
}

public sealed class SourceCardIdConverter : ContentIdentifierConverter<SourceCardId>
{
    protected override SourceCardId Create(string value) => new(value);
    protected override string Value(SourceCardId value) => value.Value;
}
