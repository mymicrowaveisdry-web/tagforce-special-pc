using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TagForceSpecial.Workbench.Infrastructure;
using TagForceSpecial.Workbench.ViewModels;

namespace TagForceSpecial.Workbench.Views;

internal sealed class ImportPage : DockPanel
{
    public ImportViewModel Model { get; }
    public ImportPage(Workspace workspace, Action<string> navigate)
    {
        Model = new(workspace);
        var form = new StackPanel(); SetDock(form, Dock.Top); Children.Add(form);
        form.Children.Add(Controls.Text("Import / Select → Verify → Configure → Run → Review", 22));
        form.Children.Add(Controls.Text("Source ISO or extracted folder (original inputs are read-only)"));
        form.Children.Add(Controls.Field(Model, nameof(Model.Source)));
        form.Children.Add(Controls.Row(Controls.Button("Select ISO…", () => { if (Controls.File("Original ISO", "PSP ISO|*.iso") is { } p) Model.Source = p; }),
            Controls.Button("Select folder…", () => { if (Controls.Folder("Extracted PSP game") is { } p) Model.Source = p; }),
            Controls.AsyncButton("Verify source", Model.VerifyAsync)));
        var recent = new ComboBox { ItemsSource = workspace.Settings.RecentSources, ToolTip = "Recent sources" };
        recent.SelectionChanged += (_, _) => { if (recent.SelectedItem is string path) Model.Source = path; }; form.Children.Add(recent);
        form.Children.Add(Controls.Text("Optional recipes/viewer.json (blank keeps the previous recipe library)"));
        form.Children.Add(Controls.Field(Model, nameof(Model.Recipes)));
        form.Children.Add(Controls.Button("Select recipe library…", () => { if (Controls.File("Prepared recipe library", "Recipe library|viewer.json") is { } p) Model.Recipes = p; }));
        form.Children.Add(Controls.Row(Controls.Check("Presentation textures", Model, nameof(Model.Presentation)), Controls.Check("Full file inventory", Model, nameof(Model.Inventory)), Controls.Check("Reuse verified outputs", Model, nameof(Model.ReuseCache))));
        form.Children.Add(Controls.Text("Card catalog, validation and reports are included. Audio/dialogue conversion is not supported."));
        form.Children.Add(Controls.Row(Controls.AsyncButton("Start / repeat import", Model.ImportAsync), Controls.Button("Cancel safely", Model.Cancel),
            Controls.Button("Browse assets", () => navigate("Asset Explorer")), Controls.Button("Validation report", () => navigate("Validation"))));
        var progress = new ProgressBar { Height = 12, Maximum = 100, Margin = new Thickness(4) };
        progress.SetBinding(ProgressBar.ValueProperty, new Binding(nameof(Model.Fraction)) { Source = Model, Mode = BindingMode.OneWay });
        progress.SetBinding(ProgressBar.IsIndeterminateProperty, new Binding(nameof(Model.Indeterminate)) { Source = Model }); form.Children.Add(progress);
        var status = Controls.Text(""); status.SetBinding(TextBlock.TextProperty, new Binding(nameof(Model.ProgressText)) { Source = Model }); form.Children.Add(status);
        var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Stage results", Content = new DataGrid { ItemsSource = Model.Stages } });
        tabs.Items.Add(new TabItem { Header = "Live diagnostics", Content = new DataGrid { ItemsSource = Model.Logs } }); Children.Add(tabs);
    }
}
