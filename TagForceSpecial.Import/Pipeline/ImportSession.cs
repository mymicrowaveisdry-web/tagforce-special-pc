using TagForceSpecial.Import.Diagnostics;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Import.Pipeline;

/// <summary>State of one running import: stage records, collected checks, outputs and the session log.</summary>
public sealed class ImportSession
{
    private readonly IProgress<ImportProgress>? progress;
    private readonly List<StageRecord> records;
    private readonly List<ValidationCheck> checks = [];
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public PrivateStorage Storage { get; }
    public ImportOptions Options { get; }
    public DiagnosticLog Log { get; }
    public SourceIdentity Source { get; set; } = new("unknown", "", "", 0, null, null, null, null, false, false, false, null, null, []);
    public ImportOutputs Outputs { get; set; } = new(null, null, null, null, null);
    public ImportManifest? Manifest { get; set; }
    public bool Cancelled { get; private set; }
    public IReadOnlyList<StageRecord> Records => records;
    public IReadOnlyList<ValidationCheck> Checks => checks;

    public ImportSession(PrivateStorage storage, ImportOptions options, IProgress<ImportProgress>? progress, Action<LogEntry>? onLog)
    {
        Storage = storage; Options = options; this.progress = progress;
        Log = new(Id, storage.PathFor(ImportLayout.SessionLog(Id)));
        if (onLog is not null) Log.Written += onLog;
        records = ImportPipeline.Stages.Select(s => new StageRecord(s.Id, s.Name, StageStatus.Pending, null, null, null, null, 0, 0, null, null, null)).ToList();
    }

    /// <summary>Runs one stage. Failures are recorded on the stage and later stages continue; after cancellation every later stage is marked cancelled.</summary>
    public void Run(string id, Func<Action<long, long, string>, StageResult> stage)
    {
        int index = records.FindIndex(r => r.Id == id);
        var record = records[index] with { Status = StageStatus.Running, StartedUtc = DateTimeOffset.UtcNow };
        records[index] = record;
        if (Cancelled) { Finish(new(StageStatus.Cancelled, "Import was cancelled before this stage.")); return; }
        progress?.Report(new(id, record.Name, record.Name + "..."));
        int warnings = Log.Count(LogSeverity.Warning), errors = Log.Count(LogSeverity.Error);
        StageResult result;
        try
        {
            result = stage((done, total, message) => progress?.Report(new(id, record.Name, message, done, total)));
        }
        catch (OperationCanceledException)
        {
            Cancelled = true;
            result = new(StageStatus.Cancelled, "Cancelled; completed outputs were kept and nothing incomplete was published.");
        }
        catch (Exception e) when (PrivateStorage.Recoverable(e) || e is KeyNotFoundException)
        {
            Log.Error(id, record.Name + " failed: " + e.Message, exception: e);
            result = new(StageStatus.Failed, e.Message);
        }
        // Skipped is informational (deselected or not applicable); a stage that wants attention adds a Warning check.
        if (result.Status is StageStatus.Succeeded or StageStatus.Reused or StageStatus.Skipped) Log.Info(id, result.Message);
        else if (result.Status is StageStatus.NotSupported or StageStatus.Warning or StageStatus.Cancelled) Log.Warning(id, result.Message);
        checks.AddRange(result.AllChecks);
        Finish(result, Log.Count(LogSeverity.Warning) - warnings, Log.Count(LogSeverity.Error) - errors);

        void Finish(StageResult r, int w = 0, int e = 0)
        {
            records[index] = records[index] with
            {
                Status = r.Status, FinishedUtc = DateTimeOffset.UtcNow, Processed = r.Processed, Total = r.Total, Message = r.Message,
                Warnings = w, Errors = e, CacheKey = r.Output?.CacheKey, OutputDirectory = r.Output?.Directory
            };
            progress?.Report(new(id, records[index].Name, r.Message, r.Processed, r.Total));
        }
    }

    public StageStatus StatusOf(string id) => records.Single(r => r.Id == id).Status;

    public ImportStatus OverallStatus()
    {
        if (Cancelled) return ImportStatus.Cancelled;
        if (StatusOf(Stages.SourceStage.Id) != StageStatus.Succeeded ||
            StatusOf(Stages.CatalogStage.Id) is not (StageStatus.Succeeded or StageStatus.Reused)) return ImportStatus.Failed;
        if (records.Any(r => r.Status == StageStatus.Failed) || checks.Any(c => c.Status == CheckStatus.Failed)) return ImportStatus.Partial;
        return Log.Count(LogSeverity.Warning) > 0 || checks.Any(c => c.Status == CheckStatus.Warning)
            ? ImportStatus.SucceededWithWarnings : ImportStatus.Succeeded;
    }

    public ImportManifest BuildManifest(ImportStatus? status = null) => new(ImportManifest.CurrentSchema, Id, ToolIdentity.ImporterVersion,
        ToolIdentity.DecoderSha256, StartedUtc, DateTimeOffset.UtcNow, status ?? OverallStatus(), Source, Options, records.ToArray(), Outputs,
        Log.Count(LogSeverity.Warning), Log.Count(LogSeverity.Error));
}
