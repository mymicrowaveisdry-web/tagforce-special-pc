using TagForceSpecial.Import.Installed;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Workbench.Infrastructure;

public sealed class Workspace(WorkbenchSettings settings) : Observable
{
    public WorkbenchSettings Settings { get; private set; } = settings;
    public PrivateStorage Storage => new(Settings.DataRoot);
    private ImportManifest? selected;
    public ImportManifest? Selected { get => selected; set { if (Set(ref selected, value)) Changed(nameof(Summary)); } }
    private ImportManifest[] sessions = [];
    public ImportManifest[] Sessions { get => sessions; private set => Set(ref sessions, value); }
    private bool busy;
    public bool Busy { get => busy; set => Set(ref busy, value); }
    public Exception? LastError { get; private set; }
    private string notice = "Select a data root in Settings or start an import.";
    public string Notice { get => notice; set => Set(ref notice, value); }
    public string Summary => Selected is { } m ? $"{m.Source.BuildId ?? "Unsupported build"}  /  {m.Status}  /  {m.SessionId}" : "No import selected";
    public async Task ReloadAsync()
    {
        var result = await Task.Run(() => (Sessions: InstalledGameReader.ListSessions(Storage).ToArray(), Active: InstalledGameReader.ReadActiveManifest(Storage)));
        Sessions = result.Sessions; Selected = Sessions.FirstOrDefault(s => s.SessionId == result.Active?.SessionId) ?? result.Active ?? Sessions.FirstOrDefault();
    }
    public void SaveSettings(WorkbenchSettings value) { value.Save(); Settings = value; Changed(nameof(Settings)); }
    public async Task RunAsync(Func<Task> work)
    {
        if (Busy) { Notice = "Wait for the current operation to finish."; return; }
        Busy = true; LastError = null;
        try { await work(); }
        catch (OperationCanceledException e) { LastError = e; Notice = "Operation cancelled safely."; }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { LastError = e; Notice = e.Message; }
        finally { Busy = false; }
    }
}
