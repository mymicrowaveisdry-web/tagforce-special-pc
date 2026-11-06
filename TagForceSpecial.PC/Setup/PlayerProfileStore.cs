using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.Setup;

/// <summary>Schema 2 adds the original Options values; schema 1 files load with new-game defaults for them.</summary>
public sealed record PlayerProfile(int SchemaVersion, string Name, DuelistLevelChoice Level, DateTimeOffset SavedUtc, PlayerOptions? Options = null);

/// <summary>Native PC profile only. Original PSP DATA0 files are encrypted and are neither read nor written here.</summary>
public sealed class PlayerProfileStore(PrivateStorage storage)
{
    public const int CurrentSchema = 2;
    private const string Path = "profile.json";
    public PlayerProfile? Current { get; private set; }
    public bool Present => File.Exists(storage.PathFor(Path));

    public bool TryLoad()
    {
        try
        {
            var value = storage.Read<PlayerProfile>(Path, 65536);
            if (value.SchemaVersion is not (1 or CurrentSchema) || value.Name is not { Length: > 0 and <= TitleFlow.NameLimit } || !Enum.IsDefined(value.Level)) return false;
            if (value.SchemaVersion == CurrentSchema && value.Options?.Valid() != true) return false;
            Current = value.SchemaVersion == 1 ? value with { SchemaVersion = CurrentSchema, Options = PlayerOptions.Defaults() } : value;
            return true;
        }
        catch (Exception e) when (SetupStore.Recoverable(e)) { return false; }
    }

    public void Save(string name, DuelistLevelChoice level, PlayerOptions options)
    {
        if (!options.Valid()) throw new ArgumentException("Options values are outside the original ranges.", nameof(options));
        var value = new PlayerProfile(CurrentSchema, name, level, DateTimeOffset.UtcNow, options);
        storage.Write(Path, value);
        Current = value;
    }
}
