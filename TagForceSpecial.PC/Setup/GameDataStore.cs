using TagForceSpecial.Import.Installed;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.PC.Setup;

/// <summary>The PC's private data root: preferences with their backup, and the validated active import the game plays from.</summary>
public sealed class GameDataStore
{
    public PrivateStorage Storage { get; }
    public Preferences Preferences { get; private set; } = new();
    /// <summary>Set only after the importer's output re-validated; null means an import is required.</summary>
    public InstalledGame? Game { get; private set; }
    /// <summary>Non-fatal problems of a usable import, such as missing title artwork.</summary>
    public IReadOnlyList<ImportIssue> GameIssues { get; private set; } = [];
    public SetupIssue? Issue { get; private set; }
    private bool preferencesAccepted = true;

    public GameDataStore(string root)
    {
        Storage = new(root);
        if (!File.Exists(Storage.PathFor("preferences.json"))) return;
        try { var preferences = Storage.Read<Preferences>("preferences.json", 65536); preferences.Validate(); Preferences = preferences; }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { Issue = PreferencesInvalid; preferencesAccepted = false; }
    }

    private static SetupIssue PreferencesInvalid => new("PreferencesInvalid",
        "Preferences are unreadable or from another version. Restore the backup or reset preferences.");

    public void SavePreferences(Preferences value)
    {
        value.Validate();
        // Backup only the last accepted in-memory document, never a corrupt file.
        if (preferencesAccepted) Storage.Write("preferences.backup.json", Preferences);
        Storage.Write("preferences.json", value); Preferences = value; preferencesAccepted = true;
    }

    public SetupResult RestorePreferences()
    {
        try
        {
            var backup = Storage.Read<Preferences>("preferences.backup.json", 65536); backup.Validate();
            Storage.Write("preferences.json", backup); Preferences = backup; preferencesAccepted = true;
            return Reload();
        }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { Issue = PreferencesInvalid; return new(false, Issue); }
    }

    /// <summary>Re-opens the active import through the importer's reader; every output is verified again.</summary>
    public SetupResult Reload()
    {
        var result = InstalledGameReader.Open(Storage);
        Game = result.Game;
        GameIssues = result.Game is null ? [] : result.Issues;
        if (result.Game is null)
        {
            var first = result.Issues[0];
            Issue = new(first.Code, first.Message);
            return new(false, Issue);
        }
        if (preferencesAccepted) Issue = null;
        return new(true);
    }
}
