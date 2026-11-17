using System.Windows.Controls;
using System.Windows.Data;
using TagForceSpecial.Workbench.Infrastructure;

namespace TagForceSpecial.Workbench.Views;

internal sealed class DashboardPage : DockPanel
{
    public DashboardPage(Workspace workspace, Action<string> navigate)
    {
        var header = new StackPanel(); SetDock(header, Dock.Top); Children.Add(header);
        header.Children.Add(Controls.Text("Dashboard", 24));
        header.Children.Add(Controls.Row(Controls.AsyncButton("Refresh", () => workspace.RunAsync(workspace.ReloadAsync)),
            Controls.Button("New import", () => navigate("Import")), Controls.Button("Assets", () => navigate("Asset Explorer")),
            Controls.Button("Validation", () => navigate("Validation")), Controls.Button("Logs", () => navigate("Logs"))));
        var sessions = new ComboBox { DisplayMemberPath = "SessionId" };
        sessions.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(Workspace.Sessions)) { Source = workspace });
        sessions.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(Workspace.Selected)) { Source = workspace, Mode = BindingMode.TwoWay }); header.Children.Add(sessions);
        header.Children.Add(Controls.Button("Open selected manifest", () =>
        {
            if (workspace.Selected is { } m) Controls.Open(workspace.Storage.PathFor(TagForceSpecial.Import.ImportLayout.SessionManifest(m.SessionId)));
        }));
        header.Children.Add(Controls.AsyncButton("Audit card counts", () => workspace.RunAsync(async () =>
        {
            var selected = workspace.Selected ?? throw new InvalidOperationException("Select an import first.");
            var counts = await Task.Run(() => TagForceSpecial.Import.Inspection.CardCountAudit.Run(workspace.Storage, selected));
            workspace.Notice = $"Raw rows {counts.RawPropertyRows:N0} − {counts.SentinelRows} sentinel = {counts.LinkedCards:N0} linked = {counts.NormalizedCards:N0} normalized cards.";
        })));
        var details = new TextBox { IsReadOnly = true, TextWrapping = System.Windows.TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Refresh()
        {
            details.Text = workspace.Selected is { } m
                ? $"{m.Source.Title ?? m.Source.DisplayName}\nGame ID: {m.Source.GameId ?? "Unknown"}\nRegion: Not measured\nSource: {m.Source.Kind} / {m.Source.Path}\nBytes: {m.Source.Length:N0}\nSHA-256: {m.Source.Sha256}\nRecognized: {m.Source.Recognized}; structurally valid: {m.Source.StructurallyValid}; supported: {m.Source.Supported}\nImported: {m.FinishedUtc:u}\nDuration: {(m.FinishedUtc - m.StartedUtc).TotalSeconds:0.0}s\nImporter: {m.ImporterVersion}; schema: {m.SchemaVersion}\nResult: {m.Status}; warnings: {m.Warnings}; errors: {m.Errors}\n\n" + string.Join("\n", m.Stages.Select(s => $"{s.Name}: {s.Status} — {s.Message}"))
                : "No import available. Select a source on the Import page.";
        }
        workspace.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Workspace.Selected)) Refresh(); }; Refresh(); Children.Add(details);
    }
}
