using System.Windows;
using TagForceSpecial.Workbench.Infrastructure;

namespace TagForceSpecial.Workbench;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = WorkbenchSettings.Load();
        for (int i = 0; i < e.Args.Length; i++)
            if (e.Args[i] == "--data-root" && i + 1 < e.Args.Length) settings = settings with { DataRoot = Path.GetFullPath(e.Args[++i]) };
        var window = new MainWindow(new Workspace(settings));
        MainWindow = window; window.Show();
    }
}
