using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TagForceSpecial.Workbench.Infrastructure;
using TagForceSpecial.Workbench.Views;

namespace TagForceSpecial.Workbench;

public sealed class MainWindow : Window
{
    private readonly Workspace workspace;
    private readonly ContentControl content = new();
    private readonly Dictionary<string, FrameworkElement> pages = [];
    private readonly ListBox navigation = new() { Width = 210, Margin = new Thickness(0, 12, 12, 0) };
    public MainWindow(Workspace workspace)
    {
        this.workspace = workspace;
        SetResourceReference(StyleProperty, typeof(Window));
        Title = "Tag Force Special / Importer & Analysis Workbench"; Width = 1440; Height = 900; MinWidth = 1000; MinHeight = 650;
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var header = new StackPanel(); header.Children.Add(Controls.Text("IMPORTER & ANALYSIS WORKBENCH  ·  " + TagForceSpecial.Import.ToolIdentity.ImporterVersion.Split('+')[0], 22));
        header.Children.Add(Controls.AsyncButton("Play Game", () => GameLauncher.StartAsync(workspace)));
        var summary = Controls.Text(""); summary.SetBinding(TextBlock.TextProperty, new Binding(nameof(Workspace.Summary)) { Source = workspace }); header.Children.Add(summary);
        var activity = new ProgressBar { Height = 3, Maximum = 1 };
        activity.SetBinding(ProgressBar.IsIndeterminateProperty, new Binding(nameof(Workspace.Busy)) { Source = workspace }); header.Children.Add(activity);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = Controls.Text(""); footer.SetBinding(TextBlock.TextProperty, new Binding(nameof(Workspace.Notice)) { Source = workspace }); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var nav = navigation;
        DockPanel.SetDock(nav, Dock.Left); root.Children.Add(nav); root.Children.Add(content);
        pages["Dashboard"] = new DashboardPage(workspace, Navigate);
        pages["Import"] = new ImportPage(workspace, Navigate);
        pages["Settings"] = new SettingsPage(workspace);
        pages["Asset Explorer"] = new AssetExplorerPage(workspace);
        pages["Validation"] = new ValidationPage(workspace);
        pages["Logs"] = new LogsPage(workspace, Navigate);
        pages["Comparison"] = new ComparisonPage(workspace);
        pages["Decompilation Analysis"] = new AnalysisPage(workspace);
        foreach (string name in new[] { "Dashboard", "Import", "Asset Explorer", "Decompilation Analysis", "Comparison", "Validation", "Logs", "Settings" }) nav.Items.Add(name);
        nav.SelectionChanged += (_, _) => { if (nav.SelectedItem is string name) Navigate(name); };
        nav.SelectedIndex = 0;
        Loaded += async (_, _) =>
        {
            await workspace.RunAsync(workspace.ReloadAsync);
            if (workspace.Selected is null) Navigate("Import");
            string[] args = Environment.GetCommandLineArgs();
            int report = Array.IndexOf(args, "--smoke-report");
            if (report >= 0 && report + 1 < args.Length) await WorkbenchSmoke.RunAsync(this, workspace, args[report + 1]);
        };
        Closing += OnClosing;
    }
    internal void Navigate(string name)
    {
        if (!Equals(navigation.SelectedItem, name)) navigation.SelectedItem = name;
        content.Content = pages[name];
    }
    internal T Page<T>(string name) where T : FrameworkElement => (T)pages[name];
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (workspace.Busy) { e.Cancel = true; workspace.Notice = "Cancel the current operation and wait for it to finish before closing."; }
    }
}
