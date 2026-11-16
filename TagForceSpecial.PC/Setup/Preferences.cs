namespace TagForceSpecial.PC.Setup;

public sealed record Preferences(
    [property: System.Text.Json.Serialization.JsonRequired] int SchemaVersion = 1,
    [property: System.Text.Json.Serialization.JsonRequired] string? ActiveGeneration = null,
    [property: System.Text.Json.Serialization.JsonRequired] string? SourcePath = null,
    [property: System.Text.Json.Serialization.JsonRequired] string? SelectedDeck = null,
    [property: System.Text.Json.Serialization.JsonRequired] uint OpponentSeed = 7,
    [property: System.Text.Json.Serialization.JsonRequired] bool IntegerScaling = false,
    Dictionary<string, string[]>? KeyBindings = null)
{
    /// <summary>Saved key bindings, or the defaults when none were saved.</summary>
    public App.Input.KeyBindings Bindings() => App.Input.KeyBindings.FromDocument(KeyBindings);

    public void Validate()
    {
        if (SchemaVersion != 1 || ActiveGeneration is { } generation && !Guid.TryParseExact(generation, "N", out _) ||
            SourcePath is { } source && (!Path.IsPathFullyQualified(source) || source.Length > 32768) ||
            SelectedDeck is { } deck && !ValidDeckKey(deck))
            throw new InvalidDataException("Preferences version or values are unsupported.");
        _ = Bindings(); // throws InvalidDataException for unknown, duplicate or reserved keys
    }
    public static bool ValidDeckKey(string key)
    {
        var parts = key.Split('/');
        return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out _) && parts[1].EndsWith(".json", StringComparison.Ordinal) &&
            Guid.TryParseExact(parts[1][..^5], "N", out _);
    }
}
public sealed record SetupIssue(string Code, string Message);
public sealed record SetupResult(bool Success, SetupIssue? Issue = null);
public sealed record ImportProgress(int Percent, string Message);
