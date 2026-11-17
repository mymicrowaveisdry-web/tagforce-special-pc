using System.Text.Json;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Workbench.Infrastructure;

public sealed record WorkbenchSettings(string DataRoot, string RepositoryRoot, string[] RecentSources,
    bool ImportRecipes = true, string? RecipeLibrary = null, string? FontDirectory = null)
{
    public static string SettingsPath => Environment.GetEnvironmentVariable("TFSP_WORKBENCH_SETTINGS") is { Length: > 0 } path ? Path.GetFullPath(path) :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TagForceSpecial.Workbench", "settings.json");
    public static WorkbenchSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath)) return JsonSerializer.Deserialize<WorkbenchSettings>(File.ReadAllText(SettingsPath))
                ?? throw new InvalidDataException("Settings are empty.");
        }
        catch (Exception e) when (PrivateStorage.Recoverable(e))
        { System.Windows.MessageBox.Show("Settings could not be read; defaults will be used. " + e.Message, "Workbench settings"); }
        return new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TagForceSpecial"), Directory.GetCurrentDirectory(), []);
    }
    public void Save()
    {
        var storage = new PrivateStorage(Path.GetDirectoryName(SettingsPath)!);
        storage.WriteBytes(Path.GetFileName(SettingsPath), JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
