using System.Windows.Controls;
using TagForceSpecial.Import;
using TagForceSpecial.Import.Inspection;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Validation;
using TagForceSpecial.Workbench.Infrastructure;

namespace TagForceSpecial.Workbench.Views;

internal sealed class ValidationPage : DockPanel
{
    private readonly Workspace workspace;
    private readonly SearchTable table = new();
    private ValidationReport? report;
    public ValidationPage(Workspace workspace)
    {
        this.workspace = workspace;
        var header = new StackPanel(); SetDock(header, Dock.Top); Children.Add(header);
        header.Children.Add(Controls.Text("Validation / Recorded checks and current artifact integrity", 22));
        header.Children.Add(Controls.Row(Controls.AsyncButton("Load import report", LoadAsync), Controls.AsyncButton("Revalidate outputs", RevalidateAsync),
            Controls.Button("Export JSON…", () => { if (report is not null) Controls.ExportText(System.Text.Encoding.UTF8.GetString(ManifestJson.Serialize(report)), "validation.json"); }),
            Controls.Button("Export Markdown…", () => { if (report is not null) Controls.ExportText(ValidationReportWriter.Markdown(report), "validation.md"); })));
        header.Children.Add(Controls.Text("Skipped and NotSupported checks are never counted as passed. Revalidation checks artifacts; repeat the import to recheck the original source."));
        Children.Add(table);
    }
    public Task LoadAsync() => workspace.RunAsync(async () =>
    {
        var selected = workspace.Selected ?? throw new InvalidOperationException("Select an import first.");
        report = await Task.Run(() => ManifestJson.Parse<ValidationReport>(workspace.Storage.ReadBytes(ImportLayout.SessionValidation(selected.SessionId), 16 * 1024 * 1024)));
        Show();
    });
    public Task RevalidateAsync() => workspace.RunAsync(async () =>
    {
        var selected = workspace.Selected ?? throw new InvalidOperationException("Select an import first.");
        report = await Task.Run(() => ImportRevalidation.Run(workspace.Storage, selected)); Show();
    });
    private void Show()
    {
        table.Items(report!.Checks);
        workspace.Notice = string.Join(" / ", Enum.GetValues<CheckStatus>().Select(s => $"{s}: {report.Count(s)}"));
    }
}
