using System.Text;

namespace TagForceSpecial.Import.Validation;

/// <summary>Readable Markdown summary of a validation report. Source paths are reduced to file names so the summary can be shared.</summary>
public static class ValidationReportWriter
{
    public static string Markdown(ValidationReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# Import validation report").AppendLine();
        text.AppendLine($"- Session: `{report.SessionId}`");
        text.AppendLine($"- Created: {report.CreatedUtc:u}");
        text.AppendLine($"- Importer: {report.ToolVersion}");
        text.AppendLine($"- Source: {report.Source.DisplayName} ({report.Source.Kind}), build {report.Source.BuildId ?? "unidentified"}");
        text.AppendLine($"- Recognized: {YesNo(report.Source.Recognized)}; structurally valid: {YesNo(report.Source.StructurallyValid)}; supported: {YesNo(report.Source.Supported)}");
        text.AppendLine();
        text.AppendLine("| Status | Count |").AppendLine("|---|---|");
        foreach (var status in Enum.GetValues<CheckStatus>()) text.AppendLine($"| {status} | {report.Count(status)} |");
        foreach (string category in CheckCategory.All)
        {
            var rows = report.Checks.Where(c => c.Category == category).ToArray();
            if (rows.Length == 0) continue;
            text.AppendLine().AppendLine($"## {category}").AppendLine();
            text.AppendLine("| Check | Status | Resource | Explanation | Details |").AppendLine("|---|---|---|---|---|");
            foreach (var c in rows)
                text.AppendLine($"| `{c.Id}` | {c.Status} | {Cell(c.Resource)} | {Cell(c.Explanation)} | {Cell(c.Details)}{(c.Remediation is null ? "" : " Fix: " + Cell(c.Remediation))} |");
        }
        return text.ToString();
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
    private static string Cell(string? value) => (value ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
