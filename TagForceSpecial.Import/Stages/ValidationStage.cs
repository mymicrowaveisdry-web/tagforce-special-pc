using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Import.Stages;

/// <summary>Collects every stage's checks plus manifest completeness into the session's validation report and its readable summary.</summary>
public static class ValidationStage
{
    public const string Id = "validation";
    public const int ReportSchema = 1;

    public static StageResult Run(ImportSession session)
    {
        var checks = session.Checks.ToList();
        var requested = new (string Stage, bool Wanted, StageOutput? Output)[]
        {
            (CatalogStage.Id, session.Options.Catalog, session.Outputs.Catalog),
            (PresentationStage.Id, session.Options.Presentation, session.Outputs.Presentation),
        };
        foreach (var (stage, wanted, output) in requested)
            checks.Add(new($"manifest.{stage}-output", CheckCategory.Reproducibility, wanted ? CheckSeverity.Error : CheckSeverity.Info,
                !wanted ? CheckStatus.Skipped : output is null ? CheckStatus.Failed : CheckStatus.Passed, stage,
                "A requested stage recorded its output directory, cache key, tool version and per-file hashes.",
                output is null ? null : $"{output.Artifacts.Length} files, key {output.CacheKey[..12]}"));
        checks.Add(new("reproducibility.repeat-import", CheckCategory.Reproducibility, CheckSeverity.Info, CheckStatus.NotSupported, null,
            "Byte-identical repeat imports are checked by comparing two manifests in the workbench, not within one session."));
        var report = new ValidationReport(ReportSchema, session.Id, DateTimeOffset.UtcNow, ToolIdentity.ImporterVersion, session.Source, checks.ToArray());
        session.Storage.WriteBytes(ImportLayout.SessionValidation(session.Id), ManifestJson.Serialize(report));
        session.Storage.WriteBytes(ImportLayout.SessionReport(session.Id), System.Text.Encoding.UTF8.GetBytes(ValidationReportWriter.Markdown(report)));
        int failed = report.Count(CheckStatus.Failed), warned = report.Count(CheckStatus.Warning);
        string summary = $"{report.Count(CheckStatus.Passed)} passed, {warned} warnings, {failed} failed, " +
            $"{report.Count(CheckStatus.Skipped)} skipped, {report.Count(CheckStatus.NotSupported)} not supported.";
        return new(failed > 0 ? StageStatus.Failed : warned > 0 ? StageStatus.Warning : StageStatus.Succeeded, summary,
            Processed: checks.Count, Total: checks.Count);
    }
}
