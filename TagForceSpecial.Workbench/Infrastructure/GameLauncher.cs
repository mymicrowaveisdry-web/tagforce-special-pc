using System.Diagnostics;
using TagForceSpecial.Import.Installed;

namespace TagForceSpecial.Workbench.Infrastructure;

internal static class GameLauncher
{
    public static async Task StartAsync(Workspace workspace)
    {
        if (workspace.Busy) { workspace.Notice = "Wait for the current operation before starting the game."; return; }
        await workspace.RunAsync(async () =>
        {
            var result = await Task.Run(() => InstalledGameReader.Open(workspace.Storage));
            if (!result.Usable || result.Game?.Presentation is null)
                throw new InvalidOperationException("Import and validate the game catalog and presentation before playing.");
            var missing = await Task.Run(() => TagForceSpecial.Import.Resources.GameFonts.Ensure(workspace.Storage,
                result.Game.Manifest?.Source.Path, workspace.Settings.FontDirectory));
            if (missing.Count > 0) throw new InvalidOperationException("PSP fonts are missing. Select a local folder containing ltn4.pgf and ltn0.pgf on Import, then repeat import.");
            string[] args = Environment.GetCommandLineArgs();
            int host = Array.IndexOf(args, "--game-host");
            string executable = host >= 0 && host + 1 < args.Length ? Path.GetFullPath(args[host + 1]) : FindHost();
            if (!File.Exists(executable)) throw new FileNotFoundException("Build the PC application before starting the game.", executable);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
            start.ArgumentList.Add("--game"); start.ArgumentList.Add("--data-root"); start.ArgumentList.Add(workspace.Settings.DataRoot);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The game could not start.");
            workspace.Notice = "Game started.";
        });
        if (workspace.LastError is null && !workspace.Busy) System.Windows.Application.Current.MainWindow.Close();
    }
    private static string FindHost()
    {
        string bundled = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "TagForceSpecial.PC.exe"));
        if (File.Exists(bundled)) return bundled;
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "ygo-tag-force-special-decomp.slnx")))
                return Path.Combine(folder.FullName, "TagForceSpecial.PC", "bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", "TagForceSpecial.PC.exe");
        return bundled;
    }
}
