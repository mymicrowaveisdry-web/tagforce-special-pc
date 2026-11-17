using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace TagForceSpecial.Workbench.Views;

internal static class Controls
{
    public static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, Action click)
    { var b = new Button { Content = text }; b.Click += (_, _) => Safe(click); return b; }
    public static Button AsyncButton(string text, Func<Task> click)
    { var b = new Button { Content = text }; b.Click += async (_, _) => await click(); return b; }
    public static TextBox Field(object source, string property)
    { var box = new TextBox(); box.SetBinding(TextBox.TextProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); return box; }
    public static CheckBox Check(string label, object source, string property)
    { var c = new CheckBox { Content = label }; c.SetBinding(CheckBox.IsCheckedProperty, new Binding(property) { Source = source }); return c; }
    public static WrapPanel Row(params UIElement[] children)
    { var p = new WrapPanel(); foreach (var c in children) p.Children.Add(c); return p; }
    public static string? Folder(string title)
    { var d = new OpenFolderDialog { Title = title }; return d.ShowDialog() == true ? d.FolderName : null; }
    public static string? File(string title, string filter)
    { var d = new OpenFileDialog { Title = title, Filter = filter }; return d.ShowDialog() == true ? d.FileName : null; }
    public static void ExportText(string text, string name)
    { var d = new SaveFileDialog { FileName = name, Filter = "Text or JSON|*.*" }; if (d.ShowDialog() == true) System.IO.File.WriteAllText(d.FileName, text); }
    public static void Open(string path) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    private static void Safe(Action click)
    {
        try { click(); }
        catch (Exception e) when (TagForceSpecial.Import.Storage.PrivateStorage.Recoverable(e) || e is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        { MessageBox.Show(e.Message, "Workbench operation could not finish", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}

internal sealed class SearchTable : DockPanel
{
    public DataGrid Table { get; } = new();
    public TextBox Search { get; } = new() { ToolTip = "Search any displayed field" };
    public SearchTable()
    {
        SetDock(Search, Dock.Top); Children.Add(Search); Children.Add(Table);
        Table.AutoGeneratingColumn += (_, e) =>
        {
            if (e.PropertyName is not ("Status" or "Severity" or "Parse")) return;
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(e.PropertyName));
            text.SetBinding(TextBlock.ForegroundProperty, new Binding(e.PropertyName) { Converter = new StatusColor() });
            e.Column = new DataGridTemplateColumn { Header = e.PropertyName, SortMemberPath = e.PropertyName, CellTemplate = new DataTemplate { VisualTree = text } };
        };
        Search.TextChanged += (_, _) => Apply();
    }
    public void Items(IEnumerable items) { Table.ItemsSource = items; Apply(); }
    private void Apply()
    {
        if (Table.ItemsSource is null) return;
        var view = CollectionViewSource.GetDefaultView(Table.ItemsSource);
        string query = Search.Text;
        view.Filter = item => string.IsNullOrWhiteSpace(query) || System.Text.Json.JsonSerializer.Serialize(item).Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class StatusColor : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value?.ToString() switch
        {
            "Passed" or "Succeeded" or "Reused" or "parsed" => "#80D5A6",
            "Failed" or "Error" or "failed" => "#FF7B86",
            "Warning" or "Partial" or "SucceededWithWarnings" => "#F4C56A",
            "Running" or "Info" => "#7DB5FF",
            _ => "#AFBECE"
        }));
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
