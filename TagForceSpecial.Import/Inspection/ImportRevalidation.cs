using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Import.Inspection;

/// <summary>Rechecks generated artifact integrity without rewriting the historical import result or activating a session.</summary>
public static class ImportRevalidation
{
    public static ValidationReport Run(PrivateStorage storage, ImportManifest manifest)
    {
        var checks = new List<ValidationCheck>();
        foreach (var output in ImportComparison.Outputs(manifest))
        {
            try
            {
                ImportInspection.Verify(storage, output);
                if (output.Stage == "catalog") _ = ImportInspection.Cards(storage, manifest);
                checks.Add(new("revalidate." + output.Stage, CheckCategory.Reproducibility, CheckSeverity.Info, CheckStatus.Passed,
                    output.Directory, "Generated artifacts match their recorded lengths and SHA-256 hashes."));
            }
            catch (Exception e) when (PrivateStorage.Recoverable(e))
            { checks.Add(new("revalidate." + output.Stage, CheckCategory.Reproducibility, CheckSeverity.Error, CheckStatus.Failed, output.Directory, e.Message, Remediation: "Repeat the import to rebuild invalid outputs.")); }
        }
        checks.Add(new("revalidate.source", CheckCategory.Source, CheckSeverity.Info, CheckStatus.Skipped,
            manifest.Source.DisplayName, "Original source verification requires a repeat import; it was not repeated by this artifact check."));
        return new(1, manifest.SessionId, DateTimeOffset.UtcNow, ToolIdentity.ImporterVersion, manifest.Source, checks.ToArray());
    }
}
