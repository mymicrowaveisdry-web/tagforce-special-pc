using System.Windows.Controls;
using TagForceSpecial.Workbench.Infrastructure;
using TagForceSpecial.Import.Maintenance;

namespace TagForceSpecial.Workbench.Views;

internal sealed class SettingsPage : StackPanel
{
    public SettingsPage(Workspace workspace)
    {
        Children.Add(Controls.Text("Settings", 24));
        Children.Add(Controls.Text("Game data root")); var root = new TextBox { Text = workspace.Settings.DataRoot }; Children.Add(root);
        Children.Add(Controls.Button("Choose data folder…", () => { if (Controls.Folder("Game data root") is { } p) root.Text = p; }));
        Children.Add(Controls.Text("Repository root (function inventory and evidence)")); var repo = new TextBox { Text = workspace.Settings.RepositoryRoot }; Children.Add(repo);
        Children.Add(Controls.Button("Choose repository…", () => { if (Controls.Folder("Repository") is { } p) repo.Text = p; }));
        Children.Add(Controls.AsyncButton("Save settings", () => workspace.RunAsync(async () =>
        {
            workspace.SaveSettings(workspace.Settings with { DataRoot = Path.GetFullPath(root.Text), RepositoryRoot = Path.GetFullPath(repo.Text) });
            await workspace.ReloadAsync(); workspace.Notice = "Settings saved.";
        })));
        Children.Add(Controls.Text("Maintenance previews the owned files and asks for confirmation. Saved player data is retained."));
        Children.Add(Controls.AsyncButton("Clean previous importer data…", () => Clean(workspace, true)));
        Children.Add(Controls.AsyncButton("Clean unused stage outputs…", () => Clean(workspace, false)));
        Children.Add(Controls.Text("Settings: " + WorkbenchSettings.SettingsPath));
    }
    private static Task Clean(Workspace workspace, bool legacy) => workspace.RunAsync(async () =>
    {
        var storage = workspace.Storage;
        var plan = await Task.Run(() => legacy ? Cleanup.PlanLegacy(storage) : Cleanup.PlanUnusedStages(storage));
        if (plan.Blocked is not null) { workspace.Notice = plan.Blocked; return; }
        if (plan.Items.Count == 0) { workspace.Notice = "No importer-owned files need cleanup."; return; }
        string text = string.Join("\n", plan.Items.Select(i => i.RelativePath)) + $"\n\nRemove {plan.Bytes:N0} bytes?";
        if (System.Windows.MessageBox.Show(text, "Confirm cleanup", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
        await Task.Run(() => Cleanup.Execute(storage, plan)); workspace.Notice = "Cleanup completed.";
    });
}
