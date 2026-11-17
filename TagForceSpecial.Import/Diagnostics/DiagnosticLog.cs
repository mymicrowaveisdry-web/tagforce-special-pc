using System.Text.Json;
using System.Text.Json.Serialization;

namespace TagForceSpecial.Import.Diagnostics;

[JsonConverter(typeof(JsonStringEnumConverter<LogSeverity>))]
public enum LogSeverity { Debug, Info, Warning, Error }

/// <param name="Resource">Source path, asset key or stage output the entry is about, when there is one.</param>
public sealed record LogEntry(DateTimeOffset TimestampUtc, LogSeverity Severity, string Component, string SessionId,
    string Message, string? Resource = null, string? Exception = null);

/// <summary>Thread-safe structured log of one import session, kept in memory and appended to a JSON Lines file.</summary>
public sealed class DiagnosticLog : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly object gate = new();
    private readonly List<LogEntry> entries = [];
    private readonly StreamWriter? writer;
    public string SessionId { get; }
    public event Action<LogEntry>? Written;

    public DiagnosticLog(string sessionId, string? path = null)
    {
        SessionId = sessionId;
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        writer = new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
    }

    public IReadOnlyList<LogEntry> Entries { get { lock (gate) return entries.ToArray(); } }
    public int Count(LogSeverity severity) { lock (gate) return entries.Count(e => e.Severity == severity); }

    public void Info(string component, string message, string? resource = null) => Add(LogSeverity.Info, component, message, resource);
    public void Warning(string component, string message, string? resource = null) => Add(LogSeverity.Warning, component, message, resource);
    public void Error(string component, string message, string? resource = null, Exception? exception = null) =>
        Add(LogSeverity.Error, component, message, resource, exception);

    public void Add(LogSeverity severity, string component, string message, string? resource = null, Exception? exception = null)
    {
        // Exception text only: stack traces can expose machine paths in shared diagnostics.
        var entry = new LogEntry(DateTimeOffset.UtcNow, severity, component, SessionId, message, resource,
            exception is null ? null : exception.GetType().Name + ": " + exception.Message);
        lock (gate)
        {
            entries.Add(entry);
            writer?.WriteLine(JsonSerializer.Serialize(entry, Json));
        }
        Written?.Invoke(entry);
    }

    /// <summary>Reads a session log written by this class; unreadable lines are skipped and counted.</summary>
    public static (IReadOnlyList<LogEntry> Entries, int Unreadable) Read(string path, int maximumEntries = 200_000)
    {
        var list = new List<LogEntry>(); int bad = 0;
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        for (string? line; (line = reader.ReadLine()) is not null && list.Count < maximumEntries;)
        {
            try { if (JsonSerializer.Deserialize<LogEntry>(line, Json) is { } entry) list.Add(entry); else bad++; }
            catch (JsonException) { bad++; }
        }
        return (list, bad);
    }

    public void Dispose() { lock (gate) writer?.Dispose(); }
}
