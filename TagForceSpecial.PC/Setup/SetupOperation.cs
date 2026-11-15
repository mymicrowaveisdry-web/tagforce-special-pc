using System.Collections.Concurrent;

namespace TagForceSpecial.PC.Setup;

public sealed record SetupCompletion(bool Success, string Message);

/// <summary>Owns one background setup job, its cancellation and queued CPU-only progress.</summary>
public sealed class SetupOperation(SetupStore store) : IDisposable
{
    private Task<SetupResult>? task;
    private CancellationTokenSource? cancellation;
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
                ? store.PresentationNotice ?? "Local game data is ready. Open Decks to prepare and play."
                : result.Issue!.Message);
        }
        catch (Exception e) when (SetupStore.Recoverable(e))
        { completion = new(false, "Setup could not finish. Retry or select your ISO again."); }
        task = null;
        return completion;
    }

    public void Dispose()
    {
        Cancel(); cancellation?.Dispose();
    }

    private sealed class QueuedProgress(ConcurrentQueue<ImportProgress> queue) : IProgress<ImportProgress>
    { public void Report(ImportProgress value) => queue.Enqueue(value); }
}
