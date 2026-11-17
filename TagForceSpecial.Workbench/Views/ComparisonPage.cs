using System.Windows.Controls;
using System.Windows.Data;
using TagForceSpecial.Import.Inspection;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Workbench.Infrastructure;

namespace TagForceSpecial.Workbench.Views;

internal sealed class ComparisonPage : DockPanel
{
    private readonly Workspace workspace;
    private readonly ComboBox before = new() { DisplayMemberPath = "SessionId", MinWidth = 340 };
    private readonly ComboBox after = new() { DisplayMemberPath = "SessionId", MinWidth = 340 };
    private readonly SearchTable table = new();
    public ComparisonPage(Workspace workspace)
    {
        this.workspace = workspace;
        var header = new StackPanel(); SetDock(header, Dock.Top); Children.Add(header);
        header.Children.Add(Controls.Text("Comparison / Two recorded import sessions", 22));
        foreach (var box in new[] { before, after }) box.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(Workspace.Sessions)) { Source = workspace });
        header.Children.Add(Controls.Text("Before / After (same data root)")); header.Children.Add(Controls.Row(before, after));
        header.Children.Add(Controls.Row(Controls.AsyncButton("Compare", CompareAsync), Controls.Button("Export differences…", () => Controls.ExportText(System.Text.Json.JsonSerializer.Serialize(table.Table.Items.Cast<ImportDifference>().ToArray(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), "comparison.json"))));
        header.Children.Add(Controls.Text("Missing historical stage outputs are reported as an error. This comparison does not claim PSP behavior equivalence or run tests.")); Children.Add(table);
    }
    private Task CompareAsync() => workspace.RunAsync(async () =>
    {
        if (before.SelectedItem is not ImportManifest a || after.SelectedItem is not ImportManifest b) throw new InvalidOperationException("Choose two sessions.");
        var changes = await Task.Run(() => ImportComparison.Compare(workspace.Storage, a, b)); table.Items(changes);
        workspace.Notice = $"{changes.Count} differences in metadata, stages, artifacts, original files, checks and normalized card counts.";
    });
    internal Task SmokeCompareAsync(ImportManifest a, ImportManifest b)
    { before.SelectedItem = a; after.SelectedItem = b; return CompareAsync(); }
}
