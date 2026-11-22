using System.Diagnostics;

namespace TagForceSpecial.PC.Setup;

/// <summary>Opens the bundled desktop importer as the normal application entry point, telling it which host to start for Play Game.</summary>
public static class WorkbenchLauncher
{
    public static void Start(string workbenchExecutable, GameHost host, string? dataRoot)
    {
        if (!File.Exists(workbenchExecutable)) throw new InvalidOperationException("The importer workbench is missing. Rebuild or reinstall the PC application.");
        var start = new ProcessStartInfo(workbenchExecutable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Directory.GetCurrentDirectory() };
        foreach (string argument in GameHostCommand.ToWorkbench(host)) start.ArgumentList.Add(argument);
        if (dataRoot is not null) { start.ArgumentList.Add("--data-root"); start.ArgumentList.Add(dataRoot); }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The importer workbench could not start.");
    }
}
