using System.Text.Json.Serialization;
using TagForceSpecial.Import.Manifest;

namespace TagForceSpecial.Import.Validation;

/// <summary>Skipped and NotSupported are never counted as passed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CheckStatus>))]
public enum CheckStatus { Passed, Warning, Failed, Skipped, NotSupported }

[JsonConverter(typeof(JsonStringEnumConverter<CheckSeverity>))]
public enum CheckSeverity { Info, Warning, Error }

/// <summary>Validation categories from the importer guide; each check names exactly one.</summary>
public static class CheckCategory
{
    public const string Source = "source-integrity";
    public const string Archive = "archive-integrity";
    public const string Asset = "asset-integrity";
    public const string Normalized = "normalized-data";
    public const string Reproducibility = "import-reproducibility";
    public const string Evidence = "reverse-engineering-evidence";
    public static readonly string[] All = [Source, Archive, Asset, Normalized, Reproducibility, Evidence];
}

public sealed record ValidationCheck(string Id, string Category, CheckSeverity Severity, CheckStatus Status,
    string? Resource, string Explanation, string? Details = null, string? Remediation = null);

public sealed record ValidationReport(int SchemaVersion, string SessionId, DateTimeOffset CreatedUtc, string ToolVersion,
    SourceIdentity Source, ValidationCheck[] Checks)
{
    public int Count(CheckStatus status) => Checks.Count(c => c.Status == status);
}
