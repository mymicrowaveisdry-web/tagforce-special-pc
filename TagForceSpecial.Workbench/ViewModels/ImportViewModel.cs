using System.Collections.ObjectModel;
using System.Windows;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Import.Diagnostics;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Stages;
using TagForceSpecial.Workbench.Infrastructure;

namespace TagForceSpecial.Workbench.ViewModels;

public sealed class ImportViewModel(Workspace workspace) : Observable
{
    private string source = workspace.Settings.RecentSources.FirstOrDefault() ?? "", recipes = workspace.Settings.RecipeLibrary ?? "",
        fontDirectory = workspace.Settings.FontDirectory ?? "", progressText = "Select an ISO or extracted game folder.";
    private bool importRecipes = workspace.Settings.ImportRecipes;
    private double fraction;
    private bool indeterminate;
    private CancellationTokenSource? cancellation;
    public string Source { get => source; set => Set(ref source, value); }
    public string Recipes { get => recipes; set => Set(ref recipes, value); }
    public bool ImportRecipes { get => importRecipes; set => Set(ref importRecipes, value); }
    public string FontDirectory { get => fontDirectory; set => Set(ref fontDirectory, value); }
    public string ProgressText { get => progressText; private set => Set(ref progressText, value); }
    public double Fraction { get => fraction; private set => Set(ref fraction, value); }
    public bool Indeterminate { get => indeterminate; private set => Set(ref indeterminate, value); }
    public bool Presentation { get; set; } = true;
    public bool Inventory { get; set; } = true;
    public bool ReuseCache { get; set; } = true;
    public ObservableCollection<LogEntry> Logs { get; } = [];
    public ObservableCollection<StageRecord> Stages { get; } = [];
    public Task VerifyAsync() => workspace.RunAsync(async () =>
    {
        string path = Source;
        using var stop = new CancellationTokenSource(); cancellation = stop; Indeterminate = true;
        try
        {
            var outcome = await Task.Run(() => SourceStage.Run(path, [GameBuildProfile.English21], null, stop.Token));
            using (outcome.Source)
                ProgressText = $"Recognized: {outcome.Identity.Recognized}  /  Structurally valid: {outcome.Identity.StructurallyValid}  /  Supported: {outcome.Identity.Supported}\n{outcome.Result.Message}";
            workspace.Notice = outcome.Result.Message;
        }
        finally { cancellation = null; Indeterminate = false; }
    });
    public Task ImportAsync() => workspace.RunAsync(async () =>
    {
        string path = Source, root = workspace.Settings.DataRoot;
        var options = new ImportOptions(Presentation: Presentation, Inventory: Inventory, ReuseCache: ReuseCache, Recipes: ImportRecipes,
            RecipeLibrary: string.IsNullOrWhiteSpace(Recipes) ? null : Recipes, FontDirectory: string.IsNullOrWhiteSpace(FontDirectory) ? null : FontDirectory);
        using var stop = new CancellationTokenSource(); cancellation = stop;
        Logs.Clear(); Stages.Clear();
        var dispatcher = Application.Current.Dispatcher;
        var progress = new InlineProgress<ImportProgress>(p => dispatcher.InvokeAsync(() =>
        {
            ProgressText = $"{p.StageName}: {p.Message}";
            Fraction = (p.Fraction ?? 0) * 100; Indeterminate = p.Fraction is null;
        }));
        try
        {
            var manifest = await Task.Run(() => ImportPipeline.Run(new(path, root, options), progress, stop.Token,
                entry => dispatcher.InvokeAsync(() => Logs.Add(entry))));
            foreach (var stage in manifest.Stages) Stages.Add(stage);
            workspace.SaveSettings(workspace.Settings with { RecentSources = new[] { path }.Concat(workspace.Settings.RecentSources).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
                ImportRecipes = options.Recipes, RecipeLibrary = options.RecipeLibrary, FontDirectory = options.FontDirectory });
            await workspace.ReloadAsync(); workspace.Selected = workspace.Sessions.FirstOrDefault(s => s.SessionId == manifest.SessionId) ?? manifest;
            ProgressText = $"{manifest.Status}: {manifest.Warnings} warnings / {manifest.Errors} errors. Session {manifest.SessionId}";
            workspace.Notice = ProgressText;
        }
        finally { cancellation = null; Indeterminate = false; }
    });
    public void Cancel() { cancellation?.Cancel(); workspace.Notice = "Cancellation requested; waiting for a safe stage boundary."; }
}
