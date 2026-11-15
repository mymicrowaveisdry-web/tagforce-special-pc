using System.Collections.Concurrent;

namespace TagForceSpecial.PC.Setup;

public sealed record SetupCompletion(bool Success, string Message);

/// <summary>Owns one background setup job, its cancellation and queued CPU-only progress.</summary>
public sealed class SetupOperation(SetupStore store) : IDisposable
{
    private Task<SetupResult>? task;
    private CancellationTokenSource? cancellation;
    private string? successMessage;
    private readonly ConcurrentQueue<ImportProgress> updates = new();
    public bool Busy => task is not null;
    public ImportProgress Progress { get; private set; } = new(0, "Select your original ISO to begin");

    public void Validate(bool initial = false)
    {
        if (initial) Progress = new(0, "Checking existing local game data");
        task = Task.Run(store.ValidateActive);
    }

    public bool Import(string source)
    {
        if (task is { IsCompleted: false }) return false;
        cancellation?.Dispose(); cancellation = new();
        var token = cancellation.Token;
        Progress = new(0, "Checking original image");
        task = Task.Run(() => store.Import(source, new QueuedProgress(updates), token));
        return true;
    }

    /// <summary>Copies and checks a prepared recipe library against the active catalog; a failure leaves the previous library.</summary>
    public bool ImportRecipes(string file, TagForceSpecial.Cards.CardCatalog catalog)
    {
        if (task is not null) return false;
        cancellation?.Dispose(); cancellation = new();
        var token = cancellation.Token;
        successMessage = "Recipe library imported. Continue to Game to open Recipe Viewer.";
        Progress = new(0, "Copying and checking recipe data");
        task = Task.Run(() =>
        {
            try { _ = new Recipes.RecipeLibraryStore(store.Storage).Import(file, catalog, token, new QueuedProgress(updates)); return new SetupResult(true); }
            catch (OperationCanceledException) { return new SetupResult(false, new("RecipeImportCanceled", "Recipe import canceled; the previous library is unchanged.")); }
            catch (Exception e) when (SetupStore.Recoverable(e))
            {
                Console.Error.WriteLine("RECIPE_IMPORT_FAILED " + e.Message);
                return new SetupResult(false, new("RecipeImportFailed", "Recipe import failed. Select a prepared recipes/viewer.json library."));
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
        try
        {
            var result = task.GetAwaiter().GetResult();
            completion = new(result.Success, result.Success
                ? store.PresentationNotice ?? successMessage ?? "Local game data is ready. Choose Continue to Game."
                : result.Issue!.Message);
        }
        catch (Exception e) when (SetupStore.Recoverable(e))
        { completion = new(false, "Setup could not finish. Retry or select your ISO again."); }
        task = null; successMessage = null;
        return completion;
    }

    public void Dispose()
    {
        Cancel(); cancellation?.Dispose();
    }

    private sealed class QueuedProgress(ConcurrentQueue<ImportProgress> queue) : IProgress<ImportProgress>
    { public void Report(ImportProgress value) => queue.Enqueue(value); }
}
