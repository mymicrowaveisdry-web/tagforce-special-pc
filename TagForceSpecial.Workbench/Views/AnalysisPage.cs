using System.Windows;
using System.Windows.Controls;
using TagForceSpecial.Import.Inspection;
using TagForceSpecial.Workbench.Infrastructure;

namespace TagForceSpecial.Workbench.Views;

internal sealed class AnalysisPage : DockPanel
{
    private readonly Workspace workspace;
    private readonly TextBlock metrics = Controls.Text("Load repository function registries to inspect available evidence.");
    private readonly SearchTable table = new();
    public AnalysisPage(Workspace workspace)
    {
        this.workspace = workspace;
        var header = new StackPanel(); SetDock(header, Dock.Top); Children.Add(header);
        header.Children.Add(Controls.Text("Decompilation Analysis / Persisted function evidence", 22));
        var details = new TextBox { IsReadOnly = true, AcceptsReturn = true, Height = 180, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        header.Children.Add(Controls.Row(Controls.AsyncButton("Load evidence", LoadAsync), Controls.Button("Copy selected evidence", () => Clipboard.SetText(details.Text)),
            Controls.Button("Open registry", () =>
            { if (table.Table.SelectedItem is FunctionEvidence row) Controls.Open(Path.Combine(workspace.Settings.RepositoryRoot, row.Registry)); })));
        header.Children.Add(metrics); SetDock(details, Dock.Bottom); Children.Add(details); Children.Add(table);
        table.Table.SelectionChanged += (_, _) => { if (table.Table.SelectedItem is FunctionEvidence row) details.Text = row.Details + "\n" + row.EvidenceIntegrity; };
    }
    internal Task LoadAsync() => workspace.RunAsync(async () =>
    {
        var data = await Task.Run(() => DecompilationEvidence.Load(workspace.Settings.RepositoryRoot));
        table.Items(data.Functions); metrics.Text = string.Join("\n", data.Metrics);
        workspace.Notice = $"Measured {data.MeasuredUtc:u}. " + (data.Diagnostics.Length == 0 ? "Registry loaded." : string.Join(" ", data.Diagnostics.Take(4)));
    });
}
