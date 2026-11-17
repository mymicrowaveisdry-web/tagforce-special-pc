using System.Collections.Concurrent;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.PC.Setup;

public sealed record SetupCompletion(bool Success, string Message);

/// <summary>Owns one background import or verification job for the in-game import screen; progress is queued for the game thread.</summary>
public sealed class ImportOperation(GameDataStore store) : IDisposable
{
    private Task<SetupCompletion>? task;
    private CancellationTokenSource? cancellation;
    private readonly ConcurrentQueue<ImportProgress> updates = new();
    public bool Busy => task is not null;
    public ImportProgress Progress { get; private set; } = new("idle", "Import", "Select your original ISO to begin.");

    public void Validate(bool initial = false)
    {
        if (task is not null) return;
        Progress = new("verify", "Verify", initial ? "Checking existing local game data" : "Verifying local game data");
        task = Task.Run(() =>
        {
            var result = store.Reload();
            return new SetupCompletion(result.Success, result.Success ? ReadyMessage() : result.Issue!.Message);
        });
    }

    /// <param name="recipeLibrary">A prepared <c>recipes/viewer.json</c>, or null to keep the current or previous recipe import.</param>
    public bool Import(string source, string? recipeLibrary = null)
    {
        if (task is not null) return false;
        cancellation?.Dispose(); cancellation = new();
        var token = cancellation.Token;
        Progress = new("source", "Recognize source", "Checking the original image");
        task = Task.Run(() =>
        {
            try
            {
                var manifest = ImportPipeline.Run(new(source, store.Storage.Root, new ImportOptions(RecipeLibrary: recipeLibrary)),
                    new InlineProgress<ImportProgress>(updates.Enqueue), token);
                var reload = store.Reload();
                return Completion(manifest, reload.Success);
            }
            catch (Exception e) when (PrivateStorage.Recoverable(e))
            {
                // A held import lock or an unwritable data folder; the previous import stays active.
                return new SetupCompletion(false, "Import could not start. Close other importers using this data folder and check its free space.");
            }
        });
        return true;
    }

    public void Cancel() => cancellation?.Cancel();

    public SetupCompletion? Poll()
    {
        while (updates.TryDequeue(out var update)) Progress = update;
        if (task is not { IsCompleted: true }) return null;
        SetupCompletion completion;
        try { completion = task.GetAwaiter().GetResult(); }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { completion = new(false, "Setup could not finish. Retry or select your ISO again."); }
        task = null;
        return completion;
    }

    private SetupCompletion Completion(ImportManifest manifest, bool usable)
    {
        string Stage(string id) => manifest.Stages.Single(s => s.Id == id).Message ?? "";
        return manifest.Status switch
        {
            ImportStatus.Succeeded or ImportStatus.SucceededWithWarnings when usable => new(true, ReadyMessage()),
            ImportStatus.Partial => new(usable, "Import finished partially: " + string.Join(" ",
                manifest.Stages.Where(s => s.Status == StageStatus.Failed).Select(s => s.Name + " failed."))),
            ImportStatus.Cancelled => new(false, "Import stopped safely. Finished parts are kept and reused next time."),
            _ => new(false, Stage(manifest.Stages.FirstOrDefault(s => s.Status == StageStatus.Failed)?.Id ?? "publish") is { Length: > 0 } m
                ? m : "Import failed; the previous game data stays active.")
        };
    }

    private string ReadyMessage()
    {
        string message = "Local game data is ready. Choose Continue to Game.";
        if (store.GameIssues.Count > 0) message += " " + store.GameIssues[0].Message;
        if (store.Game?.RecipeRoot is null) message += " No recipe library is imported yet.";
        return message;
    }

    public void Dispose() { Cancel(); cancellation?.Dispose(); }
}
