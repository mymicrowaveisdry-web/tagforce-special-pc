using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TagForceSpecial.Import.Inspection;
using TagForceSpecial.Import.Stages;
using TagForceSpecial.Workbench.Infrastructure;

namespace TagForceSpecial.Workbench.Views;

internal sealed class AssetExplorerPage : DockPanel
{
    private readonly Workspace workspace;
    private readonly SearchTable files = new();
    private readonly TreeView tree = new();
    private readonly TextBox details = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Image image = new() { Stretch = Stretch.Uniform, MaxHeight = 300 };
    private readonly SearchTable members = new();
    private InventoryRecord[] inventory = [];
    private InventoryRecord? current;
    private BitmapSource? bitmap;
    public AssetExplorerPage(Workspace workspace)
    {
        this.workspace = workspace;
        var header = new StackPanel(); SetDock(header, Dock.Top); Children.Add(header);
        header.Children.Add(Controls.Text("Asset Explorer / Original files and normalized cards", 22));
        header.Children.Add(Controls.Row(Controls.AsyncButton("Load selected inventory", LoadAsync),
            Controls.AsyncButton("Card catalog", CardsAsync), Controls.Button("Copy resource details", () => Clipboard.SetText(details.Text)),
            Controls.Button("Export metadata…", () => Controls.ExportText(details.Text, "resource.txt")),
            Controls.Button("Export preview PNG…", ExportPng)));
        var grid = new Grid(); Children.Add(grid);
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(210) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(tree); VirtualizingStackPanel.SetIsVirtualizing(tree, true);
        var split1 = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(split1, 1); grid.Children.Add(split1);
        Grid.SetColumn(files, 2); grid.Children.Add(files);
        var split2 = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(split2, 3); grid.Children.Add(split2);
        var inspector = new Grid(); inspector.RowDefinitions.Add(new() { Height = new GridLength(2, GridUnitType.Star) });
        inspector.RowDefinitions.Add(new() { Height = GridLength.Auto }); inspector.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        inspector.Children.Add(details); Grid.SetRow(image, 1); inspector.Children.Add(image); Grid.SetRow(members, 2); inspector.Children.Add(members);
        Grid.SetColumn(inspector, 4); grid.Children.Add(inspector);
        files.Table.SelectionChanged += async (_, _) =>
        {
            if (files.Table.SelectedItem is InventoryRecord record) await InspectAsync(record);
            else if (files.Table.SelectedItem is { } card) details.Text = System.Text.Json.JsonSerializer.Serialize(card, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        };
        members.Table.SelectionChanged += async (_, _) =>
        { if (current is { } record && members.Table.SelectedItem is TagForceSpecial.Assets.EhpEntry entry) await InspectAsync(record, entry.Name); };
        tree.SelectedItemChanged += (_, _) =>
        {
            if (tree.SelectedItem is TreeViewItem { Tag: string prefix }) files.Items(inventory.Where(f => f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray());
        };
        details.Text = "Load an import inventory. Original resources are read from the source only on selection and checked against their recorded hash. Unknown files retain their metadata and header bytes.";
    }
    public Task LoadAsync() => workspace.RunAsync(async () =>
    {
        var selected = workspace.Selected ?? throw new InvalidOperationException("Select an import on the Dashboard.");
        var data = await Task.Run(() => ImportInspection.Inventory(workspace.Storage, selected));
        inventory = data.Files; files.Items(inventory); tree.Items.Clear(); current = null; members.Items(Array.Empty<object>());
        var all = new TreeViewItem { Header = $"All original files ({inventory.Length})", Tag = "", IsExpanded = true }; tree.Items.Add(all);
        var folders = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase) { [""] = all };
        foreach (var record in inventory)
        {
            string prefix = "";
            foreach (string part in record.Path.Split('/')[..^1])
            {
                string parent = prefix; prefix += part + "/";
                if (folders.ContainsKey(prefix)) continue;
                var node = new TreeViewItem { Header = part, Tag = prefix }; folders[parent].Items.Add(node); folders[prefix] = node;
            }
        }
        workspace.Notice = $"{inventory.Length:N0} source files; {inventory.Count(f => f.Format == "unknown")} unknown; {inventory.Count(f => f.Parse == "failed")} archive indexes failed. Search includes format, category and parser status; column headers sort.";
    });
    internal Task CardsAsync() => workspace.RunAsync(async () =>
    {
        var selected = workspace.Selected ?? throw new InvalidOperationException("Select an import first.");
        var cards = await Task.Run(() => ImportInspection.Cards(workspace.Storage, selected));
        files.Items(cards); current = null; members.Items(Array.Empty<object>()); image.Source = bitmap = null;
        workspace.Notice = $"{cards.Count:N0} normalized cards; select a row for decoded properties and source provenance.";
    });
    internal Task InspectAsync(InventoryRecord record, string? member = null) => workspace.RunAsync(async () =>
    {
        var selected = workspace.Selected ?? throw new InvalidOperationException("Select an import first.");
        current = record; image.Source = bitmap = null;
        details.Text = $"{record.Path}\n{record.Format}: {record.Reason}\nSHA-256: {record.Sha256}\nHeader: {record.Header}\n{record.ParseDetail}\nReading source…";
        var inspection = await Task.Run(() => ImportInspection.Inspect(selected, record, member));
        details.Text = inspection.Details + "\n\n" + Hex(inspection.Header);
        if (member is null) members.Items(inspection.Members);
        if (inspection.Texture is { } texture)
        {
            byte[] bgra = (byte[])texture.Rgba.Clone();
            for (int i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
            bitmap = BitmapSource.Create(texture.Width, texture.Height, 96, 96, PixelFormats.Bgra32, null, bgra, texture.Width * 4);
            bitmap.Freeze(); image.Source = bitmap;
        }
        workspace.Notice = "Resource inspected; original file hash matches the selected inventory.";
    });
    private void ExportPng()
    {
        if (bitmap is null) { workspace.Notice = "Select a decodable GIM file or archive member first."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG image|*.png", FileName = "preview.png" };
        if (dialog.ShowDialog() != true) return;
        try { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(dialog.FileName); encoder.Save(stream); workspace.Notice = "PNG exported."; }
        catch (Exception e) when (TagForceSpecial.Import.Storage.PrivateStorage.Recoverable(e)) { workspace.Notice = e.Message; }
    }
    private static string Hex(byte[] bytes) => string.Join("\n", Enumerable.Range(0, (bytes.Length + 15) / 16).Select(row =>
        $"{row * 16:X8}  " + Convert.ToHexString(bytes.AsSpan(row * 16, Math.Min(16, bytes.Length - row * 16)))));
    internal int SmokeFilter()
    {
        files.Search.Text = "gim";
        int count = files.Table.Items.Count;
        if (count == 0 || count >= inventory.Length) throw new InvalidDataException("Asset search did not filter the displayed rows.");
        System.Windows.Data.CollectionViewSource.GetDefaultView(files.Table.ItemsSource).SortDescriptions.Add(new("Length", System.ComponentModel.ListSortDirection.Descending));
        files.Search.Clear(); return count;
    }
}
