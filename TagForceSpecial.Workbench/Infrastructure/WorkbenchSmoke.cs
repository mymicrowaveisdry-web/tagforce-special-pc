using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TagForceSpecial.Import.Inspection;
using TagForceSpecial.Workbench.Views;

namespace TagForceSpecial.Workbench.Infrastructure;

/// <summary>Opt-in desktop smoke: opens real pages and reads a selected real import; never executes a unit-test runner.</summary>
internal static class WorkbenchSmoke
{
    public static async Task RunAsync(MainWindow window, Workspace workspace, string reportPath)
    {
        var observations = new List<string>(); bool passed = false;
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            int importSource = Array.IndexOf(args, "--smoke-source");
            if (importSource >= 0 && importSource + 1 < args.Length)
            {
                window.Navigate("Import");
                var model = window.Page<ImportPage>("Import").Model; model.Source = args[importSource + 1];
                int recipes = Array.IndexOf(args, "--smoke-recipes");
                if (recipes >= 0 && recipes + 1 < args.Length) model.Recipes = args[recipes + 1];
                await model.VerifyAsync(); Check(); observations.Add("Source verification UI: " + model.ProgressText);
                await model.ImportAsync(); Check(); observations.Add("Import UI: " + model.ProgressText);
                if (workspace.Selected?.Status is not (TagForceSpecial.Import.Manifest.ImportStatus.Succeeded or TagForceSpecial.Import.Manifest.ImportStatus.SucceededWithWarnings))
                    throw new InvalidDataException("Desktop import did not succeed: " + model.ProgressText);
                observations.Add("Import stages: " + string.Join("; ", workspace.Selected.Stages.Select(s => s.Id + "=" + s.Status)));
                observations.Add($"Import elapsed seconds: {(workspace.Selected.FinishedUtc - workspace.Selected.StartedUtc).TotalSeconds:0.0}");
                Capture("import");
                if (args.Contains("--smoke-cancel"))
                {
                    string activeId = TagForceSpecial.Import.Installed.InstalledGameReader.ReadActiveManifest(workspace.Storage)!.SessionId;
                    var interrupted = model.ImportAsync(); model.Cancel(); await interrupted; Check();
                    if (workspace.Selected?.Status != TagForceSpecial.Import.Manifest.ImportStatus.Cancelled ||
                        TagForceSpecial.Import.Installed.InstalledGameReader.ReadActiveManifest(workspace.Storage)!.SessionId != activeId)
                        throw new InvalidDataException("Cancelled import changed the active session or did not report cancellation.");
                    observations.Add("GUI cancellation: Cancelled; previous active session preserved."); await workspace.ReloadAsync();
                }
            }
            if (workspace.Selected is not { } selected) throw new InvalidOperationException("Desktop data smoke requires an imported game.");
            foreach (string name in new[] { "Dashboard", "Import", "Asset Explorer", "Decompilation Analysis", "Comparison", "Validation", "Logs", "Settings" })
            {
                window.Navigate(name); await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                observations.Add("Navigation opened: " + name);
            }
            var assets = window.Page<AssetExplorerPage>("Asset Explorer"); window.Navigate("Asset Explorer");
            await assets.LoadAsync(); Check(); observations.Add("Inventory UI: " + workspace.Notice);
            observations.Add($"Asset search/sort: {assets.SmokeFilter()} GIM-related rows");
            var inventory = await Task.Run(() => ImportInspection.Inventory(workspace.Storage, selected));
            var ehp = inventory.Files.First(f => f.Format == "ehp" && f.Parse == "parsed");
            await assets.InspectAsync(ehp); Check(); observations.Add("EHP inspector: " + workspace.Notice);
            var index = await Task.Run(() => ImportInspection.Inspect(selected, ehp));
            var member = index.Members.FirstOrDefault(m => m.Name.EndsWith(".gim", StringComparison.OrdinalIgnoreCase));
            if (member is not null) { await assets.InspectAsync(ehp, member.Name); Check(); observations.Add("GIM inspector: " + workspace.Notice); Capture("gim"); }
            await assets.CardsAsync(); Check(); observations.Add("Card UI: " + workspace.Notice);
            window.Navigate("Validation"); await window.Page<ValidationPage>("Validation").LoadAsync(); Check(); observations.Add("Historical validation: " + workspace.Notice);
            await window.Page<ValidationPage>("Validation").RevalidateAsync(); Check(); observations.Add("Revalidation: " + workspace.Notice);
            Capture("validation");
            var differences = await Task.Run(() => ImportComparison.Compare(workspace.Storage, selected, selected));
            if (differences.Count != 0) throw new InvalidDataException("A session differs from itself.");
            observations.Add("Manifest self-comparison: 0 differences");
            var prior = workspace.Sessions.FirstOrDefault(m => m.SessionId != selected.SessionId && m.Status is TagForceSpecial.Import.Manifest.ImportStatus.Succeeded or TagForceSpecial.Import.Manifest.ImportStatus.SucceededWithWarnings);
            if (prior is not null)
            {
                window.Navigate("Comparison"); await window.Page<ComparisonPage>("Comparison").SmokeCompareAsync(prior, selected); Check();
                observations.Add("Two-session comparison UI: " + workspace.Notice); Capture("comparison");
            }
            var logs = await Task.Run(() => ImportInspection.Logs(workspace.Storage, selected)); observations.Add($"Persisted logs: {logs.Entries.Count}; unreadable: {logs.Unreadable}");
            window.Navigate("Logs"); await window.Page<LogsPage>("Logs").LoadAsync(); Check(); Capture("logs");
            window.Navigate("Decompilation Analysis"); await window.Page<AnalysisPage>("Decompilation Analysis").LoadAsync(); Check(); Capture("analysis");
            var evidence = await Task.Run(() => DecompilationEvidence.Load(workspace.Settings.RepositoryRoot)); observations.Add($"Function candidates: {evidence.Functions.Length}");
            var counts = await Task.Run(() => CardCountAudit.Run(workspace.Storage, selected));
            observations.Add($"Card count audit: raw {counts.RawPropertyRows}; sentinel {counts.SentinelRows}; linked {counts.LinkedCards}; normalized {counts.NormalizedCards}.");
            window.Navigate("Dashboard"); await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
            Capture("dashboard"); passed = true;
        }
        catch (Exception e) { observations.Add(e.ToString()); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { passed, observations }, new JsonSerializerOptions { WriteIndented = true }));
        Application.Current.Shutdown(passed ? 0 : 1);
        void Check() { if (workspace.LastError is { } error) throw new InvalidOperationException("Desktop operation failed: " + workspace.Notice, error); }
        void Capture(string page)
        {
            window.UpdateLayout();
            string screenshot = Path.GetFullPath(reportPath) + "." + page + ".png";
            Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(screenshot)) encoder.Save(stream);
            observations.Add("Actual window rendered: " + screenshot);
        }
    }
}
