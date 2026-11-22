using System.Diagnostics;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.Workbench.Infrastructure;

internal static class GameLauncher
{
    /// <summary>Godot editor used to run the game from a repository checkout (kept in an ignored folder beside the solution).</summary>
    private const string RepositoryGodot = "Godot-win-x64/Godot_v4.7.2-stable_mono_win64.exe";

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
            var host = GameHostCommand.FromWorkbench(Environment.GetCommandLineArgs()) ?? FindHost();
            if (!File.Exists(host.Executable)) throw new FileNotFoundException("Build or install the game host before starting the game.", host.Executable);
            var start = new ProcessStartInfo(host.Executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(host.Executable)! };
            // Hosts that load assemblies from memory cannot hash their decoder; the game checks its import against this one.
            foreach (string argument in GameHostCommand.PlayArguments(host, workspace.Settings.DataRoot, TagForceSpecial.Import.ToolIdentity.DecoderSha256))
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The game could not start.");
            workspace.Notice = "Game started.";
        });
        if (workspace.LastError is null && !workspace.Busy) System.Windows.Application.Current.MainWindow.Close();
    }

    /// <summary>The exported game beside the bundled Workbench, else the repository's Godot editor running the game project.</summary>
    private static GameHost FindHost()
    {
        string bundled = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "TagForceSpecial.PC.exe"));
        if (File.Exists(bundled)) return new(bundled, []);
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "ygo-tag-force-special-decomp.slnx")))
                return new(Path.Combine(folder.FullName, RepositoryGodot), ["--path", Path.Combine(folder.FullName, "TagForceSpecial.PC")]);
        return new(bundled, []);
    }
}
