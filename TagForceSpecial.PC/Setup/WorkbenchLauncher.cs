using System.Diagnostics;

namespace TagForceSpecial.PC.Setup;

/// <summary>Opens the bundled desktop importer as the normal application entry point.</summary>
public static class WorkbenchLauncher
{
    public static void Start(string? dataRoot)
    {
        string executable = Path.Combine(AppContext.BaseDirectory, "workbench", "TagForceSpecial.Workbench.exe");
        if (!File.Exists(executable)) throw new InvalidOperationException("The importer workbench is missing. Rebuild or reinstall the PC application.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Directory.GetCurrentDirectory() };
        start.ArgumentList.Add("--game-host"); start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "TagForceSpecial.PC.exe"));
        if (dataRoot is not null) { start.ArgumentList.Add("--data-root"); start.ArgumentList.Add(dataRoot); }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The importer workbench could not start.");
    }
}
