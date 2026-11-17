using System.Windows;
using System.Windows.Controls;
using TagForceSpecial.Import.Diagnostics;
using TagForceSpecial.Import.Inspection;
using TagForceSpecial.Workbench.Infrastructure;

namespace TagForceSpecial.Workbench.Views;

internal sealed class LogsPage : DockPanel
{
    private readonly SearchTable table = new();
    private readonly Workspace workspace;
    public LogsPage(Workspace workspace, Action<string> navigate)
    {
        this.workspace = workspace;
        var header = new StackPanel(); SetDock(header, Dock.Top); Children.Add(header);
        header.Children.Add(Controls.Text("Logs / Diagnostics", 22));
        header.Children.Add(Controls.Row(Controls.AsyncButton("Load selected session", LoadAsync), Controls.Button("Copy selected", () => Clipboard.SetText(SelectedText())),
            Controls.Button("Export visible…", () => Controls.ExportText(string.Join("\n", table.Table.Items.Cast<LogEntry>().Select(e => System.Text.Json.JsonSerializer.Serialize(e))), "diagnostics.jsonl")),
            Controls.Button("Clear view", () => table.Items(Array.Empty<LogEntry>())),
            Controls.Button("Related assets", () => { navigate("Asset Explorer"); workspace.Notice = "Search Asset Explorer for " + (table.Table.SelectedItem as LogEntry)?.Resource; }),
            Controls.Button("Validation", () => navigate("Validation"))));
        header.Children.Add(Controls.Text("Search filters severity, stage/component, resource, session and message. Clearing the view retains persisted history. Exports can include local paths; review before sharing."));
        Children.Add(table);
    }
    internal Task LoadAsync() => workspace.RunAsync(async () =>
    {
        var selected = workspace.Selected ?? throw new InvalidOperationException("Select an import first.");
        var result = await Task.Run(() => ImportInspection.Logs(workspace.Storage, selected)); table.Items(result.Entries);
        workspace.Notice = $"{result.Entries.Count} entries loaded; {result.Unreadable} malformed lines skipped.";
    });
    private string SelectedText() => System.Text.Json.JsonSerializer.Serialize(table.Table.SelectedItems.Cast<LogEntry>().ToArray(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}
